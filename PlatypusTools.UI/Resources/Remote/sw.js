// Platypus Remote Service Worker
const CACHE_NAME = 'platypus-remote-v1';
const urlsToCache = [
    '/',
    '/index.html',
    '/app.js',
    '/manifest.json'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => cache.addAll(urlsToCache))
            .catch(() => {
                // Cache failed - that's ok for development
            })
    );
});

self.addEventListener('fetch', (event) => {
    // Only cache GET requests for static assets
    if (event.request.method !== 'GET') return;
    
    // Don't cache API or hub requests
    const url = new URL(event.request.url);
    if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hub/')) {
        return;
    }

    event.respondWith(
        caches.match(event.request)
            .then((response) => {
                // Return cached response or fetch from network
                return response || fetch(event.request);
            })
            .catch(() => {
                // If both fail, return a generic offline page
                if (event.request.mode === 'navigate') {
                    return caches.match('/');
                }
            })
    );
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((cacheNames) => {
            return Promise.all(
                cacheNames.filter((name) => name !== CACHE_NAME)
                    .map((name) => caches.delete(name))
            );
        })
    );
});
