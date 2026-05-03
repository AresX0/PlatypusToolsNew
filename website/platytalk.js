/**
 * Platytalk web client.
 *  - Microsoft sign-in (browser redirect → fragment token)
 *  - Local X25519/P-256 ECDH + ECDSA-P256 key generation, stored in IndexedDB
 *  - REST + WebSocket against the same origin
 *  - Per-message AES-256-GCM with HKDF-derived key, ephemeral ECDH per send
 */
(() => {
  const $ = (id) => document.getElementById(id);
  const yr = $('yr'); if (yr) yr.textContent = new Date().getFullYear();

  const RELAY = location.origin;
  const WS_URL = (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws';

  const DB_NAME = 'platytalk', STORE = 'kv';
  function openDb() {
    return new Promise((res, rej) => {
      const r = indexedDB.open(DB_NAME, 1);
      r.onupgradeneeded = () => r.result.createObjectStore(STORE);
      r.onsuccess = () => res(r.result);
      r.onerror = () => rej(r.error);
    });
  }
  async function kvGet(k) { const db = await openDb(); return new Promise((res) => { const tx = db.transaction(STORE).objectStore(STORE).get(k); tx.onsuccess = () => res(tx.result); }); }
  async function kvSet(k, v) { const db = await openDb(); return new Promise((res) => { const tx = db.transaction(STORE, 'readwrite'); tx.objectStore(STORE).put(v, k); tx.oncomplete = () => res(); }); }
  async function kvDel(k) { const db = await openDb(); return new Promise((res) => { const tx = db.transaction(STORE, 'readwrite'); tx.objectStore(STORE).delete(k); tx.oncomplete = () => res(); }); }

  const subtle = crypto.subtle;
  const enc = new TextEncoder(), dec = new TextDecoder();
  const b64 = (buf) => btoa(String.fromCharCode(...new Uint8Array(buf)));
  const unb64 = (s) => Uint8Array.from(atob(s), c => c.charCodeAt(0));

  async function genIdentity() {
    const ec = await subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
    const sg = await subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
    return {
      identityPubSpki: b64(await subtle.exportKey('spki', ec.publicKey)),
      identityPrivPkcs8: b64(await subtle.exportKey('pkcs8', ec.privateKey)),
      signingPubSpki: b64(await subtle.exportKey('spki', sg.publicKey)),
      signingPrivPkcs8: b64(await subtle.exportKey('pkcs8', sg.privateKey)),
    };
  }

  async function deriveAesKey(myPrivPkcs8B64, peerPubSpkiB64, info) {
    const priv = await subtle.importKey('pkcs8', unb64(myPrivPkcs8B64), { name: 'ECDH', namedCurve: 'P-256' }, false, ['deriveBits']);
    const pub = await subtle.importKey('spki', unb64(peerPubSpkiB64), { name: 'ECDH', namedCurve: 'P-256' }, false, []);
    const shared = await subtle.deriveBits({ name: 'ECDH', public: pub }, priv, 256);
    const baseKey = await subtle.importKey('raw', shared, 'HKDF', false, ['deriveBits']);
    const keyBits = await subtle.deriveBits({ name: 'HKDF', hash: 'SHA-256', salt: enc.encode('platytalk/v1'), info: enc.encode(info) }, baseKey, 256);
    return subtle.importKey('raw', keyBits, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
  }

  async function encryptToPeer(plaintext, peerPubSpkiB64, contextInfo) {
    const eph = await subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
    const ephPubSpki = b64(await subtle.exportKey('spki', eph.publicKey));
    const ephPrivPkcs8 = b64(await subtle.exportKey('pkcs8', eph.privateKey));
    const aes = await deriveAesKey(ephPrivPkcs8, peerPubSpkiB64, contextInfo);
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const ct = await subtle.encrypt({ name: 'AES-GCM', iv, additionalData: enc.encode(contextInfo) }, aes, enc.encode(plaintext));
    const blob = new Uint8Array(12 + ct.byteLength);
    blob.set(iv, 0); blob.set(new Uint8Array(ct), 12);
    return { cipherBlob: b64(blob), ephemeralPublic: ephPubSpki };
  }

  async function decryptFromPeer(cipherBlobB64, ephemeralPubSpkiB64, myIdentityPrivPkcs8, contextInfo) {
    const aes = await deriveAesKey(myIdentityPrivPkcs8, ephemeralPubSpkiB64, contextInfo);
    const buf = unb64(cipherBlobB64);
    const iv = buf.slice(0, 12), ct = buf.slice(12);
    const pt = await subtle.decrypt({ name: 'AES-GCM', iv, additionalData: enc.encode(contextInfo) }, aes, ct);
    return dec.decode(pt);
  }

  const state = {
    token: null, me: null, identity: null, contacts: [], conversations: {},
    selected: null, ws: null,
  };

  function setSignedInUi() {
    const si = $('ptk-signin'); const so = $('ptk-signout');
    if (si) si.hidden = !!state.token;
    if (so) so.hidden = !state.token;
    const inviteBox = $('ptk-invite-box');
    if (inviteBox) inviteBox.hidden = !state.token;
    const editBtn = $('ptk-edit-handle');
    if (editBtn) editBtn.hidden = !state.token;
    const editor = $('ptk-handle-editor');
    if (editor && !state.token) editor.hidden = true;
    document.querySelectorAll('.signed-out-only').forEach(el => { el.hidden = !!state.token; });
    const me = $('ptk-me');
    if (me) {
      if (state.me) {
        me.innerHTML = `<div class="me-name">${escapeHtml(state.me.displayName)}</div><div class="me-handle">@${escapeHtml(state.me.handle)}</div>`;
      } else {
        me.innerHTML = `<div class="me-name">not signed in</div><div class="me-handle">sign in with your Microsoft account</div>`;
      }
    }
    if (state.me) renderInviteQr();
  }

  function escapeHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  function renderInviteQr() {
    const handleEl = $('ptk-invite-handle');
    const qrEl = $('ptk-qr');
    if (!state.me || !handleEl || !qrEl) return;
    const handle = state.me.handle || '';
    const inviteUrl = `${location.origin}/?add=${encodeURIComponent(handle)}`;
    handleEl.textContent = '@' + handle;
    qrEl.innerHTML = '';
    if (typeof QRCode !== 'undefined' && QRCode.toCanvas) {
      const c = document.createElement('canvas');
      qrEl.appendChild(c);
      QRCode.toCanvas(c, inviteUrl, { width: 156, margin: 1, color: { dark: '#00e5ff', light: '#04070d' } }, (err) => {
        if (err) qrEl.textContent = inviteUrl;
      });
    } else {
      qrEl.textContent = inviteUrl;
    }
    const copyBtn = $('ptk-copy-invite');
    if (copyBtn) copyBtn.onclick = () => { navigator.clipboard?.writeText(inviteUrl); copyBtn.textContent = 'Copied!'; setTimeout(() => copyBtn.textContent = 'Copy invite link', 1500); };
  }

  async function api(path, opts = {}) {
    const r = await fetch(RELAY + path, {
      ...opts,
      headers: { 'Content-Type': 'application/json', ...(opts.headers || {}), ...(state.token ? { Authorization: 'Bearer ' + state.token } : {}) },
    });
    if (!r.ok) throw new Error(`${r.status} ${path}`);
    return r.status === 204 ? null : r.json();
  }

  async function ensureIdentity() {
    let id = await kvGet('identity');
    if (!id) { id = await genIdentity(); await kvSet('identity', id); }
    state.identity = id;
    await api('/v1/keys/identity', { method: 'POST', body: JSON.stringify({ identityKeyPublic: id.identityPubSpki, signingKeyPublic: id.signingPubSpki }) });
  }

  async function refreshContacts() {
    const r = await api('/v1/contacts'); state.contacts = r.contacts || [];
    const ul = $('ptk-contacts'); if (!ul) return;
    ul.innerHTML = '';
    for (const c of state.contacts) {
      const li = document.createElement('li');
      li.textContent = '@' + c.handle;
      li.onclick = () => selectConversation(c);
      ul.appendChild(li);
    }
  }

  function appendMessage(direction, text, when) {
    const ol = $('ptk-msgs'); if (!ol) return;
    const li = document.createElement('li');
    li.className = direction === 'out' ? 'out' : 'in';
    li.innerHTML = `<div class="bubble">${text.replace(/[<&>]/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;' }[c]))}</div><time>${new Date(when).toLocaleTimeString()}</time>`;
    ol.appendChild(li); ol.scrollTop = ol.scrollHeight;
  }

  function selectConversation(contact) {
    state.selected = contact;
    $('ptk-title').textContent = '@' + contact.handle;
    $('ptk-msgs').innerHTML = '';
    const buf = state.conversations[contact.id] || [];
    for (const m of buf) appendMessage(m.direction, m.text, m.when);
  }

  async function send() {
    if (!state.selected) return;
    const txt = $('ptk-msg').value.trim(); if (!txt) return;
    $('ptk-msg').value = '';
    const peer = state.selected;
    if (!peer.identityKeyPublic) { alert('Contact has no key yet — they need to sign in once.'); return; }
    const messageId = crypto.randomUUID();
    const conversationId = [state.me.id, peer.id].sort().join(':');
    const ctx = `${conversationId}|${messageId}`;
    const { cipherBlob, ephemeralPublic } = await encryptToPeer(txt, peer.identityKeyPublic, ctx);
    await api('/v1/messages/send', {
      method: 'POST', body: JSON.stringify({
        messageId, conversationId,
        recipients: [{ userId: peer.id, cipherBlob, ephemeralPublic }],
        cipherBlob, ephemeralPublic, counter: 0,
      }),
    });
    state.conversations[peer.id] = state.conversations[peer.id] || [];
    state.conversations[peer.id].push({ direction: 'out', text: txt, when: Date.now() });
    appendMessage('out', txt, Date.now());
  }

  async function handleEnvelope(env) {
    try {
      const ctx = `${env.conversationId}|${env.messageId}`;
      const text = await decryptFromPeer(env.cipherBlob, env.ephemeralPublic, state.identity.identityPrivPkcs8, ctx);
      const contact = state.contacts.find(c => c.id === env.senderId) || { id: env.senderId, handle: 'unknown', displayName: 'unknown' };
      state.conversations[contact.id] = state.conversations[contact.id] || [];
      state.conversations[contact.id].push({ direction: 'in', text, when: env.timestampMs });
      if (state.selected && state.selected.id === contact.id) appendMessage('in', text, env.timestampMs);
      try { state.ws?.send(JSON.stringify({ type: 'ack', messageIds: [env.messageId + ':' + state.me.id] })); } catch {}
    } catch (e) { console.warn('decrypt failed', e); }
  }

  function connectWs() {
    if (!state.token) return;
    const ws = new WebSocket(`${WS_URL}?token=${encodeURIComponent(state.token)}`);
    state.ws = ws;
    ws.onmessage = (ev) => {
      let m; try { m = JSON.parse(ev.data); } catch { return; }
      if (m.type === 'envelope') handleEnvelope(m);
      else if (m.type === 'tombstone') {
        for (const k of Object.keys(state.conversations)) state.conversations[k] = state.conversations[k].filter(x => x.messageId !== m.messageId);
      }
    };
    ws.onclose = () => setTimeout(connectWs, 2000);
  }

  async function bootstrapSignedIn(token) {
    state.token = token;
    await kvSet('ptk_token', token);
    state.me = await api('/v1/me');
    setSignedInUi();
    await ensureIdentity();
    state.me = await api('/v1/me');
    await refreshContacts();
    connectWs();
  }

  function checkFragment() {
    if (location.hash.startsWith('#ptk=')) {
      const params = new URLSearchParams(location.hash.slice(1));
      const t = params.get('ptk');
      history.replaceState(null, '', location.pathname + location.search);
      if (t) bootstrapSignedIn(t);
    }
  }

  async function tryRestore() {
    const t = await kvGet('ptk_token'); if (!t) return;
    try { state.token = t; state.me = await api('/v1/me'); setSignedInUi(); await ensureIdentity(); state.me = await api('/v1/me'); await refreshContacts(); connectWs(); }
    catch { await kvDel('ptk_token'); state.token = null; setSignedInUi(); }
  }

  async function signOut() {
    state.token = null; state.me = null; state.identity = null; state.contacts = []; state.conversations = {};
    await kvDel('ptk_token');
    try { state.ws?.close(); } catch {}
    setSignedInUi();
    if ($('ptk-contacts')) $('ptk-contacts').innerHTML = '';
    if ($('ptk-msgs')) $('ptk-msgs').innerHTML = '';
    if ($('ptk-title')) $('ptk-title').textContent = 'No conversation selected';
  }

  // Sign-in is an <a href="/auth/start?returnTo=/"> so it works even if JS fails to load.
  // We still bind onclick to ensure it works if some framework intercepts the anchor.
  const signinEl = $('ptk-signin');
  if (signinEl && signinEl.tagName !== 'A') {
    signinEl.onclick = () => { location.href = '/auth/start?returnTo=/'; };
  }

  // Edit handle (consistent across desktop + web).
  const editBtn = $('ptk-edit-handle');
  const editor = $('ptk-handle-editor');
  const editInput = $('ptk-handle-input');
  const editSave = $('ptk-handle-save');
  const editCancel = $('ptk-handle-cancel');
  const editErr = $('ptk-handle-err');
  if (editBtn && editor) {
    editBtn.onclick = () => {
      editor.hidden = false;
      if (editInput && state.me) editInput.value = state.me.handle || '';
      if (editErr) editErr.textContent = '';
      editInput?.focus();
    };
    editCancel.onclick = () => { editor.hidden = true; if (editErr) editErr.textContent = ''; };
    editSave.onclick = async () => {
      const newHandle = (editInput.value || '').trim().toLowerCase();
      if (!/^[a-z0-9][a-z0-9-]{2,23}$/.test(newHandle)) {
        editErr.textContent = 'lowercase a-z, 0-9, dashes only; 3-24 chars';
        return;
      }
      try {
        const r = await api('/v1/me/handle', { method: 'PUT', body: JSON.stringify({ handle: newHandle }) });
        if (r.token) { state.token = r.token; await kvSet('ptk_token', r.token); }
        state.me = await api('/v1/me');
        editor.hidden = true;
        editErr.textContent = '';
        setSignedInUi();
      } catch (e) {
        editErr.textContent = String(e.message || e).includes('409') ? 'handle already taken' : 'could not update handle';
      }
    };
  }
  if ($('ptk-signout')) $('ptk-signout').onclick = signOut;
  if ($('ptk-add-btn')) $('ptk-add-btn').onclick = async () => {
    const h = $('ptk-add').value.trim().replace(/^@/, ''); if (!h) return;
    try {
      const r = await api('/v1/contacts/add', { method: 'POST', body: JSON.stringify({ handle: h }) });
      $('ptk-add').value = '';
      await refreshContacts();
      const c = state.contacts.find(c => c.id === r.contact.id);
      if (c) selectConversation(c);
    } catch (e) { alert('Could not add: ' + e.message); }
  };
  if ($('ptk-send')) $('ptk-send').onclick = send;
  const msgInput = $('ptk-msg'); if (msgInput) msgInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); send(); } });

  // Auto-add by handle from invite link (?add=handle)
  try {
    const u = new URL(location.href);
    const add = u.searchParams.get('add');
    if (add && $('ptk-add')) {
      $('ptk-add').value = add;
      u.searchParams.delete('add');
      history.replaceState(null, '', u.pathname + (u.search || '') + u.hash);
    }
  } catch {}

  setSignedInUi();
  checkFragment();
  tryRestore();
})();
