// Flowing-light controller for the Blazor workflow demo's links.
//
// ONE requestAnimationFrame loop drives every registered link. Blazor renders the source path and
// the 16 + 16 comet paths once; from then on this module owns their geometry and alpha, so the
// .NET side never renders a frame and never builds a path string per frame.
//
// The tail spans the last TAIL_FRACTION of the curve, cut into SEGMENTS pieces by arc length.
// Piece k covers [head - tail*(1-k/16), head - tail*(1-(k+1)/16)], so neighbouring pieces share
// boundaries and the halo pass reuses the body pass's points. Points come from the source path via
// getTotalLength()/getPointAtLength(), which keeps the cut on the drawn curve instead of a raw
// control-point approximation.

const CYCLE_MS = 1600;                 // 450 enter + 700 travel + 450 exit
const ENTER_SHARE = 450 / CYCLE_MS;
const TRAVEL_SHARE = 700 / CYCLE_MS;
const EXIT_START = ENTER_SHARE + TRAVEL_SHARE;
const BAND_FORMED = 0.30;              // head position (fraction of length) at each band end
const BAND_LEAVING = 0.78;
const TAIL_FRACTION = 0.30;
const SEGMENTS = 16;
const HALO_OPACITY = 0.22;
const HALO_EXTRA_WIDTH = 9;
const MIN_VISIBLE_ALPHA = 0.004;
const FRAME_MS = 1000 / 30;            // 30fps cap, shared by every link
const MAX_DELTA_MS = 100;              // clamp a tab-switch / long stall so the comet never jumps
const CHORD_TARGET_PX = 16;            // one sub-chord per ~16 path units, matching the 128-point body
const MAX_CHORDS = 4;

const entries = new Map();             // id -> entry
const byElement = new WeakMap();       // svg Element -> entry
let nextId = 1;
let rafId = 0;
let lastFrame = 0;
let observer = null;

function now() {
    return typeof performance !== 'undefined' ? performance.now() : Date.now();
}

function clamp01(value) {
    return value < 0 ? 0 : value > 1 ? 1 : value;
}

// 2 decimals keeps the d string short; the body path is authored at the same order of precision.
function q(value) {
    return Math.round(value * 100) / 100;
}

// Phase -> head position, piecewise over the enter / travel / exit bands. This is the .NET
// mapping moved verbatim into the frame loop.
function headAt(phase) {
    if (phase <= ENTER_SHARE) {
        return BAND_FORMED * (phase / ENTER_SHARE);
    }
    if (phase <= EXIT_START) {
        return BAND_FORMED + ((BAND_LEAVING - BAND_FORMED) * ((phase - ENTER_SHARE) / TRAVEL_SHARE));
    }
    return BAND_LEAVING + ((1 - BAND_LEAVING) * ((phase - EXIT_START) / ENTER_SHARE));
}

// Phase -> comet intensity: ramps in, holds, ramps out.
function intensityAt(phase) {
    if (phase <= ENTER_SHARE) {
        return phase / ENTER_SHARE;
    }
    if (phase <= EXIT_START) {
        return 1;
    }
    return 1 - ((phase - EXIT_START) / ENTER_SHARE);
}

/**
 * Registers one link's SVG and returns the numeric key used by update/unregister.
 * colors is the 16-entry palette (tail colour -> white-hot head) computed by Blazor on style
 * changes only; thickness / litThickness drive the body and halo widths.
 */
export function register(svg, colors, thickness, litThickness) {
    // A batched re-render can register the same SVG twice; never let two entries chase one element.
    const previous = byElement.get(svg);
    if (previous) {
        remove(previous);
    }

    const entry = {
        id: nextId++,
        svg,
        source: svg.querySelector('.wf-flow-source'),
        halo: svg.querySelectorAll('.wf-flow-halo'),
        body: svg.querySelectorAll('.wf-flow-comet'),
        colors: colors || [],
        thickness: thickness > 0 ? thickness : 2,
        litThickness: litThickness > 0 ? litThickness : thickness,
        length: 0,
        elapsed: 0,
        last: now(),
        visible: true,
        hidden: new Array(SEGMENTS).fill(false),
        px: new Float64Array(SEGMENTS + 1),
        py: new Float64Array(SEGMENTS + 1),
    };

    entries.set(entry.id, entry);
    byElement.set(svg, entry);
    // The surface's drag pass rewrites the source path from the live slot boxes. The cached arc
    // length belongs to the old geometry, so drop it and let the next frame re-measure once.
    svg.addEventListener('veloxdev-link-geometry', function () {
        entry.length = 0;
    });
    applyStyle(entry);
    observe(entry);
    ensureLoop();
    return entry.id;
}

/** Pushes new colours / widths and invalidates the cached arc length after a geometry change. */
export function update(id, colors, thickness, litThickness) {
    const entry = entries.get(id);
    if (!entry) {
        return;
    }

    if (colors) {
        entry.colors = colors;
    }
    if (thickness > 0) {
        entry.thickness = thickness;
    }
    if (litThickness > 0) {
        entry.litThickness = litThickness;
    }

    // The body path may have been re-rendered by Blazor; re-measure and repaint every piece.
    entry.length = 0;
    entry.hidden.fill(false);
    applyStyle(entry);
    ensureLoop();
}

/** Removes the link from the shared loop. Safe to call more than once. */
export function unregister(id) {
    const entry = entries.get(id);
    if (entry) {
        remove(entry);
    }
    if (entries.size === 0) {
        stopLoop();
    }
}

function remove(entry) {
    byElement.delete(entry.svg);
    entries.delete(entry.id);
    if (observer) {
        try { observer.unobserve(entry.svg); } catch { /* already detached */ }
    }
}

// Colours and stroke widths only change when Blazor pushes an update, never per frame.
function applyStyle(entry) {
    const haloWidth = entry.litThickness + HALO_EXTRA_WIDTH;
    for (let k = 0; k < SEGMENTS; k++) {
        const color = entry.colors[k];
        const halo = entry.halo[k];
        const body = entry.body[k];

        if (halo) {
            if (color) {
                halo.setAttribute('stroke', color);
            }
            halo.setAttribute('stroke-width', haloWidth);
        }
        if (body) {
            if (color) {
                body.setAttribute('stroke', color);
            }
            body.setAttribute('stroke-width', entry.thickness * (0.45 + (0.95 * (k / SEGMENTS))));
        }
    }
}

function ensureLoop() {
    if (rafId === 0 && entries.size > 0) {
        rafId = requestAnimationFrame(tick);
    }
}

function stopLoop() {
    if (rafId !== 0) {
        cancelAnimationFrame(rafId);
        rafId = 0;
    }
}

function tick(timestamp) {
    rafId = 0;
    if (entries.size === 0) {
        return;
    }

    // 30fps cap: the browser may hand us 60 or 120 frames; skip the DOM writes until ~33ms passed.
    if (timestamp - lastFrame < FRAME_MS) {
        ensureLoop();
        return;
    }
    lastFrame = timestamp;

    let active = false;
    for (const entry of entries.values()) {
        if (!entry.visible || !entry.svg.isConnected) {
            entry.last = timestamp;
            continue;
        }
        active = true;
        updateEntry(entry, timestamp);
    }

    // When every link is offscreen (or detached) the loop stops; IntersectionObserver restarts it.
    if (active) {
        ensureLoop();
    }
}

function updateEntry(entry, timestamp) {
    const dt = timestamp - entry.last;
    entry.last = timestamp;
    entry.elapsed = (entry.elapsed + (dt > 0 ? Math.min(dt, MAX_DELTA_MS) : 0)) % CYCLE_MS;

    const source = entry.source;
    if (!source || !source.isConnected) {
        return;
    }

    if (entry.length === 0) {
        try {
            entry.length = source.getTotalLength();
        } catch {
            entry.length = 0;
        }
        if (entry.length <= 0) {
            return;
        }
    }

    const phase = entry.elapsed / CYCLE_MS;
    const intensity = intensityAt(phase);
    const head = clamp01(headAt(phase)) * entry.length;
    const tail = TAIL_FRACTION * entry.length;
    const segmentLength = tail / SEGMENTS;

    // The 17 boundaries of the tail window, shared by both passes and by neighbouring pieces.
    const px = entry.px;
    const py = entry.py;
    for (let j = 0; j <= SEGMENTS; j++) {
        // The tail window starts before 0 while the head is still near the sender; SVG's
        // getPointAtLength is not guaranteed to accept a negative length on every engine.
        const at = head - (tail * (1 - (j / SEGMENTS)));
        const point = source.getPointAtLength(at < 0 ? 0 : at > entry.length ? entry.length : at);
        px[j] = point.x;
        py[j] = point.y;
    }

    const chords = Math.min(MAX_CHORDS, Math.max(1, Math.ceil(segmentLength / CHORD_TARGET_PX)));

    for (let k = 0; k < SEGMENTS; k++) {
        const f0 = k / SEGMENTS;
        const alpha = intensity * f0 * f0;
        const b0 = head - (tail * (1 - f0));
        const b1 = head - (tail * (1 - ((k + 1) / SEGMENTS)));
        const hidden = b1 <= 0 || b0 >= entry.length || alpha <= MIN_VISIBLE_ALPHA;

        const halo = entry.halo[k];
        const body = entry.body[k];

        if (hidden) {
            // Repaint once on the falling edge; a remain-hidden piece costs no DOM write.
            if (!entry.hidden[k]) {
                entry.hidden[k] = true;
                if (halo) {
                    halo.setAttribute('d', '');
                    halo.setAttribute('stroke-opacity', '0');
                }
                if (body) {
                    body.setAttribute('d', '');
                    body.setAttribute('stroke-opacity', '0');
                }
            }
            continue;
        }

        entry.hidden[k] = false;

        // Follow the curve inside the piece; a bare chord would cut across tight bends.
        let d = 'M' + q(px[k]) + ',' + q(py[k]);
        for (let c = 1; c < chords; c++) {
            const at = b0 + ((b1 - b0) * (c / chords));
            const point = source.getPointAtLength(at < 0 ? 0 : at > entry.length ? entry.length : at);
            d += 'L' + q(point.x) + ',' + q(point.y);
        }
        d += 'L' + q(px[k + 1]) + ',' + q(py[k + 1]);

        if (halo) {
            halo.setAttribute('d', d);
            halo.setAttribute('stroke-opacity', String(alpha * HALO_OPACITY));
        }
        if (body) {
            body.setAttribute('d', d);
            body.setAttribute('stroke-opacity', String(alpha));
        }
    }
}

function observe(entry) {
    if (typeof IntersectionObserver === 'undefined') {
        return;
    }
    if (!observer) {
        observer = new IntersectionObserver(onIntersect, { threshold: 0 });
    }
    try {
        observer.observe(entry.svg);
    } catch {
        // detached before observation; C# unregisters it on the next render.
    }
}

function onIntersect(records) {
    let resumed = false;
    for (const record of records) {
        const entry = byElement.get(record.target);
        if (!entry || entry.visible === record.isIntersecting) {
            continue;
        }

        entry.visible = record.isIntersecting;
        if (entry.visible) {
            // Do not advance the phase by the time spent offscreen.
            entry.last = now();
            resumed = true;
        }
    }

    if (resumed) {
        ensureLoop();
    }
}
