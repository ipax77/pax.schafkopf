// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
self.addEventListener('message', event => {
    if (event.data?.type === 'SK_UPDATE_REQUEST' && isAppClient(event.source)) {
        event.waitUntil(requestActivation());
    }
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.svg$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);
let activationAttempt;

function isAppClient(client) {
    return client?.type === 'window' && client.url.startsWith(self.registration.scope);
}

function requestActivation() {
    // All windows share this waiting worker, so only one handshake can run.
    if (!activationAttempt) {
        activationAttempt = activateWhenReady()
            .catch(error => console.warn('PWA activation deferred.', error))
            .finally(() => { activationAttempt = undefined; });
    }
    return activationAttempt;
}

async function activateWhenReady() {
    const clients = (await self.clients.matchAll({ type: 'window', includeUncontrolled: true })).filter(isAppClient);
    const token = crypto.randomUUID();
    const deadline = Date.now() + 5_000;
    let committed = false;
    try {
        const responses = await Promise.all(clients.map(client => prepareClient(client, token, deadline)));
        if (!responses.every(Boolean) || Date.now() >= deadline) return;

        // A newly opened window has not agreed to reload. Retry on a later check.
        const current = (await self.clients.matchAll({ type: 'window', includeUncontrolled: true })).filter(isAppClient);
        if (current.some(client => !clients.some(prepared => prepared.id === client.id)) || Date.now() >= deadline) return;

        await self.skipWaiting();
        committed = true;
    } finally {
        if (!committed) {
            for (const client of clients) {
                try { client.postMessage({ type: 'SK_UPDATE_ABORT', token }); }
                catch { /* The window may have closed during the handshake. */ }
            }
        }
    }
}

function prepareClient(client, token, deadline) {
    return new Promise(resolve => {
        const channel = new MessageChannel();
        const timer = setTimeout(() => finish(false), Math.max(0, deadline - Date.now()));
        function finish(ready) {
            clearTimeout(timer);
            channel.port1.close();
            resolve(ready);
        }
        channel.port1.onmessage = event => finish(event.data?.token === token && event.data?.ready === true);
        try {
            client.postMessage({ type: 'SK_UPDATE_PREPARE', token, deadline }, [channel.port2]);
        } catch {
            channel.port2.close();
            finish(false);
        }
    });
}

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    await self.clients.claim();
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}
