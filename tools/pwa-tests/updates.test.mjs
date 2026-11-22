import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { test } from 'node:test';
import vm from 'node:vm';

const pageScript = readFileSync(new URL('../../src/sk.pwa/wwwroot/js/pwa-updates.js', import.meta.url), 'utf8');
const workerScript = readFileSync(new URL('../../src/sk.pwa/wwwroot/service-worker.published.js', import.meta.url), 'utf8');
const settle = () => new Promise(resolve => setImmediate(resolve));

class Events {
    listeners = new Map();
    addEventListener(name, callback) {
        if (!this.listeners.has(name)) this.listeners.set(name, new Set());
        this.listeners.get(name).add(callback);
    }
    removeEventListener(name, callback) { this.listeners.get(name)?.delete(callback); }
    emit(name, event = {}) { for (const callback of [...(this.listeners.get(name) ?? [])]) callback(event); }
}

class Clock {
    now = 100_000;
    nextId = 0;
    tasks = new Map();
    setTimeout = (callback, delay) => this.add(callback, delay, false);
    clearTimeout = id => this.tasks.delete(id);
    setInterval = (callback, delay) => this.add(callback, delay, true);
    add(callback, delay, repeat) {
        const id = ++this.nextId;
        this.tasks.set(id, { callback, at: this.now + delay, delay, repeat });
        return id;
    }
    async advance(milliseconds) {
        const end = this.now + milliseconds;
        while (true) {
            const next = [...this.tasks].filter(([, task]) => task.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
            if (!next) break;
            const [id, task] = next;
            this.now = task.at;
            if (task.repeat) task.at += task.delay;
            else this.tasks.delete(id);
            task.callback();
            await settle();
        }
        this.now = end;
        await settle();
    }
    get Date() {
        const clock = this;
        return class extends Date { static now() { return clock.now; } };
    }
}

function pageHarness({ controlled = true, supported = true, online = true, waiting = true } = {}) {
    const clock = new Clock();
    const worker = Object.assign(new Events(), { state: 'installed', messages: [], postMessage(message) { this.messages.push(message); } });
    const registration = Object.assign(new Events(), {
        active: controlled ? {} : null, waiting: waiting ? worker : null, installing: null,
        updates: 0, async update() { this.updates++; }
    });
    const workers = Object.assign(new Events(), {
        controller: controlled ? {} : null, registrations: [],
        async register(url, options) { this.registrations.push({ url, options }); return registration; }
    });
    const window = Object.assign(new Events(), {
        isSecureContext: true, reloads: 0,
        location: { reload() { window.reloads++; } }, setInterval: clock.setInterval
    });
    const document = Object.assign(new Events(), { visibilityState: 'visible' });
    const navigator = { onLine: online, ...(supported ? { serviceWorker: workers } : {}) };
    const warnings = [];
    const context = vm.createContext({ window, document, navigator, Date: clock.Date, console: { warn: (...args) => warnings.push(args) } });
    const start = () => vm.runInContext(pageScript, context);
    function prepare({ token = 'attempt', deadline = clock.now + 5_000, source = worker } = {}) {
        let response;
        workers.emit('message', { source, data: { type: 'SK_UPDATE_PREPARE', token, deadline },
            ports: [{ postMessage(value) { response = value; }, close() {} }] });
        return response;
    }
    const abort = (token = 'attempt') => workers.emit('message', { source: worker, data: { type: 'SK_UPDATE_ABORT', token } });
    return { clock, window, document, navigator, workers, worker, registration, warnings, start, prepare, abort };
}

class Channel {
    constructor() {
        const makePort = () => ({ closed: false, onmessage: null, close() { this.closed = true; } });
        this.port1 = makePort();
        this.port2 = makePort();
        for (const [sender, receiver] of [[this.port1, this.port2], [this.port2, this.port1]]) {
            sender.postMessage = data => queueMicrotask(() => {
                if (!receiver.closed) receiver.onmessage?.({ data });
            });
        }
    }
}

function workerHarness(states = [true]) {
    const clock = new Clock();
    const clients = states.map((ready, index) => ({
        id: String(index), type: 'window', url: 'https://example.test/', messages: [],
        postMessage(message, ports) {
            this.messages.push(message);
            if (ports && ready !== null) ports[0].postMessage({ token: message.token, ready });
        }
    }));
    const self = Object.assign(new Events(), {
        origin: 'https://example.test', assetsManifest: { version: 'new', assets: [] },
        registration: { scope: 'https://example.test/' }, importScripts() {},
        skips: 0, async skipWaiting() { this.skips++; },
        clients: { claims: 0, async claim() { this.claims++; }, async matchAll() { return clients; } }
    });
    const cache = { async addAll() {}, async match() { return undefined; } };
    const caches = { deleted: [], async open() { return cache; }, async keys() { return ['offline-cache-old', 'offline-cache-new', 'unrelated']; },
        async delete(key) { this.deleted.push(key); } };
    const context = vm.createContext({ self, caches, URL, Request, crypto: { randomUUID }, MessageChannel: Channel,
        Date: clock.Date, setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout,
        console: { info() {}, warn() {} }, fetch: async () => 'network' });
    vm.runInContext(workerScript, context);
    function dispatch(name, event = {}) {
        let work;
        self.emit(name, { ...event, waitUntil(promise) { work = promise; }, respondWith(promise) { work = promise; } });
        return work;
    }
    const request = () => dispatch('message', { source: clients[0], data: { type: 'SK_UPDATE_REQUEST' } });
    return { clock, clients, self, caches, cache, dispatch, request };
}

test('registration checks without caches; an unchanged release never reloads', async () => {
    const h = pageHarness({ waiting: false });
    h.start();
    await settle();
    assert.equal(h.workers.registrations[0].options.updateViaCache, 'none');
    assert.equal(h.registration.updates, 1);
    h.window.skPwaUpdates.setReady(true);
    await h.clock.advance(60_000);
    assert.equal(h.registration.updates, 2);
    assert.equal(h.window.reloads, 0);
});

test('an existing waiting update waits for initialization and reserves table entry', async () => {
    const h = pageHarness();
    h.start();
    await settle();
    assert.equal(h.worker.messages.length, 0);
    assert.equal(h.prepare().ready, false);
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.worker.messages.at(-1).type, 'SK_UPDATE_REQUEST');
    assert.equal(h.prepare().ready, true);
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), false);
    h.abort('another-attempt');
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), false);
    h.abort();
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), true);
});

test('creating, joining and remaining at a table defer updates; returning retries', async () => {
    const h = pageHarness();
    h.start();
    await settle();
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), true);
    assert.equal(h.prepare().ready, false);
    h.window.skPwaUpdates.setReady(false);
    assert.equal(h.prepare().ready, false);
    const requests = h.worker.messages.length;
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.worker.messages.length, requests + 1);
    assert.equal(h.prepare().ready, true);
});

test('late or unrelated prepare messages cannot reserve a page', async () => {
    const h = pageHarness();
    h.start();
    await settle();
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.prepare({ deadline: h.clock.now }).ready, false);
    assert.equal(h.prepare({ source: {} }).ready, false);
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), true);
});

test('fresh installs do not reload; later controller changes reload exactly once', async () => {
    const h = pageHarness({ controlled: false, waiting: false });
    h.start();
    await settle();
    h.workers.controller = {};
    h.workers.emit('controllerchange');
    assert.equal(h.window.reloads, 0);
    h.workers.controller = {};
    h.workers.emit('controllerchange');
    h.workers.controller = {};
    h.workers.emit('controllerchange');
    assert.equal(h.window.reloads, 1);
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), false);
});

test('an uncontrolled window reloads when it has agreed to an existing worker update', async () => {
    const h = pageHarness({ controlled: false });
    h.registration.active = {};
    h.start();
    await settle();
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.prepare().ready, true);
    h.workers.controller = h.worker;
    h.workers.emit('controllerchange');
    assert.equal(h.window.reloads, 1);
});

test('first installation never requests forced activation', async () => {
    const h = pageHarness({ controlled: false });
    h.start();
    await settle();
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.worker.messages.length, 0);
    assert.equal(h.prepare().ready, false);
});

test('a superseded waiting worker releases its table-entry reservation', async () => {
    const h = pageHarness();
    h.start();
    await settle();
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.prepare().ready, true);
    h.worker.state = 'redundant';
    h.worker.emit('statechange');
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), true);
});

test('newly installed workers request activation only after becoming waiting', async () => {
    const h = pageHarness({ waiting: false });
    h.start();
    await settle();
    h.window.skPwaUpdates.setReady(true);
    h.worker.state = 'installing';
    h.registration.installing = h.worker;
    h.registration.emit('updatefound');
    assert.equal(h.worker.messages.length, 0);
    h.registration.waiting = h.worker;
    h.worker.state = 'installed';
    h.worker.emit('statechange');
    assert.equal(h.worker.messages.length, 1);
    assert.equal(h.window.reloads, 0);
});

test('periodic checks pause when hidden/offline and resume on foreground/reconnection', async () => {
    const h = pageHarness({ waiting: false });
    h.start();
    await settle();
    h.document.visibilityState = 'hidden';
    await h.clock.advance(60_000);
    assert.equal(h.registration.updates, 1);
    h.document.visibilityState = 'visible';
    h.document.emit('visibilitychange');
    h.window.emit('focus');
    await settle();
    assert.equal(h.registration.updates, 2);
    h.navigator.onLine = false;
    await h.clock.advance(60_000);
    assert.equal(h.registration.updates, 2);
    h.navigator.onLine = true;
    h.window.emit('online');
    await settle();
    assert.equal(h.registration.updates, 3);
});

test('overlapping checks are suppressed and failed checks are retried', async () => {
    const h = pageHarness();
    let finish;
    h.registration.update = () => { h.registration.updates++; return new Promise(resolve => { finish = resolve; }); };
    h.start();
    await settle();
    await h.clock.advance(60_000);
    h.window.emit('focus');
    assert.equal(h.registration.updates, 1);
    finish();
    await settle();
    h.registration.update = async () => { h.registration.updates++; throw Error('offline'); };
    await h.clock.advance(60_000);
    assert.equal(h.warnings.length, 1);
    await h.clock.advance(60_000);
    assert.equal(h.registration.updates, 3);
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), true);
});

test('a failed registration retries without breaking table entry', async () => {
    const h = pageHarness();
    let attempts = 0;
    h.workers.register = async () => { if (++attempts === 1) throw Error('offline'); return h.registration; };
    h.start();
    await settle();
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), true);
    await h.clock.advance(60_000);
    assert.equal(attempts, 2);
    assert.equal(h.registration.updates, 1);
});

test('offline launches can activate an already cached update', async () => {
    const h = pageHarness({ online: false });
    h.start();
    await settle();
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.registration.updates, 0);
    assert.equal(h.worker.messages.length, 1);
});

test('unsupported browsers retain normal table entry', () => {
    const h = pageHarness({ supported: false });
    h.start();
    h.window.skPwaUpdates.setReady(true);
    assert.equal(h.window.skPwaUpdates.tryBeginTable(), true);
});

test('all lobby windows approve one shared activation attempt', async () => {
    const h = workerHarness([true, true]);
    await Promise.all([h.request(), h.request()]);
    assert.equal(h.self.skips, 1);
    assert.ok(h.clients.every(client => client.messages.length === 1));
    assert.equal(h.caches.deleted.length, 0);
});

test('one seated window defers activation and releases every reservation', async () => {
    const h = workerHarness([true, false]);
    await h.request();
    assert.equal(h.self.skips, 0);
    assert.ok(h.clients.every(client => client.messages.at(-1).type === 'SK_UPDATE_ABORT'));
    assert.equal(h.caches.deleted.length, 0);
});

test('legacy or suspended windows time out after five seconds', async () => {
    const h = workerHarness([true, null]);
    const pending = h.request();
    await settle();
    await h.clock.advance(4_999);
    assert.equal(h.clients[0].messages.length, 1);
    await h.clock.advance(1);
    await pending;
    assert.equal(h.self.skips, 0);
    assert.ok(h.clients.every(client => client.messages.at(-1).type === 'SK_UPDATE_ABORT'));
});

test('a new window appearing during the handshake defers activation', async () => {
    const h = workerHarness();
    let reads = 0;
    h.self.clients.matchAll = async () => ++reads === 1 ? h.clients : [...h.clients, { id: 'new', type: 'window', url: 'https://example.test/' }];
    await h.request();
    assert.equal(h.self.skips, 0);
    assert.equal(h.clients[0].messages.at(-1).type, 'SK_UPDATE_ABORT');
});

test('failed activation releases reservations and allows a later attempt', async () => {
    const h = workerHarness();
    h.self.skipWaiting = async () => { throw Error('activation failed'); };
    await h.request();
    assert.equal(h.clients[0].messages.at(-1).type, 'SK_UPDATE_ABORT');
    h.self.skipWaiting = async () => { h.self.skips++; };
    await h.request();
    assert.equal(h.self.skips, 1);
});

test('installation preserves the active cache and never forces activation', async () => {
    const h = workerHarness();
    h.cache.addAll = async () => { throw Error('integrity mismatch'); };
    await assert.rejects(h.dispatch('install'), /integrity mismatch/);
    assert.equal(h.self.skips, 0);
    assert.equal(h.self.clients.claims, 0);
    assert.equal(h.caches.deleted.length, 0);
});

test('activation cleans only obsolete app caches, then claims windows', async () => {
    const h = workerHarness();
    await h.dispatch('activate');
    assert.deepEqual(h.caches.deleted, ['offline-cache-old']);
    assert.equal(h.self.clients.claims, 1);
});

test('offline navigations use the complete cached app', async () => {
    const h = workerHarness();
    h.cache.match = async request => { assert.equal(request, 'index.html'); return 'cached app'; };
    const result = await h.dispatch('fetch', { request: { method: 'GET', mode: 'navigate', url: 'https://example.test/?TableId=ABC' } });
    assert.equal(result, 'cached app');
});
