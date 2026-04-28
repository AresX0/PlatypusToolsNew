// PlatypusTools browser extension (MV3 service worker).
// Talks to the local Remote.Server /api/v1 surface using a Bearer token.

const DEFAULTS = { baseUrl: 'https://localhost:47392', token: '' };

async function settings() {
  const s = await chrome.storage.local.get(DEFAULTS);
  return { ...DEFAULTS, ...s };
}

async function apiPost(path, body) {
  const { baseUrl, token } = await settings();
  if (!baseUrl || !token) throw new Error('PlatypusTools: configure URL and token in popup.');
  const res = await fetch(baseUrl.replace(/\/$/, '') + path, {
    method: 'POST',
    headers: { 'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : null
  });
  if (!res.ok) throw new Error('HTTP ' + res.status);
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({ id: 'platy-send-selection', title: 'PlatypusTools: send selection', contexts: ['selection'] });
  chrome.contextMenus.create({ id: 'platy-send-link', title: 'PlatypusTools: send link', contexts: ['link'] });
});

chrome.contextMenus.onClicked.addListener(async (info) => {
  try {
    if (info.menuItemId === 'platy-send-selection' && info.selectionText) {
      await apiPost('/api/v1/clipboard/plain', { text: info.selectionText });
    } else if (info.menuItemId === 'platy-send-link' && info.linkUrl) {
      await apiPost('/api/v1/clipboard/plain', { text: info.linkUrl });
    }
  } catch (e) { console.error(e); }
});
