// VeloxDev Workflow — Blazor interaction helpers.
// Surface pan / scroll, node drag, slot connection, slot layout measurement, and minimap navigation.
// Every init* function returns a `{ dispose() }` handle so components can tear down listeners.

// ── Generic browser utilities (exposed on window, used via JSInvoke) ──────────
window.downloadFile = function (fileName, contentType, base64) {
    const bytes = atob(base64);
    const arr = new Uint8Array(bytes.length);
    for (let i = 0; i < bytes.length; i++) arr[i] = bytes.charCodeAt(i);
    const blob = new Blob([arr], { type: contentType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

window.openFileDialog = function (accept) {
    return new Promise(function (resolve) {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = accept || '';
        input.onchange = function () {
            const file = input.files && input.files[0];
            if (!file) { resolve(null); return; }
            const reader = new FileReader();
            reader.onload = function () { resolve(reader.result); };
            reader.readAsText(file);
        };
        input.click();
    });
};

window.veloxdevWorkflow = (() => {
    'use strict';

    // Reads the translate of a canvas element from its computed CSS transform.
    // Serialized as matrix(a,b,c,d,e,f) or matrix3d(...). Falls back to {0,0}.
    function getCanvasTranslate(canvasEl) {
        if (!canvasEl) return { x: 0, y: 0 };
        const st = getComputedStyle(canvasEl);
        const t = st.transform;
        if (!t || t === 'none') return { x: 0, y: 0 };
        const m = t.match(/matrix\(([^)]+)\)/);
        if (m) {
            const parts = m[1].split(',').map(v => parseFloat(v.trim()));
            return { x: parts[4] || 0, y: parts[5] || 0 };
        }
        const m3 = t.match(/matrix3d\(([^)]+)\)/);
        if (m3) {
            const parts = m3[1].split(',').map(v => parseFloat(v.trim()));
            return { x: parts[12] || 0, y: parts[13] || 0 };
        }
        return { x: 0, y: 0 };
    }

    // ════════════════════════════════════════════════════════════
    // SURFACE — canvas pan (middle mouse / space+left / left on blank),
    // scroll reporting, and auto-expansion near edges.
    // ════════════════════════════════════════════════════════════
    // Surfaces register here by scroller id so other surfaces (e.g. the minimap) can drive
    // scroll + edge expansion through the same code path.
    const surfaceRegistry = {};

    // The .NET minimap pushes its rendered content-fit mapping here by scroller id. Minimap
    // drag/click navigation reads it so a drag tracks the on-screen viewport rectangle 1:1 even
    // after the canvas has been edge-extended (the raw scroll-extent ratio would overshoot).
    const minimapMappings = {};

    // The minimap's viewport-block <rect> plus its pixel dimensions, keyed by scroller id and
    // registered by initMinimap. The surface's OnSurfaceScroll pushes the current viewport world
    // rect here and we move the block directly — no .NET re-render per scroll frame.
    const minimapRects = {};

    // Last viewport world rect pushed per scroller id. Used by refreshMinimapViewport to re-apply
    // the block position after a .NET re-render would otherwise overwrite it with stale data.
    const minimapLastWorld = {};

    // Per-surface edge-expansion + layout-offset state, keyed by scroller id. The content wrapper
    // (nodes/links) is positioned at edge + ActualOffset so world 0 lands at the grid/axis origin —
    // a non-zero ActualOffset (e.g. NegativeOffset shifting the origin into view) must move the nodes
    // too, or they collapse toward the edge instead of the world origin under workspace zoom.
    const surfaceLayouts = {};

    // Per-surface host-size growers, keyed by scroller id. The workspace-zoom path (ensureCanvasSize)
    // must grow the DOM canvas host when the model ActualSize auto-extends; the grow runs inside
    // initSurface where it can report the new size back to .NET. Fallback path below sets the host
    // style directly.
    const surfaceSizers = {};

    function setMinimapMapping(scrollerId, scale, ox, oy, minX, minY, maxX, maxY) {
        minimapMappings[scrollerId] =
            (isFinite(scale) && scale > 0) ? { scale, ox, oy, minX, minY, maxX, maxY } : null;
    }

    // Positions a node wrapper at an absolute world coordinate (canvas-content-relative). Used by the
    // node-drag behavior for external anchor changes (undo/redo, layout commands); during a live drag
    // the drag handler owns the position directly.
    function setNodePosition(nodeEl, x, y) {
        if (!nodeEl) return;
        nodeEl.style.left = x + 'px';
        nodeEl.style.top = y + 'px';
    }

    // Moves the minimap's viewport-block <rect> to the current viewport world rect. Inverts the same
    // content-fit mapping used to render the minimap and applies the same clamp, so the block stays
    // inside the minimap even when the viewport is larger than the fitted content. Also stores the
    // pushed world rect so refreshMinimapViewport can re-apply it after a .NET re-render.
    function setMinimapViewport(scrollerId, worldX, worldY, vw, vh) {
        minimapLastWorld[scrollerId] = { x: worldX, y: worldY, w: vw, h: vh };
        const reg = minimapRects[scrollerId];
        if (!reg) return;
        const m = minimapMappings[scrollerId];
        if (!m || !(m.scale > 0)) return;
        let x = m.ox + (worldX - m.minX) * m.scale;
        let y = m.oy + (worldY - m.minY) * m.scale;
        let w = Math.max(2, vw * m.scale);
        let h = Math.max(2, vh * m.scale);
        w = Math.min(w, reg.width);
        h = Math.min(h, reg.height);
        x = Math.max(0, Math.min(x, reg.width - w));
        y = Math.max(0, Math.min(y, reg.height - h));
        reg.rect.setAttribute('x', x.toFixed(1));
        reg.rect.setAttribute('y', y.toFixed(1));
        reg.rect.setAttribute('width', w.toFixed(1));
        reg.rect.setAttribute('height', h.toFixed(1));
    }

    // Re-applies the last pushed viewport world rect to the minimap block. Called by the minimap
    // component after every .NET re-render, because a re-render rewrites the block from stale .NET
    // state (MappedViewport); this restores the JS-owned, current position.
    function refreshMinimapViewport(scrollerId) {
        const last = minimapLastWorld[scrollerId];
        if (!last) return;
        setMinimapViewport(scrollerId, last.x, last.y, last.w, last.h);
    }

    // Scrolls a surface by a world delta, routing through the surface's edge-aware scrollBy so a
    // drag past a minimap edge grows the canvas instead of clamping. Falls back to direct scroll.
    function scrollByDelta(scrollerId, dx, dy) {
        const el = document.getElementById(scrollerId);
        if (!el) return;
        const scrollBy = surfaceRegistry[scrollerId];
        if (scrollBy) {
            scrollBy(dx, dy);
            return;
        }
        el.scrollLeft += dx;
        el.scrollTop += dy;
    }

    // Updates a surface's grid/axis layers for a new layout (content) offset. Called by .NET when
    // layout.ActualOffset changes (rare). The edge-expansion offset is read from the content
    // wrapper's inline position, so this needs no per-surface closure state.
    function setSurfaceLayout(scrollerId, contentX, contentY) {
        const el = document.getElementById(scrollerId);
        if (!el) return;
        const st = surfaceLayouts[scrollerId];
        if (!st) return;
        // The layout offset (ActualOffset) moves the whole world — content (nodes/links), grid and
        // axis together — so nodes keep collapsing toward the grid/origin under workspace zoom.
        st.layoutX = contentX || 0;
        st.layoutY = contentY || 0;
        const gx = st.edgeX + st.layoutX;
        const gy = st.edgeY + st.layoutY;
        const host = el.querySelector('.veloxdev-wf-canvas-host');
        if (!host) return;
        const contentEl = host.querySelector('.veloxdev-wf-canvas-content');
        if (contentEl) { contentEl.style.left = gx + 'px'; contentEl.style.top = gy + 'px'; }
        const gridEl = host.querySelector('.veloxdev-wf-grid');
        if (gridEl) gridEl.style.backgroundPosition = gx + 'px ' + gy + 'px';
        const axisXEl = host.querySelector('.veloxdev-wf-axis-x');
        if (axisXEl) axisXEl.style.left = gx + 'px';
        const axisYEl = host.querySelector('.veloxdev-wf-axis-y');
        if (axisYEl) axisYEl.style.top = gy + 'px';
    }

    // Grows the canvas host to the given pixel size (grow-only) and reports the new size back to
    // .NET. Called by the workspace-zoom path: zoom-in below scale 1 auto-extends the model
    // ActualSize (the links layer re-renders larger), so the DOM scroll content must grow to match
    // or the pivot-centering scroll clamps short of the model extent and the node drifts each notch.
    function ensureCanvasSize(scrollerId, width, height) {
        const grow = surfaceSizers[scrollerId];
        if (grow) { grow(width, height); return; }
        const el = document.getElementById(scrollerId);
        if (!el) return;
        const host = el.querySelector('.veloxdev-wf-canvas-host');
        if (!host) return;
        const w = Math.max(width || 0, host.offsetWidth || 0);
        const h = Math.max(height || 0, host.offsetHeight || 0);
        if (w !== (host.offsetWidth || 0) || h !== (host.offsetHeight || 0)) {
            host.style.width = w + 'px';
            host.style.height = h + 'px';
        }
    }

    // Applies one node's collapsed geometry to its pooled wrapper + card in place. The wrapper is
    // keyed by data-veloxdev-node-id (stable across re-renders); its child .veloxdev-wf-node-card is
    // laid out at the DESIGN size (260x180 in NodeView) and uniformly scaled by collapsedWidth/260.
    // This runs synchronously inside applyZoomSurface so node geometry lands in the SAME browser
    // frame as the scroll step — never one async render behind it (the zoom flicker).
    function applyNodeGeometry(nodeWrappers, geometry) {
        if (!nodeWrappers || !geometry || !geometry.length) return;
        for (let i = 0; i < geometry.length; i++) {
            const g = geometry[i];
            if (!g || g.length < 5) continue;
            const el = nodeWrappers[g[0]];
            if (!el) continue;
            const left = parseFloat(g[1]); const top = parseFloat(g[2]);
            const width = parseFloat(g[3]); const height = parseFloat(g[4]);
            if (!(width > 0) || !(height > 0)) continue;
            el.style.left = left + 'px';
            el.style.top = top + 'px';
            el.style.width = width + 'px';
            el.style.height = height + 'px';
            // Collapse the card to its DESIGN size (width/260 keeps a single source of truth that
            // matches the NodeView ScaleCss: card scale == collapsed width / design width 260).
            const card = el.querySelector('.veloxdev-wf-node-card');
            if (card) {
                const s = width / 260;
                card.style.transform = 'scale(' + s.toFixed(4) + ')';
                card.style.transformOrigin = 'top left';
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    // ZOOM LINK SYNC — rewrites link polylines from the live endpoint
    // slots in the same frame the nodes collapse (Route A for Blazor).
    // ════════════════════════════════════════════════════════════
    // Link endpoints normally round-trip through .NET: initSlotLayout measures the slot DOM,
    // reports OnSlotLayoutBatch, .NET writes slot.Anchor, and the LinkView re-renders the polyline.
    // That is inherently async (≥1 frame behind the node collapse), so during a wheel zoom the
    // links paint at the old scale under the already-collapsed nodes/scroll for a frame — the
    // endpoint drift/flicker this path eliminates. The golden-ratio stub coefficient is mirrored
    // from the .NET LinkView.BuildPoints so the JS-written points are pixel-identical to what a
    // later .NET render produces once it re-measures the same geometry.
    const LINK_PHI = 0.6180339887;

    // The on-screen box to measure for a slot: prefer the actual glyph (<svg.veloxdev-wf-slot-svg>)
    // inside the stamped wrapper, so link endpoints sit on the drawing's center and not on the wrapper
    // box — an inline <svg> leaves a baseline-gap strip below it that makes the wrapper center drift
    // off the glyph. Falls back to the wrapper when no glyph element is present.
    function glyphRect(el) {
        if (!el) return null;
        const glyph = el.querySelector('.veloxdev-wf-slot-svg');
        return glyph ? glyph.getBoundingClientRect() : el.getBoundingClientRect();
    }

    // Resolves a link's live <polyline> by its stamped data-veloxdev-link-id. Link SVGs are not
    // pooled, but Blazor diffs attributes in place across re-renders, so the element identity
    // survives and a per-pass re-query is all this costs.
    function resolveLinkPolyline(host, linkId) {
        if (!host || !linkId) return null;
        const svg = host.querySelector('[data-veloxdev-link-id="' + linkId + '"]');
        return svg ? svg.querySelector('polyline') : null;
    }

    // Measures one link's endpoints from the LIVE slot elements using the same formula as
    // initSlotLayout's measure() (element center relative to the canvas, minus the content-wrapper
    // translate) and formats the polyline with LinkView's signed golden-ratio stubs. Returns '' when
    // an endpoint has no measurable slot DOM yet (virtual-gesture receiver, virtualized node, fresh
    // mount) — .NET owns those links until a real measurement lands, so the caller leaves them alone.
    function linkPointsFromSlotElements(host, senderSlotId, receiverSlotId) {
        if (!host || !senderSlotId || !receiverSlotId) return '';
        const canvasEl = host.querySelector('.veloxdev-wf-canvas');
        if (!canvasEl) return '';
        const sEl = canvasEl.querySelector('[data-veloxdev-slot-id="' + senderSlotId + '"]');
        const rEl = canvasEl.querySelector('[data-veloxdev-slot-id="' + receiverSlotId + '"]');
        if (!sEl || !rEl) return '';
        const sr = glyphRect(sEl);
        const rr = glyphRect(rEl);
        if (!sr || !rr || sr.width <= 0 || sr.height <= 0 || rr.width <= 0 || rr.height <= 0) return '';
        const rect = canvasEl.getBoundingClientRect();
        const contentEl = canvasEl.querySelector('.veloxdev-wf-canvas-content');
        const contentX = contentEl ? contentEl.offsetLeft : 0;
        const contentY = contentEl ? contentEl.offsetTop : 0;
        const sx = (sr.left + sr.width / 2) - rect.left - contentX;
        const sy = (sr.top + sr.height / 2) - rect.top - contentY;
        const ex = (rr.left + rr.width / 2) - rect.left - contentX;
        const ey = (rr.top + rr.height / 2) - rect.top - contentY;
        if (!isFinite(sx) || !isFinite(sy) || !isFinite(ex) || !isFinite(ey)) return '';
        const dx = ex - sx;
        // Signed stub keeps the orthogonal bend on the correct side when dragging leftward.
        const stub = dx / 2.0 * (1.0 - LINK_PHI);
        const p1x = sx + stub;
        const p4x = ex - stub;
        return sx.toFixed(1) + ',' + sy.toFixed(1) + ' ' +
            p1x.toFixed(1) + ',' + sy.toFixed(1) + ' ' +
            p4x.toFixed(1) + ',' + ey.toFixed(1) + ' ' +
            ex.toFixed(1) + ',' + ey.toFixed(1);
    }

    // Writes every link's polyline points synchronously from its endpoints' CURRENT slot DOM. Runs
    // inside applyZoomSurface immediately after applyNodeGeometry collapsed the node wrappers, so a
    // zoom step paints nodes + links + scroll in ONE browser frame. Reading getBoundingClientRect
    // after the geometry writes forces a synchronous layout, so the measured centers are exactly the
    // collapsed values .NET will converge on — never a stale pre-collapse endpoint. Only links whose
    // endpoints are materialized and measurable are stamped; the rest (.NET-owned until measured)
    // are skipped. Returns the stamped [{linkId, points}] records for the zoom settle guard to
    // re-assert against stale async .NET renders.
    function applyZoomLinkPoints(host) {
        if (!host) return null;
        const linkEls = host.querySelectorAll('[data-veloxdev-link-id]');
        if (!linkEls.length) return null;
        const stamped = [];
        for (let i = 0; i < linkEls.length; i++) {
            const svg = linkEls[i];
            const linkId = svg.getAttribute('data-veloxdev-link-id');
            const points = linkPointsFromSlotElements(
                host,
                svg.getAttribute('data-veloxdev-sender-slot'),
                svg.getAttribute('data-veloxdev-receiver-slot'));
            if (!points) continue;
            const poly = svg.querySelector('polyline');
            if (poly && poly.getAttribute('points') !== points) {
                poly.setAttribute('points', points);
            }
            stamped.push({ linkId: linkId, points: points });
        }
        return stamped.length ? stamped : null;
    }

    // One atomic workspace-zoom step: re-translate content/grid/axis to the NEW layout offset, grow
    // the canvas host to the auto-extended model content size, set the scroll, and reposition every
    // node's pooled wrapper to its collapsed geometry — all in a single synchronous block so the
    // browser paints them together. Without this, setting scrollLeft from .NET while the content
    // translate is still the OLD value paints one frame with the wrapper in the old spot (a left-right
    // jump every zoom notch), and a host that never grows leaves the pivot-centering scroll clamped
    // short of the model extent (drift on every notch).
    //
    // All lengths except the layout offset are EFFECTIVE (canvas/world) lengths: the DOM translate
    // = this edge reserve (st.edgeX) + layout offset, the host pixel width = st.edgeX + content width,
    // and the scroll range must reach st.edgeX + contentW − viewport. JS owns the edge reserve, so it
    // adds it here; .NET should never pass a raw host/scroll value built from its own (lagging) edge.
    //
    // nodeGeometry (optional, last) is a string[][] of [nodeId, collapsedLeft, collapsedTop,
    // collapsedWidth, collapsedHeight] built by the .NET side AFTER Scale changed, so the values are
    // already the post-collapse CSS lengths. The wrappers were found by scanning once per surface.
    function applyZoomSurface(scrollerId, layoutX, layoutY, contentW, contentH, scrollX, scrollY, nodeGeometry) {
        const el = document.getElementById(scrollerId);
        if (!el) return;
        const host = el.querySelector('.veloxdev-wf-canvas-host');
        if (host) {
            const st = surfaceLayouts[scrollerId];
            const edgeX = st ? st.edgeX : 0;
            const edgeY = st ? st.edgeY : 0;
            // Translate the whole content + grid + axis block to the new layout offset BEFORE the
            // scroll lands, so the world-to-viewport relationship is consistent in this frame.
            const gx = edgeX + (layoutX || 0);
            const gy = edgeY + (layoutY || 0);
            const contentEl = host.querySelector('.veloxdev-wf-canvas-content');
            if (contentEl) {
                contentEl.style.left = gx + 'px';
                contentEl.style.top = gy + 'px';
            }
            if (st) { st.layoutX = layoutX || 0; st.layoutY = layoutY || 0; }
            const gridEl = host.querySelector('.veloxdev-wf-grid');
            if (gridEl) gridEl.style.backgroundPosition = gx + 'px ' + gy + 'px';
            const axisXEl = host.querySelector('.veloxdev-wf-axis-x');
            if (axisXEl) axisXEl.style.left = gx + 'px';
            const axisYEl = host.querySelector('.veloxdev-wf-axis-y');
            if (axisYEl) axisYEl.style.top = gy + 'px';
            // Grow the host to cover the auto-extended model content (grow-only; a re-render or a
            // later edge-pan can only enlarge it further). The scroll range must reach the pivot.
            const hostW = edgeX + (contentW || 0);
            const hostH = edgeY + (contentH || 0);
            const w = Math.max(hostW, host.offsetWidth || 0);
            const h = Math.max(hostH, host.offsetHeight || 0);
            if (w !== (host.offsetWidth || 0)) host.style.width = w + 'px';
            if (h !== (host.offsetHeight || 0)) host.style.height = h + 'px';
            // Reposition every node's pooled wrapper + card to the collapsed geometry in this same
            // block (lazy map built once per surface) — the scroll below and these writes share a
            // frame, so a fast wheel can never paint nodes at the old scale under the new scroll.
            let nodeWrappers = surfaceNodeWrappers[scrollerId];
            if (!nodeWrappers && nodeGeometry && nodeGeometry.length) {
                const map = {};
                const wrappers = host.querySelectorAll('.veloxdev-wf-node-drag');
                for (let i = 0; i < wrappers.length; i++) {
                    const id = wrappers[i].getAttribute('data-veloxdev-node-id');
                    if (id) map[id] = wrappers[i];
                }
                nodeWrappers = map;
                surfaceNodeWrappers[scrollerId] = map;
            }
            applyNodeGeometry(nodeWrappers, nodeGeometry);
            // Collapse the link endpoints in this same block: read each endpoint slot's live center
            // (getBoundingClientRect after the wrapper writes above forces the new layout) and write
            // the polyline. Without this the links wait for the async measure→.NET anchor→render
            // round trip and paint one or more frames at the old scale under the new nodes/scroll.
            const stampedLinks = (nodeGeometry && nodeGeometry.length)
                ? applyZoomLinkPoints(host)
                : null;
            // Apply the effective scroll (add the edge reserve the effective value already excluded).
            el.scrollLeft = Math.max(0, edgeX + (scrollX || 0));
            el.scrollTop = Math.max(0, edgeY + (scrollY || 0));
            // Stamp this step's authoritative collapsed node geometry AND link points, then (re)start
            // the settle guard, so a stale async .NET render that lands later (each node re-renders
            // as its own SignalR message, and under a human fast-flick several zoom steps overlap
            // server-side) can never paint even one frame of old collapsed values. The translate +
            // host size + scroll were already written above and are JS-owned (async renders never
            // touch them), so only node geometry and link points need guarding. stampedAt anchors a
            // time-based settle tail below.
            surfaceZoomState[scrollerId] = {
                stampedAt: performance.now(),
                geometry: nodeGeometry || null,
                links: stampedLinks
            };
            scheduleZoomSettle(scrollerId);
        }
        // Report the settled DOM geometry (host size + scroll + offsets) back to .NET so its canvas
        // bookkeeping is correct for the next zoom notch. Runs even when the scroll didn't move.
        const reporter = surfaceReporters[scrollerId];
        if (reporter) reporter();
    }

    // Per-surface node-wrapper lookup (by data-veloxdev-node-id), built lazily on the first zoom that
    // carries node geometry. Cleared when the surface is disposed; a node added/removed mid-session is
    // still found because applyNodeGeometry misses are harmless and the map re-syncs on the next zoom.
    const surfaceNodeWrappers = {};

    // Authoritative collapsed node geometry and link points stamped by each applyZoomSurface step. A
    // per-surface settle loop (scheduleZoomSettle) re-asserts them every animation frame for a short
    // tail after the last zoom step, so a STALE async .NET render that lands late (each node/endpoint
    // re-renders as its own SignalR message; when two zoom bursts overlap server-side, the first
    // burst's renders can arrive after the second burst already applied) is overwritten before it can
    // be composited — never a painted flash-back. Only node geometry and link points are guarded:
    // async renders never write the content translate, host size, or scroll (those are JS-owned), so
    // re-asserting them would only risk fighting a user pan. Keyed by scroller id.
    const surfaceZoomState = {};

    // True while a surface's settle loop is running (one rAF chain per surface).
    const surfaceSettleRunning = {};

    // Re-asserts a surface's stamped collapsed node geometry onto the live wrappers. Returns true if
    // any write actually changed the DOM (a stale async render landed and was corrected).
    function applyZoomGeometrySettle(scrollerId) {
        const z = surfaceZoomState[scrollerId];
        const geometry = z ? z.geometry : null;
        if (!geometry || !geometry.length) return false;
        const el = document.getElementById(scrollerId);
        if (!el) return false;
        const host = el.querySelector('.veloxdev-wf-canvas-host');
        if (!host) return false;
        let nodeWrappers = surfaceNodeWrappers[scrollerId];
        if (!nodeWrappers) {
            const map = {};
            const wrappers = host.querySelectorAll('.veloxdev-wf-node-drag');
            for (let i = 0; i < wrappers.length; i++) {
                const id = wrappers[i].getAttribute('data-veloxdev-node-id');
                if (id) map[id] = wrappers[i];
            }
            nodeWrappers = map;
            surfaceNodeWrappers[scrollerId] = map;
        }
        let dirty = false;
        for (let i = 0; i < geometry.length; i++) {
            const g = geometry[i];
            if (!g || g.length < 5) continue;
            const wEl = nodeWrappers[g[0]];
            if (!wEl) continue;
            const l = parseFloat(g[1]), t = parseFloat(g[2]);
            const w = parseFloat(g[3]), h = parseFloat(g[4]);
            if (!(w > 0) || !(h > 0)) continue;
            const leftStr = l + 'px', topStr = t + 'px', wStr = w + 'px', hStr = h + 'px';
            if (wEl.style.left !== leftStr) { wEl.style.left = leftStr; dirty = true; }
            if (wEl.style.top !== topStr) { wEl.style.top = topStr; dirty = true; }
            if (wEl.style.width !== wStr) { wEl.style.width = wStr; dirty = true; }
            if (wEl.style.height !== hStr) { wEl.style.height = hStr; dirty = true; }
            const card = wEl.querySelector('.veloxdev-wf-node-card');
            if (card) {
                const s = w / 260;
                const tf = 'scale(' + s.toFixed(4) + ')';
                const cur = card.style.transform;
                if (cur !== tf && cur !== 'none') { card.style.transform = tf; dirty = true; }
            }
        }
        return dirty;
    }

    // Re-asserts a surface's stamped link points onto its live polylines, mirroring
    // applyZoomGeometrySettle for nodes: a stale async .NET LinkView render (an older zoom step's
    // anchors landing after the newest step already applied) is overwritten with the stamped
    // collapsed points before it can be painted. Returns true if any write actually changed the DOM.
    function applyZoomLinkSettle(scrollerId) {
        const z = surfaceZoomState[scrollerId];
        const links = z ? z.links : null;
        if (!links || !links.length) return false;
        const el = document.getElementById(scrollerId);
        if (!el) return false;
        const host = el.querySelector('.veloxdev-wf-canvas-host');
        if (!host) return false;
        let dirty = false;
        for (let i = 0; i < links.length; i++) {
            const l = links[i];
            const poly = resolveLinkPolyline(host, l.linkId);
            if (!poly) continue;
            const cur = poly.getAttribute('points');
            if (cur !== l.points) { poly.setAttribute('points', l.points); dirty = true; }
        }
        return dirty;
    }

    // Keeps a surface's settle loop alive: every animation frame it re-asserts the stamped geometry
    // so no stale async render can be painted, until the stamp has been stable for SETTLE_TAIL_MS
    // after the LAST zoom step (a fresh applyZoomSurface re-stamps and extends the tail, so a running
    // burst never terminates mid-way). Stops immediately if the user starts a real pan/drag (their
    // action supersedes the stamp).
    const SETTLE_TAIL_MS = 250;
    function scheduleZoomSettle(scrollerId) {
        if (surfaceSettleRunning[scrollerId]) return;
        surfaceSettleRunning[scrollerId] = true;
        const tick = function () {
            const z = surfaceZoomState[scrollerId];
            if (!z) { surfaceSettleRunning[scrollerId] = false; return; }
            applyZoomGeometrySettle(scrollerId);
            applyZoomLinkSettle(scrollerId);
            // Keep re-asserting until the tail after the last stamp elapses (late async renders are
            // overwritten whenever they land inside the window; the re-assert is idempotent once the
            // DOM matches, so extra clean frames are cheap). A real pointer gesture pre-empts the
            // guard via the scroller's pointerdown handler (deletes the stamp), so the next tick sees
            // no state and halts — the guard never fights a user pan or drag.
            if (z && performance.now() - z.stampedAt < SETTLE_TAIL_MS) {
                requestAnimationFrame(tick);
            } else {
                surfaceSettleRunning[scrollerId] = false;
                delete surfaceZoomState[scrollerId];
            }
        };
        requestAnimationFrame(tick);
    }

    // Per-surface DOM→.NET reporters, keyed by scroller id. Used by applyZoomSurface to sync .NET's
    // canvas bookkeeping after it mutates translate + host size + scroll in one block.
    const surfaceReporters = {};

    function initSurface(scrollerEl, canvasHostEl, dotnetRef, initialW, initialH, contentX, contentY, initialOffsetX, initialOffsetY) {
        if (!scrollerEl || !canvasHostEl) return null;
        let panState = null;
        let spaceHeld = false;

        // The canvas host owns the pixel size (set here + grown by edge expansion) so a Blazor
        // re-render never writes it. The content wrapper inside is translated by the "negative
        // offset" so the canvas can expand in all four directions: growing right/down enlarges the
        // host; growing left/up enlarges the host AND shifts the content wrapper right/down.
        const canvasEl = canvasHostEl.querySelector('.veloxdev-wf-canvas');
        const contentEl = canvasHostEl.querySelector('.veloxdev-wf-canvas-content');
        const gridEl = canvasHostEl.querySelector('.veloxdev-wf-grid');
        const axisXEl = canvasHostEl.querySelector('.veloxdev-wf-axis-x');
        const axisYEl = canvasHostEl.querySelector('.veloxdev-wf-axis-y');
        const offsets = { x: initialOffsetX || 0, y: initialOffsetY || 0 };
        const layoutOffsets = { x: contentX || 0, y: contentY || 0 };

        canvasHostEl.style.width = (initialW || 0) + 'px';
        canvasHostEl.style.height = (initialH || 0) + 'px';
        // The content wrapper sits at the ruler band (edge offset) PLUS the layout offset (ActualOffset),
        // so world 0 aligns with the grid/axis origin (edge + ActualOffset) and nodes collapse toward it.
        // Grow-only afterward. Register the edge/layout split so setSurfaceLayout can move the whole
        // content + grid together when the layout offset changes.
        if (contentEl) {
            contentEl.style.left = (offsets.x + layoutOffsets.x) + 'px';
            contentEl.style.top = (offsets.y + layoutOffsets.y) + 'px';
        }
        surfaceLayouts[scrollerEl.id] = {
            edgeX: offsets.x, edgeY: offsets.y,
            layoutX: layoutOffsets.x, layoutY: layoutOffsets.y
        };

        // Aligns the grid pattern + world-0 axis with world coordinates. The grid is canvas-fixed
        // (world-fixed), so it scrolls naturally; it only re-positions when the edge offset or the
        // layout offset changes (rare). background-position = edge offset + layout offset.
        function applyGridPosition() {
            const gx = offsets.x + layoutOffsets.x;
            const gy = offsets.y + layoutOffsets.y;
            // Content (nodes/links) stays in lockstep with the grid/axis so world 0 is the shared origin.
            if (contentEl) { contentEl.style.left = gx + 'px'; contentEl.style.top = gy + 'px'; }
            if (gridEl) gridEl.style.backgroundPosition = gx + 'px ' + gy + 'px';
            if (axisXEl) axisXEl.style.left = gx + 'px';
            if (axisYEl) axisYEl.style.top = gy + 'px';
        }

        function growContent(axis, amount) {
            const st = surfaceLayouts[scrollerEl.id];
            if (axis === 'x') {
                offsets.x += amount;
                if (st) st.edgeX = offsets.x;
                canvasHostEl.style.width = (canvasHostEl.offsetWidth + amount) + 'px';
            } else {
                offsets.y += amount;
                if (st) st.edgeY = offsets.y;
                canvasHostEl.style.height = (canvasHostEl.offsetHeight + amount) + 'px';
            }
            // applyGridPosition repositions content + grid + axis together at edge + layout offset.
            applyGridPosition();
            report();
        }

        function expandCanvas() {
            const cw = scrollerEl.clientWidth;
            const ch = scrollerEl.clientHeight;
            // Remaining scrollable space per axis. When the user reaches the right/bottom
            // edge there is < 400px left, so grow the canvas to keep room to drag into.
            const remW = scrollerEl.scrollWidth - scrollerEl.scrollLeft - cw;
            const remH = scrollerEl.scrollHeight - scrollerEl.scrollTop - ch;
            let changed = false;
            if (remW < 400) {
                const iw = parseFloat(canvasHostEl.style.width) || canvasHostEl.offsetWidth || 0;
                canvasHostEl.style.width = (iw + 800) + 'px';
                changed = true;
            }
            if (remH < 400) {
                const ih = parseFloat(canvasHostEl.style.height) || canvasHostEl.offsetHeight || 0;
                canvasHostEl.style.height = (ih + 800) + 'px';
                changed = true;
            }
            if (changed) {
                // A canvas-size change must reach .NET so the links-layer size (SurfaceCanvas
                // context) can follow. expandCanvas can run without a follow-up scroll event (e.g.
                // minimap drag pinned at an edge), so schedule a report rather than relying on scroll.
                scheduleReport();
            }
            return changed;
        }

        function report() {
            if (dotnetRef) {
                dotnetRef.invokeMethodAsync('OnSurfaceScroll',
                    scrollerEl.scrollLeft, scrollerEl.scrollTop,
                    scrollerEl.clientWidth, scrollerEl.clientHeight,
                    parseFloat(canvasHostEl.style.width) || canvasHostEl.offsetWidth || 0,
                    parseFloat(canvasHostEl.style.height) || canvasHostEl.offsetHeight || 0,
                    offsets.x, offsets.y);
            }
        }
        surfaceReporters[scrollerEl.id] = report;

        // Syncs the JS offset state to the reserved ruler band. The content wrapper is positioned
        // at the ruler offset, so read it from the DOM and align the local offset counter. The
        // offset is grow-only afterward, so it never shrinks back when the user pans left/up.
        function ensureRulerReserve() {
            if (!contentEl) return;
            // The content now sits at edge + layout offset, so back out the layout offset to recover
            // the ruler edge — otherwise the ActualOffset would be absorbed into the grow-only edge and
            // double-counted on the next applyGridPosition.
            const rx = (contentEl.offsetLeft || 0) - layoutOffsets.x;
            const ry = (contentEl.offsetTop || 0) - layoutOffsets.y;
            if (offsets.x < rx) offsets.x = rx;
            if (offsets.y < ry) offsets.y = ry;
            applyGridPosition();
        }

        // Coalesce bursty scroll/pan/mutation events into one .NET report per frame.
        let pendingReport = null;
        function scheduleReport() {
            if (!pendingReport) {
                pendingReport = requestAnimationFrame(function () {
                    pendingReport = null;
                    report();
                });
            }
        }

        const onScroll = function () {
            if (scrollerEl.scrollLeft + scrollerEl.clientWidth >= scrollerEl.scrollWidth - 50 ||
                scrollerEl.scrollTop + scrollerEl.clientHeight >= scrollerEl.scrollHeight - 50) {
                expandCanvas();
            }
            scheduleReport();
        };

        // Applies a scroll delta with edge expansion in all four directions. Used by panning and
        // by the minimap so both can grow the canvas when the user keeps dragging past an edge.
        function scrollBy(scrollDx, scrollDy) {
            let nl = scrollerEl.scrollLeft + scrollDx;
            let nt = scrollerEl.scrollTop + scrollDy;
            if (nl < 0) { growContent('x', -nl); nl = 0; }
            else if (nl >= scrollerEl.scrollWidth - scrollerEl.clientWidth - 50) { expandCanvas(); }
            if (nt < 0) { growContent('y', -nt); nt = 0; }
            else if (nt >= scrollerEl.scrollHeight - scrollerEl.clientHeight - 50) { expandCanvas(); }
            scrollerEl.scrollLeft = nl;
            scrollerEl.scrollTop = nt;
        }
        surfaceRegistry[scrollerEl.id] = scrollBy;

        function startPan(e) {
            e.preventDefault();
            panState = {
                lastX: e.clientX, lastY: e.clientY,
                scrollLeft: scrollerEl.scrollLeft, scrollTop: scrollerEl.scrollTop
            };
            scrollerEl.style.cursor = 'grabbing';
        }

        scrollerEl.addEventListener('scroll', onScroll);

        const keydown = function (e) { if (e.code === 'Space' && !e.repeat) { spaceHeld = true; e.preventDefault(); } };
        const keyup = function (e) { if (e.code === 'Space') spaceHeld = false; };
        document.addEventListener('keydown', keydown);
        document.addEventListener('keyup', keyup);

        scrollerEl.addEventListener('mousedown', function (e) {
            if (e.button === 1) { e.preventDefault(); startPan(e); return; }
            if (e.button === 0 && spaceHeld) { e.preventDefault(); startPan(e); return; }
            if (e.button === 0 &&
                !e.target.closest('.veloxdev-wf-node-drag, .veloxdev-wf-slot, select, input, button, textarea')) {
                startPan(e);
            }
        });

        const onMove = function (e) {
            if (!panState) return;
            const dx = e.clientX - panState.lastX;
            const dy = e.clientY - panState.lastY;
            if (dx === 0 && dy === 0) return;
            panState.lastX = e.clientX;
            panState.lastY = e.clientY;

            let nl = panState.scrollLeft - dx;
            let nt = panState.scrollTop - dy;

            // Left/top edge: grow the canvas in that direction and shift the content, keeping
            // the drag continuous (the content tracks the mouse). Offset is grow-only.
            if (nl < 0) { growContent('x', -nl); nl = 0; }
            else if (nl >= scrollerEl.scrollWidth - scrollerEl.clientWidth - 50) { expandCanvas(); }
            if (nt < 0) { growContent('y', -nt); nt = 0; }
            else if (nt >= scrollerEl.scrollHeight - scrollerEl.clientHeight - 50) { expandCanvas(); }

            scrollerEl.scrollLeft = nl;
            scrollerEl.scrollTop = nt;
            panState.scrollLeft = nl;
            panState.scrollTop = nt;
        };
        const onUp = function () {
            if (panState) { scrollerEl.style.cursor = ''; panState = null; }
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
        scrollerEl.addEventListener('auxclick', function (e) { if (e.button === 1) e.preventDefault(); });

        // A zoom-settle guard must never fight a real drag/pan. Deleting the zoom state here means
        // the next settle tick observes the state gone and halts, and any stale geometry a concurrent
        // zoom left is harmless (a node drag re-renders/positions via its own path). Uses pointerdown
        // so every pointer-derived gesture (mouse, pen, touch) pre-empts the guard identically.
        scrollerEl.addEventListener('pointerdown', onUserPointerDown);
        function onUserPointerDown() {
            delete surfaceZoomState[scrollerEl.id];
        }

        // Reserve the ruler band before the initial report so the viewport offset is correct
        // from the first frame and the ruler ticks align with the grid.
        ensureRulerReserve();

        // Initial report (fills viewport size before the user interacts).
        report();

        // Host-size grower for the workspace-zoom path: grows the canvas host to fit the auto-extended
        // model extent and reports the new size so .NET's canvas bookkeeping stays in sync.
        surfaceSizers[scrollerEl.id] = function (width, height) {
            const w = Math.max(width || 0, canvasHostEl.offsetWidth || 0);
            const h = Math.max(height || 0, canvasHostEl.offsetHeight || 0);
            if (w !== (canvasHostEl.offsetWidth || 0) || h !== (canvasHostEl.offsetHeight || 0)) {
                canvasHostEl.style.width = w + 'px';
                canvasHostEl.style.height = h + 'px';
                report();
                return true;
            }
            return false;
        };

        // The host is sized from the model extent (edge + ActualSize), which can be SMALLER than the
        // scroller when the window is large — leaving an unreachable blank strip at the right/bottom
        // (scrollWidth is forced to == clientWidth when host < client, so no scroll can fill it; only
        // an edge-drag expandCanvas grows it). Grow the host right/bottom so the canvas (grid) covers
        // the whole visible area, once at init and again whenever the scroller resizes (window resize,
        // panel expand/collapse). Right/bottom-only: the world origin (edge + layout offset) never
        // moves, so nodes/links stay put while the newly revealed area appears to the right/bottom.
        function ensureFillsViewport() {
            const gw = Math.max(canvasHostEl.offsetWidth || 0, scrollerEl.clientWidth || 0);
            const gh = Math.max(canvasHostEl.offsetHeight || 0, scrollerEl.clientHeight || 0);
            if (gw !== (canvasHostEl.offsetWidth || 0) || gh !== (canvasHostEl.offsetHeight || 0)) {
                canvasHostEl.style.width = gw + 'px';
                canvasHostEl.style.height = gh + 'px';
                scheduleReport();
            }
        }
        ensureFillsViewport();
        let roFill = null;
        if (window.ResizeObserver) {
            roFill = new ResizeObserver(ensureFillsViewport);
            roFill.observe(scrollerEl);
        }

        return {
            dispose: function () {
                delete surfaceRegistry[scrollerEl.id];
                delete surfaceSizers[scrollerEl.id];
                delete surfaceLayouts[scrollerEl.id];
                delete surfaceReporters[scrollerEl.id];
                delete surfaceNodeWrappers[scrollerEl.id];
                delete surfaceZoomState[scrollerEl.id];
                delete surfaceSettleRunning[scrollerEl.id];
                scrollerEl.removeEventListener('scroll', onScroll);
                document.removeEventListener('keydown', keydown);
                document.removeEventListener('keyup', keyup);
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
                scrollerEl.removeEventListener('pointerdown', onUserPointerDown);
                if (pendingReport) cancelAnimationFrame(pendingReport);
                if (roFill) roFill.disconnect();
                scrollerEl.style.cursor = '';
            }
        };
    }

    function getViewportSize(scrollerEl) {
        if (scrollerEl) return { w: scrollerEl.clientWidth, h: scrollerEl.clientHeight };
        return { w: 800, h: 600 };
    }

    function scrollToRatio(scrollerId, ratioX, ratioY) {
        const el = document.getElementById(scrollerId);
        if (!el) return;
        el.scrollLeft = ratioX * Math.max(0, el.scrollWidth - el.clientWidth);
        el.scrollTop = ratioY * Math.max(0, el.scrollHeight - el.clientHeight);
    }

    // Restores an absolute world scroll position (used after loading a workflow whose viewport
    // was persisted in CanvasLayout.ViewportOffset).
    function scrollToPosition(scrollerId, x, y) {
        const el = document.getElementById(scrollerId);
        if (!el) return;
        el.scrollLeft = Math.max(0, x);
        el.scrollTop = Math.max(0, y);
    }

    // ════════════════════════════════════════════════════════════
    // NODE DRAG — reports per-move world deltas to .NET.
    // ════════════════════════════════════════════════════════════
    function initNodeDrag(nodeEl, dotnetRef) {
        if (!nodeEl) return null;
        let dragState = null;
        let pendingDelta = null;

        nodeEl.addEventListener('mousedown', function (e) {
            if (e.button !== 0) return;
            if (e.target.closest('select, option, input, button, textarea, .veloxdev-wf-slot')) return;
            // preventDefault stops the browser's default text/element selection from starting on
            // this mousedown, so dragging a node never selects surrounding content.
            e.preventDefault();
            e.stopPropagation();
            dragState = { startX: e.clientX, startY: e.clientY, lastX: e.clientX, lastY: e.clientY };
        });

        const flushDelta = function () {
            if (!pendingDelta || !dotnetRef) return;
            const d = pendingDelta;
            pendingDelta = null;
            dotnetRef.invokeMethodAsync('OnNodeDrag', d.dx, d.dy);
            // Let the enclosing slot-layout behavior re-measure slots live while dragging,
            // so links follow the node in real time (not just after the drop).
            nodeEl.dispatchEvent(new CustomEvent('veloxdev-node-drag-move', { bubbles: true }));
        };

        const onMove = function (e) {
            if (!dragState) return;
            e.preventDefault();
            const dx = e.clientX - dragState.lastX;
            const dy = e.clientY - dragState.lastY;
            dragState.lastX = e.clientX;
            dragState.lastY = e.clientY;
            if (dx === 0 && dy === 0) return;

            // Position the element immediately (compositor-friendly); read the live style each move
            // so an external re-render mid-drag cannot desync the tracked position. Tell .NET at
            // most once per frame (accumulated delta).
            const curLeft = parseFloat(nodeEl.style.left) || nodeEl.offsetLeft || 0;
            const curTop = parseFloat(nodeEl.style.top) || nodeEl.offsetTop || 0;
            nodeEl.style.left = (curLeft + dx) + 'px';
            nodeEl.style.top = (curTop + dy) + 'px';

            if (!pendingDelta) {
                pendingDelta = { dx: 0, dy: 0 };
                requestAnimationFrame(flushDelta);
            }
            pendingDelta.dx += dx;
            pendingDelta.dy += dy;
        };
        const onUp = function () {
            if (!dragState) return;
            dragState = null;
            // Flush any not-yet-sent delta before OnNodeDragEnd so .NET's final anchor matches the
            // DOM position (OnNodeDragEnd then syncs the wrapper to that anchor).
            if (pendingDelta) {
                const d = pendingDelta;
                pendingDelta = null;
                if (dotnetRef) dotnetRef.invokeMethodAsync('OnNodeDrag', d.dx, d.dy);
                nodeEl.dispatchEvent(new CustomEvent('veloxdev-node-drag-move', { bubbles: true }));
            }
            if (dotnetRef) dotnetRef.invokeMethodAsync('OnNodeDragEnd');
            // Let the enclosing slot-layout behavior re-measure this node's slots now that they moved.
            nodeEl.dispatchEvent(new CustomEvent('veloxdev-node-drag-end', { bubbles: true }));
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);

        return {
            dispose: function () {
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
                pendingDelta = null;
            }
        };
    }

    // ════════════════════════════════════════════════════════════
    // SLOT CONNECTION — drag from a slot to another slot.
    // Reports world coordinates to .NET, which drives the core
    // SendConnection / SetPointer / ReceiveConnection commands.
    // ════════════════════════════════════════════════════════════
    function initSlotConnection(slotEl, dotnetRef) {
        if (!slotEl) return null;
        let active = false;

        slotEl.addEventListener('mousedown', function (e) {
            if (e.button !== 0) return;
            e.preventDefault();
            e.stopPropagation();
            active = true;
            // Measure this slot's own world position up-front so the virtual link starts from the
            // slot even if its anchor was never laid out (e.g. freshly created selector slots).
            const canvasEl = slotEl.closest('.veloxdev-wf-canvas');
            let worldX = 0, worldY = 0;
            if (canvasEl) {
                const rect = canvasEl.getBoundingClientRect();
                const contentEl = canvasEl.querySelector('.veloxdev-wf-canvas-content');
                const r = slotEl.getBoundingClientRect();
                worldX = (r.left + r.width / 2) - rect.left - (contentEl ? contentEl.offsetLeft : 0);
                worldY = (r.top + r.height / 2) - rect.top - (contentEl ? contentEl.offsetTop : 0);
            }
            if (dotnetRef) dotnetRef.invokeMethodAsync('OnSlotConnectionStart', worldX, worldY);
        });

        const onMove = function (e) {
            if (!active || !dotnetRef) return;
            const canvasEl = slotEl.closest('.veloxdev-wf-canvas');
            if (!canvasEl) return;
            const rect = canvasEl.getBoundingClientRect();
            // World coordinates relative to the canvas origin. getBoundingClientRect already
            // accounts for the scroller offset; subtract the content-wrapper translate so the
            // virtual link's receiver matches node/slot anchors.
            const contentEl = canvasEl.querySelector('.veloxdev-wf-canvas-content');
            const contentX = contentEl ? contentEl.offsetLeft : 0;
            const contentY = contentEl ? contentEl.offsetTop : 0;
            const worldX = (e.clientX - rect.left) - contentX;
            const worldY = (e.clientY - rect.top) - contentY;
            dotnetRef.invokeMethodAsync('OnSlotConnectionMove', worldX, worldY);
        };

        const resolveSlotAt = function (e) {
            let target = document.elementFromPoint(e.clientX, e.clientY);
            let cur = target;
            while (cur && cur !== document.body) {
                if (cur.getAttribute && cur.getAttribute('data-veloxdev-slot-id')) return cur;
                cur = cur.parentElement;
            }
            return null;
        };

        const onUp = function (e) {
            if (!active) return;
            active = false;
            if (!dotnetRef) return;
            const targetEl = resolveSlotAt(e);
            const targetId = targetEl ? targetEl.getAttribute('data-veloxdev-slot-id') : null;
            dotnetRef.invokeMethodAsync('OnSlotConnectionEnd', targetId);
        };

        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);

        return {
            dispose: function () {
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
            }
        };
    }

    // ════════════════════════════════════════════════════════════
    // SLOT LAYOUT — measures slot element centers inside a host and
    // reports them as canvas (world) coordinates so .NET can write
    // each slot's Anchor.
    // ════════════════════════════════════════════════════════════
    function initSlotLayout(hostEl, dotnetRef) {
        if (!hostEl) return null;
        let lastKey = '';

        function measure() {
            if (!dotnetRef) return;
            const canvasEl = hostEl.closest('.veloxdev-wf-canvas');
            if (!canvasEl) return;
            const canvasRect = canvasEl.getBoundingClientRect();
            const contentEl = canvasEl.querySelector('.veloxdev-wf-canvas-content');
            const contentX = contentEl ? contentEl.offsetLeft : 0;
            const contentY = contentEl ? contentEl.offsetTop : 0;
            const batch = [];
            // Slot wrappers may be double-stamped (a generic host wrapping a SlotView that already
            // opens the connection behavior). Measure every stamped box but collapse to the LAST
            // (innermost) one per slot id — one anchor write per slot, no duplicate link redraws.
            const seen = Object.create(null);
            hostEl.querySelectorAll('[data-veloxdev-slot-id]').forEach(function (el) {
                const id = el.getAttribute('data-veloxdev-slot-id');
                if (!id) return;
                const r = glyphRect(el);
                if (!r || (r.width <= 0 && r.height <= 0)) return;
                // World coordinates relative to the canvas origin; getBoundingClientRect already
                // accounts for the scroller offset, and we subtract the content translate so slot
                // anchors match node anchors. Sent as strings so the .NET string[][] parameter
                // marshals without an exception.
                const cx = ((r.left + r.width / 2) - canvasRect.left - contentX).toFixed(2);
                const cy = ((r.top + r.height / 2) - canvasRect.top - contentY).toFixed(2);
                seen[id] = [id, cx, cy];
            });
            for (const id in seen) batch.push(seen[id]);
            if (!batch.length) return;
            const key = batch.map(b => b[0] + ':' + b[1] + ',' + b[2]).join(';');
            if (key === lastKey) return;
            lastKey = key;
            dotnetRef.invokeMethodAsync('OnSlotLayoutBatch', batch);
        }

        measure();
        window.addEventListener('resize', measure);
        let ro = null;
        if (window.ResizeObserver) {
            ro = new ResizeObserver(measure);
            ro.observe(hostEl);
        }
        // Re-measure live while this node is dragged and once more when the drop ends. The drag
        // behavior dispatches bubbling veloxdev-node-drag-move/-end events on its wrapper which
        // reach this host, so a drag only re-measures the dragged node's own slots, keeping the
        // cost flat even at 1000 nodes.
        // The move loop keeps re-measuring each frame until the slot positions stabilize, so the
        // slots (and links drawn from them) converge on the node's final position even while the
        // .NET renderer is still applying move deltas.
        let liveRaf = 0;
        let liveMeasuring = false;
        const onDragMove = function () {
            if (liveMeasuring) return;
            liveMeasuring = true;
            const tick = function () {
                if (!liveMeasuring) return;
                const before = lastKey;
                measure();
                if (lastKey === before) {
                    liveMeasuring = false;   // converged; stop until the next move
                    return;
                }
                liveRaf = requestAnimationFrame(tick);
            };
            liveRaf = requestAnimationFrame(tick);
        };
        const onDragEnd = function () {
            liveMeasuring = false;
            if (liveRaf) cancelAnimationFrame(liveRaf);
            measure();
        };
        hostEl.addEventListener('veloxdev-node-drag-move', onDragMove);
        hostEl.addEventListener('veloxdev-node-drag-end', onDragEnd);

        // Re-measure when slot elements are added/removed (e.g. a selector switching its output
        // slots). The filter ignores ordinary text/attribute churn, so this stays cheap.
        let mo = null;
        if (window.MutationObserver) {
            mo = new MutationObserver(function (mutations) {
                for (const m of mutations) {
                    if (m.type === 'childList') {
                        const hasSlotChange = [...m.addedNodes, ...m.removedNodes].some(
                            n => n.nodeType === 1 && (n.querySelector?.('[data-veloxdev-slot-id]') || n.matches?.('[data-veloxdev-slot-id]')));
                        if (hasSlotChange) { measure(); return; }
                    }
                    // Node wrapper style change: the drag behavior repositions (style.left/top) and
                    // re-sizes (width/height) the node on workspace zoom, so re-measure AFTER the DOM
                    // actually changes. This is deterministic — the rAF-based relayout event above can
                    // run before the repositions apply, so measure() would read stale positions and be
                    // deduped by lastKey, leaving the slot anchors (and links) stuck on the old values.
                    else if (m.type === 'attributes' && m.attributeName === 'style') {
                        measure(); return;
                    }
                }
            });
            mo.observe(hostEl, { childList: true, subtree: true, attributes: true, attributeFilter: ['style'] });
        }

        // Re-measure when the workspace zooms (or any non-drag layout pass moves the node): the wheel
        // zoom handler dispatches veloxdev-wf-layout-changed after Layout.Scale collapses every node
        // toward the origin, so slot anchors (and the links drawn from them) track the collapsed nodes
        // immediately instead of waiting for the next drag/resize. The drag events above already cover
        // the drag path; measure() is idempotent (lastKey dedupe) so extra triggers are cheap.
        const onRelayout = function () { measure(); };
        document.addEventListener('veloxdev-wf-layout-changed', onRelayout);

        return {
            dispose: function () {
                window.removeEventListener('resize', measure);
                document.removeEventListener('veloxdev-wf-layout-changed', onRelayout);
                if (ro) ro.disconnect();
                if (mo) mo.disconnect();
                liveMeasuring = false;
                if (liveRaf) cancelAnimationFrame(liveRaf);
                hostEl.removeEventListener('veloxdev-node-drag-move', onDragMove);
                hostEl.removeEventListener('veloxdev-node-drag-end', onDragEnd);
            }
        };
    }

    // ════════════════════════════════════════════════════════════
    // WHEEL ZOOM — Ctrl + wheel collapses/expands nodes toward the world origin
    // (mirrors the WPF surface Ctrl+wheel zoom). Non-passive so the browser's default
    // scroll is suppressed while Ctrl is held; plain wheel still scrolls.
    // ════════════════════════════════════════════════════════════
    function initWheelZoom(scrollerEl, dotnetRef) {
        if (!scrollerEl || !dotnetRef) return null;
        // Coalescing queue: at most ONE .NET zoom step is in flight at a time, and wheel events that
        // arrive while one is processing (a human fast-flick sends several before a SignalR round-trip
        // returns) accumulate into a pending net delta applied right after. Without this the notches
        // would run concurrently on the server — each re-renders every node's collapsed geometry as a
        // separate async pass, so they can paint OUT OF ORDER across the burst (a frame where a node
        // sits at an older scale under an already-newer scroll = the flash). Serializing + coalescing
        // keeps node geometry monotonic: the DOM only ever advances one settled zoom step at a time.
        let busy = false;
        let pending = 0;
        async function pump() {
            if (busy) return;
            const delta = pending;
            if (delta === 0) return;
            busy = true;
            pending = 0;
            try {
                // Send the NET wheel-delta (count of notches) so a burst that outpaces one SignalR
                // round-trip is applied server-side as that many compounding steps in ONE serialized
                // call — the DOM only ever advances to the final settled state, never one stale step.
                //
                // Capture geometry from the LIVE DOM at burst start, not from the server's last
                // REPORTED scroll (that lags a report round-trip): if a second burst fires before the
                // report lands the server would zoom about a stale scroll, baking an off-center offset
                // in as the new ground truth — the deep-zoom non-recovering drift. eff = DOM scroll −
                // the edge reserve JS owns; reach = the current host content (scrollWidth − edge), so
                // an edge-pan-expanded host is never clamped shorter than what is already reachable.
                const st = surfaceLayouts[scrollerEl.id];
                const edgeX = st ? (st.edgeX || 0) : 0;
                const edgeY = st ? (st.edgeY || 0) : 0;
                await dotnetRef.invokeMethodAsync('OnWheelZoom', delta,
                    scrollerEl.scrollLeft - edgeX,
                    scrollerEl.scrollTop - edgeY,
                    scrollerEl.clientWidth,
                    scrollerEl.clientHeight,
                    scrollerEl.scrollWidth - edgeX,
                    scrollerEl.scrollHeight - edgeY);
            } finally {
                busy = false;
                // Drain whatever accumulated while this step was in flight (each wheel already
                // prevented default, so coalescing loses nothing — only the number of server trips).
                if (pending !== 0) pump();
            }
            // Layout.Scale change collapses every node toward the origin (Anchor/Size getters divide by
            // scale): each node wrapper repositions via setNodePosition and re-renders its size. Wait a
            // frame for those to apply, then re-measure so the slot anchors (and the links drawn from
            // them) track the collapsed nodes immediately instead of on the next drag/resize.
            requestAnimationFrame(function () {
                document.dispatchEvent(new CustomEvent('veloxdev-wf-layout-changed'));
            });
        }
        function onWheel(e) {
            if (!e.ctrlKey) return;
            e.preventDefault();
            e.stopPropagation();
            pending += e.deltaY > 0 ? -120 : 120;
            pump();
        }
        scrollerEl.addEventListener('wheel', onWheel, { passive: false });
        return {
            dispose: function () {
                scrollerEl.removeEventListener('wheel', onWheel);
            }
        };
    }

    // ════════════════════════════════════════════════════════════
    // MINIMAP — always-center navigation (matching the Jalium adapter).
    // Every press maps the minimap point to a world target and centers the viewport on it;
    // dragging keeps the viewport center tracking the cursor. The block itself is moved
    // directly on scroll.
    // ════════════════════════════════════════════════════════════
    function initMinimap(minimapEl, scrollerId, dotnetRef) {
        if (!minimapEl || !scrollerId) return null;
        let dragState = null;

        // Register the viewport-block <rect> so the surface can move it directly on scroll (no .NET
        // re-render). Its dimensions come from the SVG width/height attributes (= the minimap size).
        const vpRect = minimapEl.querySelector('.veloxdev-wf-minimap-viewport');
        if (vpRect) {
            const svg = vpRect.closest('svg');
            minimapRects[scrollerId] = {
                rect: vpRect,
                width: svg ? parseFloat(svg.getAttribute('width')) || 0 : 0,
                height: svg ? parseFloat(svg.getAttribute('height')) || 0 : 0
            };
        }

        minimapEl.addEventListener('mousedown', function (e) {
            e.preventDefault();
            const mmRect = minimapEl.getBoundingClientRect();
            const mx = e.clientX - mmRect.left;
            const my = e.clientY - mmRect.top;
            dragState = { startX: e.clientX, startY: e.clientY, hasMoved: false };
            const scroller = document.getElementById(scrollerId);
            const m = scroller ? minimapMappings[scrollerId] : null;
            const last = scroller ? minimapLastWorld[scrollerId] : null;
            if (!scroller || !m || !(m.scale > 0) || !last) return;

            const vw = scroller.clientWidth, vh = scroller.clientHeight;
            // The surface pushes the true viewport world-left every scroll; it yields the
            // edge + layout offset, exact even when the block is clamped at the minimap edge.
            const offX = scroller.scrollLeft - last.x;
            const offY = scroller.scrollTop - last.y;

            // Match the Jalium adapter: the clicked point always becomes the viewport center —
            // no grab-anchor on the indicator block, so pressing anywhere recenters the view.
            const cx = mx, cy = my;

            // Center the viewport on the block center's world point, then position the block
            // synchronously so it never lags a frame behind the scroll.
            const worldX = m.minX + (cx - m.ox) / m.scale;
            const worldY = m.minY + (cy - m.oy) / m.scale;
            const targetScrollX = worldX - vw / 2 + offX;
            const targetScrollY = worldY - vh / 2 + offY;
            scrollByDelta(scrollerId, targetScrollX - scroller.scrollLeft, targetScrollY - scroller.scrollTop);
            setMinimapViewport(scrollerId, worldX - vw / 2, worldY - vh / 2, vw, vh);
        });

        const onMove = function (e) {
            if (!dragState) return;
            const dx = e.clientX - dragState.startX;
            const dy = e.clientY - dragState.startY;
            if (Math.abs(dx) > 3 || Math.abs(dy) > 3) {
                dragState.hasMoved = true;
                const scroller = document.getElementById(scrollerId);
                const scrollBy = surfaceRegistry[scrollerId];
                if (scroller) {
                    const m = minimapMappings[scrollerId];
                    const last = minimapLastWorld[scrollerId];
                    const scrollBeforeX = scroller.scrollLeft;
                    const scrollBeforeY = scroller.scrollTop;
                    if (m) {
                        // World-space delta: the minimap is a uniform scale of world coordinates,
                        // so the viewport rect tracks the cursor when we scroll by dx/scale. This
                        // stays correct after the canvas is edge-extended (unlike a ratio over the
                        // raw scroll extent, which grows with the canvas and overshoots).
                        scrollBy(dx / m.scale, dy / m.scale);
                        // Move the block synchronously from the ACTUAL scroll change (exact even
                        // when an edge expansion clamps the pan) so it never lags behind the drag.
                        if (last) {
                            setMinimapViewport(scrollerId,
                                scroller.scrollLeft - (scrollBeforeX - last.x),
                                scroller.scrollTop - (scrollBeforeY - last.y),
                                scroller.clientWidth, scroller.clientHeight);
                        }
                    } else {
                        // No mapping pushed yet: fall back to a linear ratio over the scroll extent.
                        const rect = minimapEl.getBoundingClientRect();
                        const maxSX = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
                        const maxSY = Math.max(0, scroller.scrollHeight - scroller.clientHeight);
                        if (scrollBy) {
                            // Route through the surface so dragging past a minimap edge expands the
                            // canvas in that direction instead of just hitting a hard stop.
                            scrollBy((dx / rect.width) * maxSX, (dy / rect.height) * maxSY);
                        } else {
                            // No surface registered (standalone minimap): fall back to direct scroll.
                            scroller.scrollLeft += (dx / rect.width) * maxSX;
                            scroller.scrollTop += (dy / rect.height) * maxSY;
                        }
                    }
                }
                dragState.startX = e.clientX;
                dragState.startY = e.clientY;
            }
        };
        const onUp = function () {
            // Navigation already happened on mousedown; nothing to do here.
            dragState = null;
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);

        // Window resize changes the visible-area size without a scroll; re-push the viewport block
        // with the new scroller client size so the minimap reflects the current editor state without
        // requiring a click (the world top-left is unchanged — only the visible size shrinks).
        const scrollerEl = document.getElementById(scrollerId);
        let resizeObserver = null;
        if (scrollerEl && typeof ResizeObserver !== 'undefined') {
            const onResize = function () {
                const last = minimapLastWorld[scrollerId];
                if (last) {
                    setMinimapViewport(scrollerId, last.x, last.y, scrollerEl.clientWidth, scrollerEl.clientHeight);
                }
            };
            resizeObserver = new ResizeObserver(onResize);
            resizeObserver.observe(scrollerEl);
        }

        return {
            dispose: function () {
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
                if (resizeObserver) resizeObserver.disconnect();
                if (minimapRects[scrollerId]) delete minimapRects[scrollerId];
            }
        };
    }

    return {
        getCanvasTranslate,
        initSurface,
        getViewportSize,
        scrollToRatio,
        scrollToPosition,
        setMinimapMapping,
        setNodePosition,
        setMinimapViewport,
        refreshMinimapViewport,
        scrollByDelta,
        setSurfaceLayout,
        ensureCanvasSize,
        applyZoomSurface,
        initNodeDrag,
        initSlotConnection,
        initSlotLayout,
        initMinimap,
        initWheelZoom
    };
})();

// Named exports so Blazor components can call these via the imported module
// namespace (module.InvokeAsync("initNodeDrag", ...)).
export const getCanvasTranslate = window.veloxdevWorkflow.getCanvasTranslate;
export const initSurface = window.veloxdevWorkflow.initSurface;
export const getViewportSize = window.veloxdevWorkflow.getViewportSize;
export const scrollToRatio = window.veloxdevWorkflow.scrollToRatio;
export const scrollToPosition = window.veloxdevWorkflow.scrollToPosition;
export const setMinimapMapping = window.veloxdevWorkflow.setMinimapMapping;
export const setNodePosition = window.veloxdevWorkflow.setNodePosition;
export const setMinimapViewport = window.veloxdevWorkflow.setMinimapViewport;
export const refreshMinimapViewport = window.veloxdevWorkflow.refreshMinimapViewport;
export const scrollByDelta = window.veloxdevWorkflow.scrollByDelta;
export const setSurfaceLayout = window.veloxdevWorkflow.setSurfaceLayout;
export const ensureCanvasSize = window.veloxdevWorkflow.ensureCanvasSize;
export const applyZoomSurface = window.veloxdevWorkflow.applyZoomSurface;
export const initNodeDrag = window.veloxdevWorkflow.initNodeDrag;
export const initSlotConnection = window.veloxdevWorkflow.initSlotConnection;
export const initSlotLayout = window.veloxdevWorkflow.initSlotLayout;
export const initMinimap = window.veloxdevWorkflow.initMinimap;
export const initWheelZoom = window.veloxdevWorkflow.initWheelZoom;
