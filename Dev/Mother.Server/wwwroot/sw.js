// @module: Mother.Server.Web.ServiceWorker
// @purpose: Minimal offline shell cache for Mother PWA manifest compliance.
// @depends-on: CacheStorage API
// @exports: ServiceWorker lifecycle handlers

const CACHE_NAME = 'mother-shell-v0.1.0';
const SHELL_ASSETS = ['/', '/index.html', '/css/mother.css', '/css/mother-components.css', '/js/app.js'];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(SHELL_ASSETS)).catch(() => undefined)
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/app/')) {
    return;
  }
  event.respondWith(
    fetch(event.request).catch(() => caches.match(event.request))
  );
});
