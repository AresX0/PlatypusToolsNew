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
    const renderText = () => {
      // Compact fallback: show only the handle, not the full URL, so it stays in the box.
      qrEl.innerHTML = `<div style="padding:8px;color:#7fd6e8;font-size:11px;word-break:break-all;">@${escapeHtml(handle)}</div>`;
    };
    const tryRender = (attempt) => {
      // npm `qrcode` (preferred): exposes QRCode.toCanvas function.
      if (typeof QRCode !== 'undefined' && typeof QRCode.toCanvas === 'function') {
        const c = document.createElement('canvas');
        qrEl.appendChild(c);
        QRCode.toCanvas(c, inviteUrl, { width: 156, margin: 1, color: { dark: '#00e5ff', light: '#04070d' } }, (err) => {
          if (err) renderText();
        });
        return;
      }
      // davidshimjs/qrcodejs fallback: constructor-based.
      if (typeof QRCode !== 'undefined' && QRCode.prototype && typeof QRCode.prototype.makeCode === 'function') {
        try {
          new QRCode(qrEl, { text: inviteUrl, width: 156, height: 156, colorDark: '#00e5ff', colorLight: '#04070d' });
          return;
        } catch (e) { /* fall through */ }
      }
      if (attempt < 30) {
        setTimeout(() => tryRender(attempt + 1), 150);
      } else {
        renderText();
      }
    };
    tryRender(0);
    const urlEl = $('ptk-invite-url');
    if (urlEl) urlEl.textContent = inviteUrl;
    const copyBtn = $('ptk-copy-invite');
    if (copyBtn) copyBtn.onclick = async () => {
      try { await navigator.clipboard.writeText(inviteUrl); }
      catch { /* clipboard API may be blocked; fall back below */ }
      const orig = '📋 Copy invite link';
      copyBtn.textContent = '✓ Copied!';
      setTimeout(() => copyBtn.textContent = orig, 1600);
    };
    // Native share sheet (mobile / supported browsers).
    const shareBtn = $('ptk-share-invite');
    if (shareBtn) {
      if (navigator.share) {
        shareBtn.hidden = false;
        shareBtn.onclick = () => navigator.share({
          title: 'Add me on Platytalk',
          text: `Add me on Platytalk: @${handle}`,
          url: inviteUrl,
        }).catch(() => {});
      } else {
        shareBtn.hidden = true;
      }
    }
    // Click QR (or "enlarge") to open modal with a bigger version.
    const modal = document.getElementById('ptk-qr-modal');
    const modalImg = document.getElementById('ptk-qr-modal-img');
    const modalHandle = document.getElementById('ptk-qr-modal-handle');
    const modalUrl = document.getElementById('ptk-qr-modal-url');
    const modalClose = document.getElementById('ptk-qr-modal-close');
    if (modal && modalImg) {
      const openModal = () => {
        modalImg.innerHTML = '';
        if (typeof QRCode !== 'undefined' && typeof QRCode.toCanvas === 'function') {
          const c = document.createElement('canvas');
          modalImg.appendChild(c);
          QRCode.toCanvas(c, inviteUrl, { width: 480, margin: 2, color: { dark: '#00e5ff', light: '#04070d' } }, () => {});
        } else if (typeof QRCode !== 'undefined' && QRCode.prototype && QRCode.prototype.makeCode) {
          try { new QRCode(modalImg, { text: inviteUrl, width: 480, height: 480, colorDark: '#00e5ff', colorLight: '#04070d' }); } catch {}
        }
        if (modalHandle) modalHandle.textContent = '@' + handle;
        if (modalUrl) modalUrl.textContent = inviteUrl;
        modal.classList.add('show');
      };
      qrEl.onclick = openModal;
      modalClose && (modalClose.onclick = (e) => { e.stopPropagation(); modal.classList.remove('show'); });
      modal.onclick = (e) => { if (e.target === modal) modal.classList.remove('show'); };
    }
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

  function appendMessage(direction, text, when, messageId) {
    const ol = $('ptk-msgs'); if (!ol) return;
    const li = document.createElement('li');
    li.className = direction === 'out' ? 'out' : 'in';
    if (messageId) li.dataset.messageId = messageId;
    li.innerHTML = `<div class="bubble">${text.replace(/[<&>]/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;' }[c]))}</div><time>${new Date(when).toLocaleTimeString()}</time>`;
    ol.appendChild(li); ol.scrollTop = ol.scrollHeight;
    // ----- Disappearing-message scheduling -----
    // If a TTL is set on this conversation, schedule a tombstone after the TTL.
    // OUT: we both delete locally AND notify the server so the recipient is tombstoned.
    // IN:  we delete locally; the sender already scheduled a server-side tombstone for symmetry.
    if (state.selected && messageId) {
      const cid = [state.me.id, state.selected.id].sort().join(':');
      const ttl = state.convSettings && state.convSettings[cid];
      if (Number.isInteger(ttl) && ttl > 0) {
        const ageMs = Math.max(0, Date.now() - (when || Date.now()));
        const remainMs = Math.max(500, ttl * 1000 - ageMs);
        scheduleExpire(direction, messageId, state.selected.id, cid, remainMs);
      }
    }
  }

  function scheduleExpire(direction, messageId, peerId, conversationId, delayMs) {
    state._expireTimers = state._expireTimers || {};
    if (state._expireTimers[messageId]) return; // already scheduled
    state._expireTimers[messageId] = setTimeout(async () => {
      delete state._expireTimers[messageId];
      // Strip from local in-memory store + DOM regardless of network outcome.
      if (state.conversations[peerId]) {
        state.conversations[peerId] = state.conversations[peerId].filter(x => x.messageId !== messageId);
      }
      const ol = $('ptk-msgs');
      if (ol) [...ol.children].forEach(li => { if (li.dataset.messageId === messageId) li.remove(); });
      if (direction === 'out') {
        // Server-side tombstone so the recipient also deletes (live via WS, or on next connect).
        try {
          await api('/v1/messages/delete', {
            method: 'POST',
            body: JSON.stringify({ messageId, conversationId, recipients: [{ userId: peerId }, { userId: state.me.id }] }),
          });
        } catch (e) { console.warn('tombstone failed', e); }
      }
    }, delayMs);
  }

  async function selectConversation(contact) {
    state.selected = contact;
    $('ptk-title').textContent = '@' + contact.handle;
    $('ptk-msgs').innerHTML = '';
    refreshDisappearingLabel();
    // Pull the server-side TTL FIRST so disappearing-message scheduling works even
    // if the user immediately taps Send (otherwise on slow mobile networks the
    // settings response races the send and TTL is missed for the first message).
    try {
      const cid = [state.me.id, contact.id].sort().join(':');
      const s = await api(`/v1/conversations/${encodeURIComponent(cid)}/settings`);
      if (s && Number.isInteger(s.disappearingSeconds)) {
        state.convSettings[cid] = s.disappearingSeconds;
        refreshDisappearingLabel();
      }
    } catch {}
    // Render after settings arrive so each appendMessage sees the correct TTL.
    const buf = state.conversations[contact.id] || [];
    for (const m of buf) appendMessage(m.direction, m.text, m.when, m.messageId);
  }

  // ---------- Disappearing-message TTL (per conversation) ------------
  state.convSettings = state.convSettings || {};
  function ttlLabelShort(s) {
    if (!s) return 'off';
    if (s < 60) return s + 's';
    if (s < 3600) return ((s / 60) | 0) + 'm';
    if (s < 86400) return ((s / 3600) | 0) + 'h';
    if (s < 604800) return ((s / 86400) | 0) + 'd';
    return ((s / 604800) | 0) + 'w';
  }
  function refreshDisappearingLabel() {
    const btn = $('ptk-disappear'); if (!btn) return;
    if (!state.selected) { btn.textContent = 'Disappearing: off'; return; }
    const convId = [state.me.id, state.selected.id].sort().join(':');
    const s = state.convSettings[convId] || 0;
    btn.textContent = 'Disappearing: ' + ttlLabelShort(s);
  }
  async function cycleDisappearing() {
    if (!state.selected) { alert('Select a conversation first.'); return; }
    // Browsers gate Notification.requestPermission() to a user gesture; piggyback on this click.
    try { if (typeof Notification !== 'undefined' && Notification.permission === 'default') Notification.requestPermission().catch(()=>{}); } catch {}
    const convId = [state.me.id, state.selected.id].sort().join(':');
    const opts = [0, 30, 300, 3600, 28800, 86400, 604800, 2419200];
    const cur = state.convSettings[convId] || 0;
    const next = opts[(opts.indexOf(cur) + 1) % opts.length];
    try {
      await api(`/v1/conversations/${encodeURIComponent(convId)}/settings`, {
        method: 'PUT', body: JSON.stringify({ disappearingSeconds: next }),
      });
      state.convSettings[convId] = next;
      refreshDisappearingLabel();
    } catch (e) {
      // Fall back to a local-only setting if the endpoint isn't supported on this server.
      state.convSettings[convId] = next;
      refreshDisappearingLabel();
    }
  }

  async function send() {
    if (!state.selected) return;
    // Latch notification permission on a real user gesture (most browsers block silent prompts).
    try { if (typeof Notification !== 'undefined' && Notification.permission === 'default') Notification.requestPermission().catch(()=>{}); } catch {}
    const txt = $('ptk-msg').value.trim(); if (!txt) return;
    $('ptk-msg').value = '';
    const peer = state.selected;
    if (!peer.identityKeyPublic) { alert('Contact has no key yet — they need to sign in once.'); return; }
    const messageId = crypto.randomUUID();
    const conversationId = [state.me.id, peer.id].sort().join(':');
    // Backstop: if we haven't loaded the conversation's TTL yet (e.g. user sent before
    // selectConversation's settings fetch returned), pull it now so the disappearing
    // timer is scheduled for this very first message.
    if (state.convSettings[conversationId] === undefined) {
      try {
        const s = await api(`/v1/conversations/${encodeURIComponent(conversationId)}/settings`);
        if (s && Number.isInteger(s.disappearingSeconds)) state.convSettings[conversationId] = s.disappearingSeconds;
      } catch {}
    }
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
    state.conversations[peer.id].push({ direction: 'out', text: txt, when: Date.now(), messageId });
    appendMessage('out', txt, Date.now(), messageId);
  }

  async function handleEnvelope(env) {
    try {
      // Dedupe: server can re-flush pending envelopes on reconnect (and a stale WS
      // can briefly co-exist with the new one), which would otherwise render the
      // same message twice. Track every messageId we've already processed.
      state._seenEnvelopes = state._seenEnvelopes || new Set();
      if (state._seenEnvelopes.has(env.messageId)) {
        // Still ACK so the server stops re-flushing this one to us.
        try { state.ws?.send(JSON.stringify({ type: 'ack', messageIds: [env.messageId + ':' + state.me.id] })); } catch {}
        return;
      }
      state._seenEnvelopes.add(env.messageId);
      const ctx = `${env.conversationId}|${env.messageId}`;
      const text = await decryptFromPeer(env.cipherBlob, env.ephemeralPublic, state.identity.identityPrivPkcs8, ctx);
      const contact = state.contacts.find(c => c.id === env.senderId) || { id: env.senderId, handle: 'unknown', displayName: 'unknown' };
      state.conversations[contact.id] = state.conversations[contact.id] || [];
      state.conversations[contact.id].push({ direction: 'in', text, when: env.timestampMs, messageId: env.messageId });
      // Schedule local TTL expiry for incoming messages too, so both sides clear simultaneously.
      const cid = [state.me.id, contact.id].sort().join(':');
      const ttl = state.convSettings && state.convSettings[cid];
      const isFocused = document.visibilityState === 'visible' && document.hasFocus();
      const isCurrentConv = state.selected && state.selected.id === contact.id;
      if (isCurrentConv) {
        appendMessage('in', text, env.timestampMs, env.messageId);
      } else if (Number.isInteger(ttl) && ttl > 0) {
        // Conversation not selected but TTL set — still need to schedule local expiry.
        scheduleExpire('in', env.messageId, contact.id, cid, ttl * 1000);
      }
      // Notify whenever the user can't currently see the message: different conv, hidden tab, or unfocused window.
      if (!isCurrentConv || !isFocused) {
        if (!isCurrentConv) refreshContacts().catch(()=>{});
        try {
          if (typeof Notification !== 'undefined') {
            if (Notification.permission === 'granted') {
              const n = new Notification('Platytalk — @' + contact.handle, {
                body: text.slice(0, 120),
                icon: '/Platytalk.png', badge: '/Platytalk.png',
                tag: 'platytalk:' + contact.id,
                renotify: true,
              });
              n.onclick = () => { try { window.focus(); selectConversation(contact); n.close(); } catch {} };
            } else if (Notification.permission === 'default') {
              // Prompt opportunistically on first incoming message if not yet asked.
              Notification.requestPermission().catch(()=>{});
            }
          }
        } catch {}
      }
      try { state.ws?.send(JSON.stringify({ type: 'ack', messageIds: [env.messageId + ':' + state.me.id] })); } catch {}
    } catch (e) { console.warn('decrypt failed', e); }
  }

  function connectWs() {
    if (!state.token) return;
    // Close any prior socket so we don't end up with two live connections for the
    // same user (which causes the server to deliver each envelope twice).
    try {
      if (state.ws && (state.ws.readyState === WebSocket.OPEN || state.ws.readyState === WebSocket.CONNECTING)) {
        // Suppress its onclose so it doesn't trigger another reconnect.
        state.ws.onclose = null;
        state.ws.close();
      }
    } catch {}
    const ws = new WebSocket(`${WS_URL}?token=${encodeURIComponent(state.token)}`);
    state.ws = ws;
    ws.onmessage = (ev) => {
      let m; try { m = JSON.parse(ev.data); } catch { return; }
      if (m.type === 'envelope') handleEnvelope(m);
      else if (m.type === 'tombstone') {
        for (const k of Object.keys(state.conversations)) state.conversations[k] = state.conversations[k].filter(x => x.messageId !== m.messageId);
        // Also strip from the visible message list if present.
        const ol = $('ptk-msgs');
        if (ol) [...ol.children].forEach(li => { if (li.dataset.messageId === m.messageId) li.remove(); });
      }
      else if (m.type === 'convSettings') {
        state.convSettings[m.conversationId] = m.disappearingSeconds || 0;
        refreshDisappearingLabel();
      }
      else if (m.type === 'contactAdded') {
        refreshContacts().catch(()=>{});
      }
      else if (m.type === 'profileUpdated') {
        // Handle was changed from another client (desktop or another tab) — refresh local cache + UI.
        if (state.me && m.userId === state.me.id) {
          state.me.handle = m.handle;
          if (m.displayName) state.me.displayName = m.displayName;
          const nameEl = document.querySelector('#ptk-me .me-name');
          const handleEl = document.querySelector('#ptk-me .me-handle');
          if (nameEl) nameEl.textContent = state.me.displayName || state.me.handle;
          if (handleEl) handleEl.textContent = '@' + state.me.handle;
          renderInviteQr();
        }
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
    try { if (typeof Notification !== 'undefined' && Notification.permission === 'default') Notification.requestPermission().catch(()=>{}); } catch {}
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
  if ($('ptk-disappear')) $('ptk-disappear').onclick = cycleDisappearing;

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

  // ===================================================================
  // Groups, Devices, Backup (parity with desktop client)
  // ===================================================================

  function showSignedInRails() {
    document.querySelectorAll('.signed-in-only').forEach(el => { el.hidden = !state.token; });
  }
  const _origSetSignedInUi = setSignedInUi;
  // Re-run after sign-in to toggle the new rail sections.
  const _hookInterval = setInterval(() => { showSignedInRails(); }, 800);

  // ----- Groups -------------------------------------------------------

  state.groups = [];

  async function refreshGroups() {
    if (!state.token) return;
    try {
      const r = await api('/v1/groups');
      state.groups = r.groups || [];
      const ul = $('ptk-groups'); if (!ul) return;
      ul.innerHTML = '';
      for (const g of state.groups) {
        const li = document.createElement('li');
        const ttlLabel = formatTtl(g.disappearingSeconds || 0);
        li.innerHTML = `<span class="g-name">${escapeHtml(g.name)}</span> <span class="g-ttl">${escapeHtml(ttlLabel)}</span>`;
        li.title = `${g.members?.length || 0} members`;
        li.onclick = () => openGroupSettings(g);
        ul.appendChild(li);
      }
    } catch (e) { console.warn('groups load failed', e); }
  }

  function formatTtl(s) {
    if (!s) return 'TTL: off';
    if (s < 60) return `TTL: ${s}s`;
    if (s < 3600) return `TTL: ${(s / 60) | 0}m`;
    if (s < 86400) return `TTL: ${(s / 3600) | 0}h`;
    if (s < 604800) return `TTL: ${(s / 86400) | 0}d`;
    return `TTL: ${(s / 604800) | 0}w`;
  }

  const TTL_OPTIONS = [
    { s: 0, label: 'Off' },
    { s: 30, label: '30 seconds' },
    { s: 300, label: '5 minutes' },
    { s: 3600, label: '1 hour' },
    { s: 28800, label: '8 hours' },
    { s: 86400, label: '1 day' },
    { s: 604800, label: '1 week' },
    { s: 2419200, label: '4 weeks' },
  ];

  async function openGroupSettings(g) {
    const opts = TTL_OPTIONS.map(o => `${o.s}=${o.label}`).join(', ');
    const ttl = prompt(`Disappearing-message TTL (seconds) for "${g.name}".\nOptions: ${opts}`, String(g.disappearingSeconds || 0));
    if (ttl === null) return;
    const seconds = parseInt(ttl, 10);
    if (Number.isNaN(seconds)) return;
    try {
      await api(`/v1/groups/${encodeURIComponent(g.id)}/settings`, {
        method: 'PUT', body: JSON.stringify({ disappearingSeconds: seconds }),
      });
      await refreshGroups();
    } catch (e) { alert('TTL update failed: ' + e.message); }
  }

  if ($('ptk-group-create')) $('ptk-group-create').onclick = async () => {
    const name = ($('ptk-group-name').value || '').trim();
    if (!name) return;
    try {
      const memberIds = state.contacts.map(c => c.id);
      const r = await api('/v1/groups', {
        method: 'POST', body: JSON.stringify({ name, memberIds, disappearingSeconds: 0, invitesAdminOnly: false }),
      });
      $('ptk-group-name').value = '';
      await refreshGroups();
    } catch (e) { alert('Create failed: ' + e.message); }
  };

  // ----- Devices ------------------------------------------------------

  async function refreshDevices() {
    if (!state.token) return;
    try {
      const r = await api('/v1/me/devices');
      const ul = $('ptk-devices'); if (!ul) return;
      ul.innerHTML = '';
      for (const d of (r.devices || [])) {
        const li = document.createElement('li');
        li.innerHTML = `<span class="d-name">${escapeHtml(d.deviceName || d.id.slice(0, 8))}</span> <button class="btn ghost tiny d-del" data-id="${escapeHtml(d.id)}">×</button>`;
        ul.appendChild(li);
      }
      ul.querySelectorAll('.d-del').forEach(b => {
        b.onclick = async (ev) => {
          ev.stopPropagation();
          const id = b.getAttribute('data-id');
          if (!confirm('Sign this device out?')) return;
          try { await api(`/v1/me/devices/${encodeURIComponent(id)}`, { method: 'DELETE' }); await refreshDevices(); }
          catch (e) { alert('Failed: ' + e.message); }
        };
      });
    } catch (e) { console.warn('devices load failed', e); }
  }

  if ($('ptk-devices-refresh')) $('ptk-devices-refresh').onclick = refreshDevices;

  // ----- Encrypted backup --------------------------------------------
  // Uses PBKDF2-SHA256 (600k iterations) + AES-256-GCM. The server stores
  // kdfParams alongside, so desktop clients with Argon2id and web clients
  // with PBKDF2 can each restore their own backups.
  const PBKDF2_ITERS = 600000;

  async function deriveBackupKey(passphrase, saltBytes) {
    const baseKey = await subtle.importKey('raw', enc.encode(passphrase), 'PBKDF2', false, ['deriveBits']);
    const bits = await subtle.deriveBits(
      { name: 'PBKDF2', hash: 'SHA-256', salt: saltBytes, iterations: PBKDF2_ITERS },
      baseKey, 256);
    return subtle.importKey('raw', bits, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
  }

  function backupSnapshot() {
    return {
      formatVersion: 1,
      userId: state.me?.id || '',
      handle: state.me?.handle || '',
      displayName: state.me?.displayName || '',
      identityKeyPublicBase64: state.identity?.identityPubSpki || '',
      identityKeyPrivateBase64: state.identity?.identityPrivPkcs8 || '',
      signingKeyPublicBase64: state.identity?.signingPubSpki || '',
      signingKeyPrivateBase64: state.identity?.signingPrivPkcs8 || '',
      contacts: (state.contacts || []).map(c => ({
        contactId: c.id, displayName: c.displayName || c.handle,
        identityKeyPublicBase64: c.identityKeyPublic || '',
      })),
      conversations: [],
      groups: [],
      createdUtc: new Date().toISOString(),
    };
  }

  async function createBackup() {
    const pass = $('ptk-backup-pass').value;
    if (!pass) { setBackupStatus('Enter a passphrase first.'); return; }
    if (!state.me) { setBackupStatus('Sign in first.'); return; }
    setBackupStatus('Encrypting…');
    try {
      const snap = backupSnapshot();
      const plain = enc.encode(JSON.stringify(snap));
      const salt = crypto.getRandomValues(new Uint8Array(16));
      const iv = crypto.getRandomValues(new Uint8Array(12));
      const key = await deriveBackupKey(pass, salt);
      const ct = await subtle.encrypt({ name: 'AES-GCM', iv }, key, plain);
      // versioned blob: [v=1][iv 12][ct||tag]
      const ctArr = new Uint8Array(ct);
      const blob = new Uint8Array(1 + 12 + ctArr.length);
      blob[0] = 1; blob.set(iv, 1); blob.set(ctArr, 13);
      await api('/v1/backup', {
        method: 'PUT', body: JSON.stringify({
          cipherBlob: b64(blob),
          kdfSalt: b64(salt),
          kdfParams: { algorithm: 'pbkdf2-sha256', iterations: PBKDF2_ITERS, length: 32 },
          cipherAlg: 'aes-256-gcm',
        }),
      });
      $('ptk-backup-pass').value = '';
      setBackupStatus('Backup uploaded.');
    } catch (e) { setBackupStatus('Backup failed: ' + e.message); }
  }

  async function restoreBackup() {
    const pass = $('ptk-backup-pass').value;
    if (!pass) { setBackupStatus('Enter the passphrase.'); return; }
    setBackupStatus('Downloading…');
    try {
      const r = await api('/v1/backup');
      if (!r) { setBackupStatus('No backup found for this account.'); return; }
      const salt = unb64(r.kdfSalt);
      const blob = unb64(r.cipherBlob);
      if (blob[0] !== 1) { setBackupStatus('Unsupported backup format.'); return; }
      const iv = blob.slice(1, 13);
      const ct = blob.slice(13);
      const algo = r.kdfParams?.algorithm || 'pbkdf2-sha256';
      if (!algo.toLowerCase().startsWith('pbkdf2')) {
        setBackupStatus('This backup was created by the desktop client (Argon2id). Open the desktop app to restore it.');
        return;
      }
      const key = await deriveBackupKey(pass, salt);
      let plain;
      try { plain = await subtle.decrypt({ name: 'AES-GCM', iv }, key, ct); }
      catch { setBackupStatus('Wrong passphrase or corrupt backup.'); return; }
      const snap = JSON.parse(dec.decode(plain));
      // Apply: identity + contacts. Conversations are server-side per device.
      if (snap.identityKeyPrivateBase64) {
        state.identity = {
          identityPubSpki: snap.identityKeyPublicBase64,
          identityPrivPkcs8: snap.identityKeyPrivateBase64,
          signingPubSpki: snap.signingKeyPublicBase64,
          signingPrivPkcs8: snap.signingKeyPrivateBase64,
        };
        await kvSet('identity', state.identity);
      }
      $('ptk-backup-pass').value = '';
      setBackupStatus(`Restored ${snap.contacts?.length || 0} contacts.`);
      await refreshContacts();
    } catch (e) { setBackupStatus('Restore failed: ' + e.message); }
  }

  function setBackupStatus(s) { const el = $('ptk-backup-status'); if (el) el.textContent = s; }

  if ($('ptk-backup-create')) $('ptk-backup-create').onclick = createBackup;
  if ($('ptk-backup-restore')) $('ptk-backup-restore').onclick = restoreBackup;

  // Refresh groups + devices once we're signed in.
  setInterval(() => {
    if (state.token && state.me) {
      if (!state._didFirstLoad) {
        state._didFirstLoad = true;
        refreshGroups();
        refreshDevices();
      }
    } else {
      state._didFirstLoad = false;
    }
  }, 1500);

  // ===================================================================
  // PIN lock + idle timeout
  // -------------------------------------------------------------------
  // - PIN is stored as PBKDF2-SHA256(pin, salt, 200k iterations) → 32 bytes,
  //   never leaves the browser.
  // - Auto-lock fires after `timeoutMinutes` of no user input (mouse, key,
  //   touch). 0 = never (until tab closes).
  // - Locks immediately on tab hidden + when sign-out so PIN is required
  //   on resume even on the same machine.
  // ===================================================================
  const Lock = (() => {
    const LS_HASH = 'ptk_pin_v1';        // {salt, hash, iterations} base64
    const LS_TIMEOUT = 'ptk_pin_timeout_min';
    const DEFAULT_TIMEOUT = 15;
    const ITER = 200000;

    const b64 = (buf) => btoa(String.fromCharCode(...new Uint8Array(buf)));
    const fromB64 = (s) => Uint8Array.from(atob(s), c => c.charCodeAt(0));

    async function pbkdf2(pin, salt) {
      const enc = new TextEncoder();
      const key = await crypto.subtle.importKey('raw', enc.encode(pin), 'PBKDF2', false, ['deriveBits']);
      const bits = await crypto.subtle.deriveBits({ name: 'PBKDF2', salt, iterations: ITER, hash: 'SHA-256' }, key, 256);
      return new Uint8Array(bits);
    }
    function constTimeEq(a, b) {
      if (a.length !== b.length) return false;
      let r = 0; for (let i = 0; i < a.length; i++) r |= a[i] ^ b[i]; return r === 0;
    }
    function getStored() { try { const j = localStorage.getItem(LS_HASH); return j ? JSON.parse(j) : null; } catch { return null; } }
    function clearStored() { try { localStorage.removeItem(LS_HASH); } catch {} }
    function getTimeoutMin() {
      const v = parseInt(localStorage.getItem(LS_TIMEOUT) || '', 10);
      return Number.isFinite(v) && v >= 0 ? v : DEFAULT_TIMEOUT;
    }
    function setTimeoutMin(n) { try { localStorage.setItem(LS_TIMEOUT, String(n)); } catch {} }

    async function setPin(pin) {
      const salt = crypto.getRandomValues(new Uint8Array(16));
      const hash = await pbkdf2(pin, salt);
      localStorage.setItem(LS_HASH, JSON.stringify({ salt: b64(salt), hash: b64(hash), iter: ITER }));
    }
    async function verify(pin) {
      const s = getStored(); if (!s) return false;
      const hash = await pbkdf2(pin, fromB64(s.salt));
      return constTimeEq(hash, fromB64(s.hash));
    }

    // ---------- UI wiring ----------
    const overlay = document.getElementById('ptk-lock-overlay');
    const host = document.getElementById('app-host');
    const pinInput = document.getElementById('ptk-lock-input');
    const pinConfirm = document.getElementById('ptk-lock-confirm');
    const errEl = document.getElementById('ptk-lock-err');
    const subEl = document.getElementById('ptk-lock-sub');
    const btnSubmit = document.getElementById('ptk-lock-submit');
    const btnSignout = document.getElementById('ptk-lock-signout');
    const btnReset = document.getElementById('ptk-lock-reset');
    const timeoutSel = document.getElementById('ptk-lock-timeout');

    let mode = 'unlock'; // 'unlock' | 'set' | 'confirm'
    let firstPin = '';
    let idleTimer = null;
    let armedForSignedIn = false;

    function show(newMode) {
      mode = newMode;
      errEl.textContent = '';
      pinInput.value = ''; pinConfirm.value = '';
      if (mode === 'unlock') {
        subEl.textContent = 'Enter your PIN to unlock Platytalk.';
        pinConfirm.hidden = true;
        btnSubmit.textContent = 'Unlock';
        btnReset.parentElement.hidden = false;
      } else if (mode === 'set') {
        subEl.textContent = 'Set a PIN (4–32 chars) to lock Platytalk on this device. You\u2019ll be asked for it when the app is idle.';
        pinConfirm.hidden = true;
        btnSubmit.textContent = 'Continue';
        btnReset.parentElement.hidden = true;
      } else if (mode === 'confirm') {
        subEl.textContent = 'Re-enter the PIN to confirm.';
        pinInput.hidden = true;
        pinConfirm.hidden = false;
        btnSubmit.textContent = 'Save PIN';
        btnReset.parentElement.hidden = true;
      }
      pinInput.hidden = (mode === 'confirm');
      overlay.classList.add('show');
      host.classList.add('locked');
      setTimeout(() => (mode === 'confirm' ? pinConfirm : pinInput).focus(), 30);
    }
    function hide() {
      overlay.classList.remove('show');
      host.classList.remove('locked');
      pinInput.value = ''; pinConfirm.value = '';
      pinInput.hidden = false; pinConfirm.hidden = true;
      armIdleTimer();
    }

    async function submit() {
      const v1 = pinInput.value.trim();
      const v2 = pinConfirm.value.trim();
      errEl.textContent = '';
      if (mode === 'unlock') {
        if (!v1) { errEl.textContent = 'Enter your PIN.'; return; }
        const ok = await verify(v1);
        if (!ok) { errEl.textContent = 'Wrong PIN.'; pinInput.value = ''; return; }
        hide();
      } else if (mode === 'set') {
        if (v1.length < 4) { errEl.textContent = 'PIN must be at least 4 characters.'; return; }
        firstPin = v1;
        show('confirm');
      } else if (mode === 'confirm') {
        if (v2 !== firstPin) { errEl.textContent = 'PINs don\u2019t match.'; firstPin=''; show('set'); return; }
        await setPin(v2);
        firstPin = '';
        hide();
      }
    }

    function lock() { if (state.token) show('unlock'); }
    function clearIdleTimer() { if (idleTimer) { clearTimeout(idleTimer); idleTimer = null; } }
    function armIdleTimer() {
      clearIdleTimer();
      if (!state.token || !getStored()) return;
      const min = getTimeoutMin();
      if (min <= 0) return; // 0 = never until tab close
      idleTimer = setTimeout(lock, min * 60 * 1000);
    }

    // Kick on any user activity.
    ['mousemove','mousedown','keydown','touchstart','wheel','scroll'].forEach(ev => {
      document.addEventListener(ev, () => { if (!overlay.classList.contains('show')) armIdleTimer(); }, { passive: true, capture: true });
    });
    // Lock immediately on hide (Signal-style).
    document.addEventListener('visibilitychange', () => { if (document.hidden) lock(); });

    btnSubmit.addEventListener('click', submit);
    [pinInput, pinConfirm].forEach(el => el.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); submit(); } }));
    btnSignout.addEventListener('click', async () => { hide(); clearStored(); try { await signOut(); } catch {} });
    btnReset.addEventListener('click', async (e) => { e.preventDefault(); if (confirm('Reset PIN and sign out? You\u2019ll have to sign in again and set a new PIN.')) { clearStored(); hide(); try { await signOut(); } catch {} } });

    // Init timeout selector.
    timeoutSel.value = String(getTimeoutMin());
    timeoutSel.addEventListener('change', () => { setTimeoutMin(parseInt(timeoutSel.value, 10) || 0); armIdleTimer(); });

    // Public API: arm or trigger from the bootstrap.
    return {
      onSignedIn() {
        armedForSignedIn = true;
        if (getStored()) show('unlock');
        else show('set');
      },
      onSignedOut() {
        armedForSignedIn = false;
        clearIdleTimer();
        overlay.classList.remove('show');
        host.classList.remove('locked');
      },
      lockNow: lock,
      hasPin: () => !!getStored(),
    };
  })();

  // Hook lock into the sign-in/out flow without monkey-patching by polling state.token.
  // Function declarations in this IIFE are referenced by event handlers via closure, so
  // reassigning them is fragile. Polling is robust and runs only once per second.
  let _lockLastSignedIn = false;
  setInterval(() => {
    const signedIn = !!state.token;
    if (signedIn && !_lockLastSignedIn) { try { Lock.onSignedIn(); } catch {} }
    if (!signedIn && _lockLastSignedIn) { try { Lock.onSignedOut(); } catch {} }
    _lockLastSignedIn = signedIn;
  }, 700);
})();
