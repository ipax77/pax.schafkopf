import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const script = readFileSync(new URL('../../src/sk.weblib/wwwroot/js/sk.js', import.meta.url), 'utf8');

class Element {
    isConnected = true;
    parentElement = null;
    constructor(rect, style = {}) {
        this.rect = rect;
        this.style = { width: `${rect.width}px`, height: `${rect.height}px`, transform: 'none', ...style };
    }
    getBoundingClientRect() { return this.rect; }
}
const box = (x, y, width, height) => ({ x, y, width, height });

function opponent(seat, panelRect, handRect, withBack = true) {
    const panel = new Element(panelRect);
    const hand = new Element(handRect);
    const back = withBack ? new Element(box(0, 0, 14, 26)) : null;
    hand.dataset = { handSeat: String(seat) };
    hand.querySelector = () => back;
    hand.closest = () => ({ querySelector: () => panel });
    return hand;
}

function capture(hands) {
    const context = vm.createContext({
        window: {}, Element,
        document: { addEventListener() {}, querySelectorAll: selector => selector === '[data-hand-seat]' ? hands : [] },
        getComputedStyle: element => element.style
    });
    vm.runInContext(script, context);
    return JSON.parse(JSON.stringify(context.window.skTable.captureSources()));
}

test('hidden mobile hands originate at their own visible seats, including rotated server positions', () => {
    const hidden = box(0, 0, 0, 0);
    const sources = capture([
        opponent(3, box(20, 55, 160, 32), hidden),
        opponent(0, box(204, 55, 160, 32), hidden),
        opponent(1, box(388, 55, 160, 32), hidden)
    ]);
    for (const [seat, x] of [[3, 100], [0, 284], [1, 468]]) {
        assert.deepEqual(sources[`seat-${seat}`], { x, y: 71, width: 14, height: 26, angle: 0 });
    }
});

test('visible backs retain the stable center of the whole hand as their origin', () => {
    const sources = capture([opponent(2, box(200, 40, 150, 40), box(200, 88, 150, 60))]);
    assert.deepEqual(sources['seat-2'], { x: 275, y: 118, width: 14, height: 26, angle: 0 });
});

test('a queued final card can still originate at its seat after all backs disappear', () => {
    const sources = capture([opponent(1, box(388, 55, 160, 32), box(0, 0, 0, 0), false)]);
    assert.deepEqual(sources['seat-1'], { x: 468, y: 71, width: 28, height: 50, angle: 0 });
});

test('missing layout is omitted instead of fabricating a top-left animation', () => {
    assert.deepEqual(capture([opponent(1, box(0, 0, 0, 0), box(0, 0, 0, 0))]), {});
});
