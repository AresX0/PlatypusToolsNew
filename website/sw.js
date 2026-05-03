// Platytalk service worker — offline app shell.
// IMPORTANT: never cache /v1/, /auth/, /ws — those are live API/auth and must
// always go to the network. Only static shell assets are cached.
const VERSION = 'ptk-sw-v2';
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

  event.respondWith(
    caches.match(req).then((hit) => {
      if (hit) {
        // Update in the background.
        fetch(req).then((res) => {
          if (res && res.ok) caches.open(VERSION).then((c) => c.put(req, res.clone()));
        }).catch(() => {});
        return hit;
      }
      return fetch(req).then((res) => {
        if (res && res.ok && res.type === 'basic') {
          const clone = res.clone();
          caches.open(VERSION).then((c) => c.put(req, clone));
        }
        return res;
      }).catch(() => caches.match('/client.html'));
    }),
  );
});
