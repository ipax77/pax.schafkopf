(() => {
    let ready = false;
    let registration;
    let registering;
    let checking = false;
    let lastCheck = -Infinity;
    let reservation;
    let reloading = false;
    const workers = navigator.serviceWorker;

    // These entry points also work when service workers are unavailable.
    window.skPwaUpdates = {
        setReady(value) {
            const changed = ready !== value;
            ready = value;
            if (changed && ready) requestActivation();
        },
        tryBeginTable() {
            if (reservation || reloading || registration?.waiting?.state === 'activating') return false;
            ready = false;
            return true;
        }
    };

    if (!workers || !window.isSecureContext) return;

    let controller = workers.controller;
    workers.addEventListener('controllerchange', () => {
        const previous = controller;
        controller = workers.controller;
        // Claiming a fresh installation is not an update. Reload only once.
        if ((!previous && !reservation) || !controller || previous === controller || reloading) return;
        reloading = true;
        window.location.reload();
    });

    workers.addEventListener('message', event => {
        const message = event.data;
        if (message?.type === 'SK_UPDATE_PREPARE') {
            const port = event.ports[0];
            if (!port) return;
            const canReload = registration?.active && event.source === registration.waiting && ready && !reservation && !reloading
                && Number.isFinite(message.deadline) && Date.now() < message.deadline;
            if (canReload) {
                // Hold the gate until the worker commits or aborts, even if this
                // window is suspended. A local timer must not unlock a commit.
                const worker = event.source;
                const onStateChange = () => {
                    if (worker.state === 'redundant') release(message.token);
                };
                reservation = { token: message.token, worker, onStateChange };
                worker.addEventListener('statechange', onStateChange);
            }
            port.postMessage({ token: message.token, ready: Boolean(canReload) });
            port.close();
        } else if (message?.type === 'SK_UPDATE_ABORT' && event.source === reservation?.worker) {
            release(message.token);
        }
    });

    function release(token) {
        if (reservation?.token !== token) return;
        reservation.worker.removeEventListener('statechange', reservation.onStateChange);
        reservation = undefined;
    }

    function requestActivation() {
        if (!ready || reservation || reloading || !registration?.active || !registration.waiting) return;
        registration.waiting.postMessage({ type: 'SK_UPDATE_REQUEST' });
    }

    function watchInstalling() {
        const worker = registration.installing;
        if (!worker) return;
        const onStateChange = () => {
            if (worker.state === 'installed') requestActivation();
            if (worker.state === 'installed' || worker.state === 'redundant')
                worker.removeEventListener('statechange', onStateChange);
        };
        worker.addEventListener('statechange', onStateChange);
        onStateChange();
    }

    async function ensureRegistration() {
        if (registration) return registration;
        if (!registering) {
            registering = workers.register('service-worker.js', { updateViaCache: 'none' }).then(result => {
                registration = result;
                registration.addEventListener('updatefound', watchInstalling);
                watchInstalling();
                requestActivation();
                return result;
            }).finally(() => { registering = undefined; });
        }
        return registering;
    }

    async function checkForUpdates() {
        requestActivation();
        if (checking || !navigator.onLine || Date.now() - lastCheck < 10_000) return;
        checking = true;
        lastCheck = Date.now();
        try {
            const current = await ensureRegistration();
            await current.update();
            requestActivation();
        } catch (error) {
            // Keep the installed app usable; retry on the next scheduled check.
            console.warn('PWA update check failed; will retry.', error);
        } finally {
            checking = false;
        }
    }

    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') void checkForUpdates();
    });
    window.addEventListener('focus', () => void checkForUpdates());
    window.addEventListener('online', () => void checkForUpdates());
    window.setInterval(() => {
        if (document.visibilityState === 'visible') void checkForUpdates();
    }, 60_000);

    // Registration can find an existing waiting worker even on an offline launch.
    void ensureRegistration().then(checkForUpdates).catch(error => {
        console.warn('PWA registration failed; will retry.', error);
    });
})();
