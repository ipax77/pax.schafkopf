// Programmatic Bootstrap dialogs need the same focus restoration as trigger buttons.
(() => {
    const returnFocus = new WeakMap();

    document.addEventListener("show.bs.modal", event => {
        if (!event.target.matches(".modal")) return;
        const active = document.activeElement;
        if (active instanceof HTMLElement && active !== document.body && !active.closest(".modal")) {
            returnFocus.set(event.target, active);
        }
    });

    document.addEventListener("hide.bs.modal", event => {
        if (event.defaultPrevented || !event.target.matches(".modal")) return;
        const active = document.activeElement;
        // Bootstrap sets aria-hidden before hidden.bs.modal, so release focus now.
        if (active instanceof HTMLElement && event.target.contains(active)) active.blur();
    });

    document.addEventListener("hidden.bs.modal", event => {
        if (!event.target.matches(".modal")) return;
        const opener = returnFocus.get(event.target);
        returnFocus.delete(event.target);
        // A newer dialog or another control may already have taken focus.
        if (document.querySelector(".modal.show") || document.activeElement !== document.body) return;
        if (opener?.isConnected && !opener.matches(":disabled") &&
            !opener.closest('[inert], [aria-hidden="true"]') && opener.getClientRects().length) {
            opener.focus({ preventScroll: true });
        }
        // Automatic dialogs can outlive the card or button that previously had focus.
        if (document.activeElement === document.body) {
            document.querySelector('main[tabindex="-1"]')?.focus({ preventScroll: true });
        }
    });
})();

// Bootstrap dialogs are sequenced so focus and backdrops survive switching reports.
async function openModalById(id) {
    const element = document.getElementById(id);
    if (!element) return;
    const other = document.querySelector(".modal.show:not(#" + id + ")");
    if (other) {
        await new Promise(resolve => {
            other.addEventListener("hidden.bs.modal", resolve, { once: true });
            bootstrap.Modal.getOrCreateInstance(other).hide();
        });
    }
    bootstrap.Modal.getOrCreateInstance(element).show();
}
function closeModalById(id) {
    const element = document.getElementById(id);
    if (element) bootstrap.Modal.getInstance(element)?.hide();
}
function scrollModalToTop(id) {
    const body = document.querySelector("#" + id + " .modal-body");
    if (body) body.scrollTop = 0;
}
function updateUrlSilently(url) { history.replaceState(history.state, "", url); }
function storeConnectInfo(info) { localStorage.setItem("connectInfo", JSON.stringify(info)); }
function getConnectInfo() {
    try { return JSON.parse(localStorage.getItem("connectInfo")); }
    catch { return null; }
}

window.skTable = (() => {
    const hands = new WeakMap();
    const pending = new Map();
    const animations = new Set();
    const overlays = new Set();
    let controller, resizeTimer, noSleep, awake = false, pauseResolve, pauseTimer;
    let biddingPresentation;
    // CSS owns the breakpoint so presentation and card geometry cannot disagree.
    const compact = () => getComputedStyle(document.documentElement).getPropertyValue("--compact-table").trim() === "1";
    const inspecting = hand => compact() && hand.element.dataset.inspectOnly === "true";
    const reduced = () => matchMedia("(prefers-reduced-motion: reduce)").matches;

    function geometry(element) {
        if (!element || !element.isConnected) return null;
        const rect = element.getBoundingClientRect();
        if (!rect.width || !rect.height) return null;
        let angle = 0;
        for (let node = element; node instanceof Element; node = node.parentElement) {
            const transform = getComputedStyle(node).transform;
            if (transform && transform !== "none") {
                const matrix = new DOMMatrixReadOnly(transform);
                angle += Math.atan2(matrix.b, matrix.a) * 180 / Math.PI;
            }
        }
        const style = getComputedStyle(element);
        return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2,
            width: parseFloat(style.width), height: parseFloat(style.height), angle };
    }
    function transform(origin, width, height) {
        return "translate(" + (origin.x - width / 2) + "px," + (origin.y - height / 2) +
            "px) rotate(" + origin.angle + "deg) scale(" + (origin.width / width) + "," + (origin.height / height) + ")";
    }
    function makeOverlay(className, origin) {
        const node = document.createElement("div");
        node.className = "skcard card-flight " + className;
        node.setAttribute("aria-hidden", "true");
        node.style.width = origin.width + "px";
        node.style.height = origin.height + "px";
        node.style.transform = transform(origin, origin.width, origin.height);
        document.body.appendChild(node);
        overlays.add(node);
        return node;
    }
    function removeOverlay(node) { node?.remove(); overlays.delete(node); }
    function restorePending(id) {
        const item = pending.get(id);
        if (!item) return;
        clearTimeout(item.timer);
        item.button.style.visibility = "";
        removeOverlay(item.overlay);
        pending.delete(id);
    }
    async function animate(node, from, to, duration) {
        if (reduced()) return;
        const animation = node.animate([{ transform: from }, { transform: to }],
            { duration, easing: "cubic-bezier(.22,.75,.25,1)", fill: "forwards" });
        animations.add(animation);
        try { await animation.finished; }
        catch { /* Cancellation reconciles to the authoritative table. */ }
        finally { animations.delete(animation); animation.cancel(); }
    }
    function resetDrag(hand) {
        if (!hand.drag) return;
        const { captureTarget, wrapper, pointerId } = hand.drag;
        hand.drag = null;
        if (captureTarget.hasPointerCapture(pointerId)) captureTarget.releasePointerCapture(pointerId);
        wrapper?.classList.remove("is-dragging");
        wrapper?.style.removeProperty("--drag-x");
        wrapper?.style.removeProperty("--drag-y");
        hand.element.classList.remove("is-scrubbing", "is-play-armed");
        hand.element.style.removeProperty("--selection-raise");
        hand.selected?.closest(".hand-card").style.removeProperty("--touch-lift");
        cancelAnimationFrame(hand.frame);
        updateSelection(hand);
    }
    function clearSelection(hand) {
        if (hand.selected) {
            const wrapper = hand.selected.closest(".hand-card");
            wrapper.classList.remove("is-touch-selected");
            wrapper.style.removeProperty("--touch-lift");
            hand.selected.removeAttribute("aria-pressed");
        }
        hand.selected = null;
        hand.element.classList.remove("is-play-armed");
        hand.element.style.removeProperty("--selection-raise");
        hand.element.querySelector(".touch-selection").hidden = true;
    }
    function updateSelection(hand) {
        const button = hand.selected;
        if (!button) return;
        if (!hand.element.contains(button)) { clearSelection(hand); return; }
        const playable = button.dataset.playable === "true";
        const inspect = inspecting(hand);
        const panel = hand.element.querySelector(".touch-selection");
        panel.hidden = false;
        panel.querySelector(".touch-selection-name").textContent = button.getAttribute("aria-label");
        panel.querySelector(".touch-selection-hint").textContent = inspect ? "" : !playable ? "Gerade nicht spielbar" :
            hand.drag?.armed ? "Loslassen zum Spielen" : "Nach oben ziehen oder bestätigen";
        panel.querySelector(".touch-play").disabled = !playable || hand.busy;
        panel.querySelector(".touch-play").hidden = inspect;
    }
    function selectTouchCard(hand, button) {
        if (hand.selected !== button) {
            clearSelection(hand);
            hand.selected = button;
            button.closest(".hand-card").classList.add("is-touch-selected");
            button.setAttribute("aria-pressed", "true");
        }
        updateSelection(hand);
    }
    function touchSlots(element) {
        const bounds = element.getBoundingClientRect();
        // Use the original fan lanes, not hit-testing: the lifted card overlaps
        // its neighbours and must never steal their selection area.
        return Array.from(element.querySelectorAll("[data-card-id]")).map(button => {
            const wrapper = button.closest(".hand-card");
            return { button, left: bounds.left + wrapper.offsetLeft - wrapper.offsetWidth / 2 };
        });
    }
    function cardAt(slots, x) {
        return (slots.findLast(slot => x >= slot.left) ?? slots[0])?.button;
    }
    function drawTouch(hand) {
        const drag = hand.drag;
        const lift = Math.max(0, -drag.dy);
        if (lift < 24) {
            if (Math.abs(drag.dx) > 6) selectTouchCard(hand, cardAt(drag.slots, drag.x + drag.dx));
        }
        const button = hand.selected;
        if (inspecting(hand)) {
            drag.armed = false;
            updateSelection(hand);
            return;
        }
        const baseLift = matchMedia("(max-height: 500px)").matches ? 32 : 52;
        button.closest(".hand-card").style.setProperty("--touch-lift", -Math.max(baseLift, lift) + "px");
        const bounds = hand.element.getBoundingClientRect();
        const panel = hand.element.querySelector(".touch-selection");
        const panelGap = baseLift === 32 ? 34 : 61;
        const raise = Math.min(Math.max(0, lift - baseLift), Math.max(0, bounds.top - panel.offsetHeight - panelGap - 8));
        hand.element.style.setProperty("--selection-raise", raise + "px");
        const inside = drag.x + drag.dx >= bounds.left - 16 && drag.x + drag.dx <= bounds.right + 16;
        drag.armed = inside && lift >= Math.max(48, Math.min(88, button.offsetHeight * .25)) && button.dataset.playable === "true";
        hand.element.classList.toggle("is-play-armed", drag.armed);
        updateSelection(hand);
    }
    function drawDrag(hand) {
        if (!hand.drag) return;
        if (hand.drag.touch) { drawTouch(hand); return; }
        hand.drag.wrapper.style.setProperty("--drag-x", hand.drag.dx + "px");
        hand.drag.wrapper.style.setProperty("--drag-y", hand.drag.dy + "px");
    }
    async function submit(hand, button) {
        const id = Number(button.dataset.cardId);
        if (hand.busy || pending.has(id) || button.dataset.playable !== "true") return;
        hand.busy = true;
        drawDrag(hand);
        const origin = geometry(button);
        if (!origin) { hand.busy = false; return; }
        const overlay = makeOverlay(button.className, origin);
        button.style.visibility = "hidden";
        pending.set(id, { origin, overlay, button, timer: setTimeout(() => restorePending(id), 10000) });
        resetDrag(hand);
        clearSelection(hand);
        try {
            const accepted = await hand.reference.invokeMethodAsync("PlayFromHand", id);
            if (!accepted) restorePending(id);
        } catch { restorePending(id); }
        finally { hand.busy = false; }
    }
    function bindHand(element, reference) {
        if (!element) return;
        let hand = hands.get(element);
        if (!hand) {
            hand = { element, reference, controller: new AbortController(), drag: null, selected: null, busy: false };
            hands.set(element, hand);
            const options = { signal: hand.controller.signal };
            hand.layout = () => {
                const count = element.querySelectorAll(".hand-card").length;
                const bounds = element.getBoundingClientRect();
                const layoutKey = [count, bounds.width, bounds.height, element.dataset.inspectOnly].join(":");
                if (hand.layoutKey !== layoutKey) {
                    resetDrag(hand);
                    clearSelection(hand);
                    hand.layoutKey = layoutKey;
                }
                // Only the upper part needs to fit. Cropping the bottoms makes
                // ranks and suits substantially easier to read on phones.
                const isCompact = compact();
                const short = matchMedia("(max-height: 500px)").matches;
                const minimum = short ? (bounds.width >= 650 ? 110 : 88) : 80;
                const maxStepRatio = isCompact ? 1.08 : .68;
                const fullHandWidth = count === 8 ? (bounds.width - 16) / (1 + 7 * maxStepRatio) : 0;
                const width = isCompact ? Math.max(0, Math.min(180,
                    Math.max(minimum, (bounds.height - 20) * .95, fullHandWidth),
                    (bounds.width - 16) / (1 + Math.max(0, count - 1) * .35))) :
                    Math.min(180, Math.max(54, (bounds.height - 24) / .55 * 156.3 / 283.86),
                    Math.max(54, bounds.width / 2.5), Math.max(54, bounds.width - Math.max(0, count - 1) * 24 - 18));
                const step = count > 1 ? Math.min(width * maxStepRatio,
                    Math.max(0, (bounds.width - width - (isCompact ? 16 : 18)) / (count - 1))) : 0;
                element.style.setProperty("--hand-card-width", width + "px");
                element.style.setProperty("--hand-step", step + "px");
            };
            hand.observer = new ResizeObserver(hand.layout);
            hand.observer.observe(element);
            element.addEventListener("pointerdown", event => {
                if (event.target.closest(".touch-selection") || event.button !== 0 || !event.isPrimary || hand.busy || hand.drag) return;
                if (event.pointerType === "touch" || event.pointerType === "pen") {
                    const slots = touchSlots(element);
                    if (!slots.length) return;
                    event.preventDefault();
                    const bounds = element.getBoundingClientRect();
                    const raised = event.target.closest(".is-touch-selected [data-card-id]");
                    const button = event.clientY < bounds.top && raised ? raised : cardAt(slots, event.clientX);
                    selectTouchCard(hand, button);
                    hand.drag = { touch: true, slots, captureTarget: element, pointerId: event.pointerId,
                        x: event.clientX, y: event.clientY, dx: 0, dy: 0, armed: false };
                    element.classList.add("is-scrubbing");
                    element.setPointerCapture(event.pointerId);
                    return;
                }
                const button = event.target.closest("[data-card-id]");
                if (button && inspecting(hand)) {
                    event.preventDefault();
                    selectTouchCard(hand, button);
                    return;
                }
                if (!button || button.dataset.playable !== "true") return;
                clearSelection(hand);
                event.preventDefault();
                button.focus({ preventScroll: true });
                const wrapper = button.closest(".hand-card");
                hand.drag = { button, wrapper, captureTarget: button, pointerId: event.pointerId, x: event.clientX, y: event.clientY, dx: 0, dy: 0 };
                wrapper.classList.add("is-dragging");
                button.setPointerCapture(event.pointerId);
            }, options);
            element.addEventListener("pointermove", event => {
                if (!hand.drag || hand.drag.pointerId !== event.pointerId) return;
                hand.drag.dx = event.clientX - hand.drag.x;
                hand.drag.dy = event.clientY - hand.drag.y;
                cancelAnimationFrame(hand.frame);
                hand.frame = requestAnimationFrame(() => drawDrag(hand));
            }, options);
            element.addEventListener("pointerup", event => {
                if (!hand.drag || hand.drag.pointerId !== event.pointerId) return;
                hand.drag.dx = event.clientX - hand.drag.x;
                hand.drag.dy = event.clientY - hand.drag.y;
                if (hand.drag.touch) {
                    drawTouch(hand);
                    if (hand.drag.armed) void submit(hand, hand.selected);
                    else resetDrag(hand);
                    return;
                }
                const { button } = hand.drag;
                const tap = Math.hypot(hand.drag.dx, hand.drag.dy) < 8;
                const upward = -hand.drag.dy > button.offsetHeight * .3;
                if (tap || upward) void submit(hand, button);
                else resetDrag(hand);
            }, options);
            const cancelGesture = event => {
                if (hand.drag?.pointerId !== event.pointerId) return;
                resetDrag(hand);
                clearSelection(hand);
            };
            element.addEventListener("pointercancel", cancelGesture, options);
            element.addEventListener("lostpointercapture", cancelGesture, options);
            element.querySelector(".touch-play").addEventListener("click", () => {
                if (hand.selected) void submit(hand, hand.selected);
            }, options);
            element.querySelector(".touch-dismiss").addEventListener("click", () => clearSelection(hand), options);
            document.addEventListener("pointerdown", event => {
                if (!element.contains(event.target) && !hand.drag) clearSelection(hand);
            }, options);
            element.addEventListener("keydown", event => {
                if (event.key === "Escape") { resetDrag(hand); clearSelection(hand); }
            }, options);
            element.addEventListener("click", event => {
                const button = event.target.closest("[data-card-id]");
                if (button) {
                    event.preventDefault();
                    if (inspecting(hand)) { selectTouchCard(hand, button); return; }
                    if (event.detail === 0 && !event.sourceCapabilities?.firesTouchEvents) void submit(hand, button);
                }
            }, options);
        }
        hand.reference = reference;
        hand.layout();
        updateSelection(hand);
    }
    function unbindHand(element) {
        const hand = hands.get(element);
        if (!hand) return;
        resetDrag(hand);
        clearSelection(hand);
        hand.controller.abort();
        hand.observer.disconnect();
        hands.delete(element);
    }
    function captureSources() {
        const sources = {};
        document.querySelectorAll(".hand-container [data-card-id]").forEach(button => {
            const id = Number(button.dataset.cardId);
            sources["card-" + id] = pending.get(id)?.origin ?? geometry(button);
        });
        document.querySelectorAll("[data-hand-seat]").forEach(hand => {
            // Opponent positions are private. Every card leaves the same hand-center
            // anchor, independent of its identity, sort order, or number of trumps.
            const back = hand.querySelector(".card-back");
            // Compact layouts hide the backs. Never animate from a display:none
            // hand's zero rectangle; use that seat's visible name panel instead.
            const anchor = geometry(hand) ?? geometry(hand.closest(".opponent-seat")?.querySelector(".player-panel"));
            if (!anchor) return;
            const style = back ? getComputedStyle(back) : null;
            sources["seat-" + hand.dataset.handSeat] = {
                x: anchor.x, y: anchor.y,
                width: parseFloat(style?.width) || 28, height: parseFloat(style?.height) || 50, angle: 0
            };
        });
        return Object.fromEntries(Object.entries(sources).filter(([, value]) => value));
    }
    async function flyCard(cssClass, cardId, seat, origin) {
        const target = document.querySelector('#live-trick-layout [data-trick-seat="' + seat + '"] .trick-card-target');
        const destination = geometry(target);
        const held = pending.get(cardId);
        origin = held?.origin ?? origin;
        if (!destination || !origin) { restorePending(cardId); return; }
        let overlay;
        if (held) {
            clearTimeout(held.timer);
            pending.delete(cardId);
            held.button.style.visibility = "";
            overlay = held.overlay;
        } else overlay = makeOverlay(cssClass, origin);
        try { await animate(overlay, transform(origin, origin.width, origin.height),
            transform(destination, origin.width, origin.height), 300); }
        finally { removeOverlay(overlay); }
    }
    async function collectTrick(view) {
        const destination = geometry(document.getElementById("trick-stack-" + view));
        if (!destination) return;
        await Promise.all(Array.from(document.querySelectorAll("#live-trick-layout .skcard")).map(async card => {
            const origin = geometry(card);
            if (!origin) return;
            const overlay = makeOverlay(card.className, origin);
            overlay.style.zIndex = 1040 + Number(getComputedStyle(card.closest(".trick-slot")).zIndex);
            card.style.visibility = "hidden";
            const target = { ...destination, width: 20, height: 36, angle: -8 };
            try { await animate(overlay, transform(origin, origin.width, origin.height),
                transform(target, origin.width, origin.height), 350); }
            finally { card.style.visibility = ""; removeOverlay(overlay); }
        }));
    }
    function pauseTrick() {
        if (reduced()) return Promise.resolve();
        return new Promise(resolve => {
            pauseResolve = resolve;
            pauseTimer = setTimeout(() => { pauseResolve = null; resolve(); }, 900);
        });
    }
    function cancelAnimations() {
        document.querySelectorAll(".hand-container").forEach(element => {
            const hand = hands.get(element);
            if (hand) { resetDrag(hand); clearSelection(hand); }
        });
        for (const animation of animations) animation.cancel();
        animations.clear();
        for (const node of overlays) node.remove();
        overlays.clear();
        for (const id of [...pending.keys()]) restorePending(id);
        clearTimeout(pauseTimer);
        pauseResolve?.();
        pauseResolve = null;
    }
    function closeModals() {
        document.querySelectorAll(".modal").forEach(element => bootstrap.Modal.getInstance(element)?.hide());
    }
    function init(reference) {
        controller?.abort();
        controller = new AbortController();
        biddingPresentation = null;
        window.addEventListener("resize", () => {
            cancelAnimations();
            clearTimeout(resizeTimer);
            resizeTimer = setTimeout(() => reference.invokeMethodAsync("ViewportChanged", compact()).catch(() => {}), 150);
        }, { signal: controller.signal });
        const toggle = document.getElementById("screentoggle");
        toggle?.addEventListener("click", async () => {
            noSleep ??= new NoSleep();
            try {
                if (awake) noSleep.disable();
                else await noSleep.enable();
                awake = !awake;
                toggle.setAttribute("aria-pressed", String(awake));
                toggle.classList.toggle("is-on", awake);
            } catch { /* Wake lock may be unavailable in a browser. */ }
        }, { signal: controller.signal });
        return compact();
    }
    async function presentBidding(isCompact, active, decisionId) {
        const presentation = [isCompact, active, decisionId].join(":");
        if (biddingPresentation === presentation) return;
        biddingPresentation = presentation;
        if (!active || isCompact) closeModalById("biddingModal");
        if (active && isCompact) {
            closeModals();
            document.getElementById("biddingPanelTitle")?.focus({ preventScroll: true });
        } else if (active) {
            await openModalById("biddingModal");
        } else if (document.activeElement === document.body) {
            document.querySelector('main[tabindex="-1"]')?.focus({ preventScroll: true });
        }
    }
    function dispose() {
        controller?.abort();
        clearTimeout(resizeTimer);
        cancelAnimations();
        noSleep?.disable();
        awake = false;
        closeModals();
    }
    return { init, dispose, presentBidding, bindHand, unbindHand, captureSources, flyCard, collectTrick, pauseTrick, cancelAnimations, closeModals };
})();
