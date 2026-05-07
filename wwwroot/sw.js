// Minimal service worker for "Add to Home Screen" / PWA installability.
// We don't cache aggressively — content is dynamic and stale UIs would be worse
// than a refetch. The fetch handler is intentionally a passthrough; it exists so
// browsers (Chrome/Edge) flag the app as installable.

self.addEventListener('install', function (e) {
    self.skipWaiting();
});

self.addEventListener('activate', function (e) {
    e.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', function (e) {
    // Passthrough — let the network handle it.
    // If we ever want offline support, layer caching strategies here.
});
