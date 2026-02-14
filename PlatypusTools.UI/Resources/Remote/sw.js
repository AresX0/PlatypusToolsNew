// Platypus Remote Service Worker
const CACHE_NAME = 'platypus-remote-v3';
const urlsToCache = [
    '/',
    '/index.html',
    '/app.js',
    '/manifest.json'
];

self.addEventListener('install', (event) => {
    // Skip waiting to activate immediately
    self.skipWaiting();
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

    // Network-first strategy: try network, fall back to cache
    event.respondWith(
        fetch(event.request)
            .then((response) => {
                // Clone and cache the response
                const responseClone = response.clone();
                caches.open(CACHE_NAME).then((cache) => {
                    cache.put(event.request, responseClone);
                });
                return response;
            })
            .catch(() => {
                // Network failed, try cache
                return caches.match(event.request);
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
