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

        // Reserve the ruler band before the initial report so the viewport offset is correct
        // from the first frame and the ruler ticks align with the grid.
        ensureRulerReserve();

        // Initial report (fills viewport size before the user interacts).
        report();

        return {
            dispose: function () {
                delete surfaceRegistry[scrollerEl.id];
                delete surfaceLayouts[scrollerEl.id];
                scrollerEl.removeEventListener('scroll', onScroll);
                document.removeEventListener('keydown', keydown);
                document.removeEventListener('keyup', keyup);
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
                if (pendingReport) cancelAnimationFrame(pendingReport);
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
            hostEl.querySelectorAll('[data-veloxdev-slot-id]').forEach(function (el) {
                const id = el.getAttribute('data-veloxdev-slot-id');
                if (!id) return;
                const r = el.getBoundingClientRect();
                if (r.width <= 0 && r.height <= 0) return;
                // World coordinates relative to the canvas origin; getBoundingClientRect already
                // accounts for the scroller offset, and we subtract the content translate so slot
                // anchors match node anchors. Sent as strings so the .NET string[][] parameter
                // marshals without an exception.
                const cx = ((r.left + r.width / 2) - canvasRect.left - contentX).toFixed(2);
                const cy = ((r.top + r.height / 2) - canvasRect.top - contentY).toFixed(2);
                batch.push([id, cx, cy]);
            });
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
        async function onWheel(e) {
            if (!e.ctrlKey) return;
            e.preventDefault();
            e.stopPropagation();
            await dotnetRef.invokeMethodAsync('OnWheelZoom', e.deltaY > 0 ? -1 : 1);
            // Layout.Scale change collapses every node toward the origin (Anchor/Size getters divide by
            // scale): each node wrapper repositions via setNodePosition and re-renders its size. Wait a
            // frame for those to apply, then re-measure so the slot anchors (and the links drawn from
            // them) track the collapsed nodes immediately instead of on the next drag/resize.
            requestAnimationFrame(function () {
                document.dispatchEvent(new CustomEvent('veloxdev-wf-layout-changed'));
            });
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
export const initNodeDrag = window.veloxdevWorkflow.initNodeDrag;
export const initSlotConnection = window.veloxdevWorkflow.initSlotConnection;
export const initSlotLayout = window.veloxdevWorkflow.initSlotLayout;
export const initMinimap = window.veloxdevWorkflow.initMinimap;
export const initWheelZoom = window.veloxdevWorkflow.initWheelZoom;
