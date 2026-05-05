// Platytalk service worker — offline app shell.
// IMPORTANT: never cache /v1/, /auth/, /ws — those are live API/auth and must
// always go to the network. Static shell assets use NETWORK-FIRST so deployed
// fixes reach iOS PWA users immediately; cache is only a fallback for offline.
const VERSION = 'ptk-sw-v6-decrypt-toast';
const SHELL = [
  '/',
  '/client.html',
  '/platytalk.css',
  '/platytalk.js',
  '/Platytalk.png',
  '/manifest.webmanifest',
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(VERSION).then((c) => c.addAll(SHELL).catch(() => {/* tolerate misses */})),
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k))),
    ),
  );
  self.clients.claim();
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  // Never intercept API / auth / websocket / cross-origin.
  if (url.origin !== location.origin) return;
  if (url.pathname.startsWith('/v1/') ||
      url.pathname.startsWith('/auth/') ||
      url.pathname.startsWith('/ws')) return;

  // Network-first for shell. Falls back to cache when offline. This guarantees
  // iOS PWA users always pick up the latest platytalk.js / sw bumps as soon as
  // they have network — no stale-cache deadlock.
  event.respondWith(
    fetch(req).then((res) => {
      if (res && res.ok && res.type === 'basic') {
        const clone = res.clone();
        caches.open(VERSION).then((c) => c.put(req, clone)).catch(() => {});
      }
      return res;
    }).catch(() => caches.match(req).then((hit) => hit || caches.match('/client.html'))),
  );
});
