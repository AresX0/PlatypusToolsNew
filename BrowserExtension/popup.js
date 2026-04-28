const $ = id => document.getElementById(id);

async function load() {
  const s = await chrome.storage.local.get({ baseUrl: 'https://localhost:47392', token: '' });
  $('url').value = s.baseUrl;
  $('token').value = s.token;
}

$('save').addEventListener('click', async () => {
  await chrome.storage.local.set({ baseUrl: $('url').value, token: $('token').value });
  $('status').textContent = 'Saved.';
});

$('ping').addEventListener('click', async () => {
  $('status').textContent = 'Pinging...';
  try {
    const res = await fetch($('url').value.replace(/\/$/, '') + '/api/v1/health', {
      headers: { 'Authorization': 'Bearer ' + $('token').value }
    });
    $('status').textContent = res.ok ? 'OK ' + res.status : 'HTTP ' + res.status;
  } catch (e) { $('status').textContent = 'Error: ' + e.message; }
});

load();
