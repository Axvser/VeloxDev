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

    function setMinimapMapping(scrollerId, scale, ox, oy, minX, minY) {
        minimapMappings[scrollerId] =
            (isFinite(scale) && scale > 0) ? { scale, ox, oy, minX, minY } : null;
    }

    function initSurface(scrollerEl, canvasEl, dotnetRef) {
        if (!scrollerEl || !canvasEl) return null;
        let panState = null;
        let spaceHeld = false;

        // The canvas-content wrapper is translated by the "negative offset" so the canvas can
        // expand in all four directions. Growing right/down enlarges the canvas itself; growing
        // left/up enlarges the canvas AND shifts the content wrapper right/down by the same amount.
        const contentEl = canvasEl.querySelector('.veloxdev-wf-canvas-content');
        const offsets = { x: 0, y: 0 };

        function growContent(axis, amount) {
            if (axis === 'x') {
                offsets.x += amount;
                canvasEl.style.width = (canvasEl.offsetWidth + amount) + 'px';
                if (contentEl) contentEl.style.left = offsets.x + 'px';
            } else {
                offsets.y += amount;
                canvasEl.style.height = (canvasEl.offsetHeight + amount) + 'px';
                if (contentEl) contentEl.style.top = offsets.y + 'px';
            }
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
                const iw = parseFloat(canvasEl.style.width) || canvasEl.offsetWidth || 0;
                canvasEl.style.width = (iw + 800) + 'px';
                changed = true;
            }
            if (remH < 400) {
                const ih = parseFloat(canvasEl.style.height) || canvasEl.offsetHeight || 0;
                canvasEl.style.height = (ih + 800) + 'px';
                changed = true;
            }
            return changed;
        }

        function report() {
            if (dotnetRef) {
                dotnetRef.invokeMethodAsync('OnSurfaceScroll',
                    scrollerEl.scrollLeft, scrollerEl.scrollTop,
                    scrollerEl.clientWidth, scrollerEl.clientHeight,
                    canvasEl.offsetWidth, canvasEl.offsetHeight,
                    offsets.x, offsets.y);
            }
        }

        // Syncs the JS offset state to the reserved ruler band. The .NET surface already reserves
        // RulerThickness on the canvas width/height and positions the content wrapper at that offset
        // (matching the XAML adapters), so this only aligns the local offset counter to the DOM —
        // it must NOT grow the canvas again (that would double the reserve). The offset is grow-only
        // afterward, so it never shrinks back when the user pans left/up.
        function ensureRulerReserve() {
            if (!contentEl) return;
            const rx = contentEl.offsetLeft || 0;
            const ry = contentEl.offsetTop || 0;
            if (offsets.x < rx) offsets.x = rx;
            if (offsets.y < ry) offsets.y = ry;
        }

        const onScroll = function () {
            if (scrollerEl.scrollLeft + scrollerEl.clientWidth >= scrollerEl.scrollWidth - 50 ||
                scrollerEl.scrollTop + scrollerEl.clientHeight >= scrollerEl.scrollHeight - 50) {
                expandCanvas();
            }
            report();
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
                scrollerEl.removeEventListener('scroll', onScroll);
                document.removeEventListener('keydown', keydown);
                document.removeEventListener('keyup', keyup);
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
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

        nodeEl.addEventListener('mousedown', function (e) {
            if (e.button !== 0) return;
            if (e.target.closest('select, option, input, button, textarea, .veloxdev-wf-slot')) return;
            e.stopPropagation();
            dragState = { startX: e.clientX, startY: e.clientY, lastX: e.clientX, lastY: e.clientY };
        });

        const onMove = function (e) {
            if (!dragState) return;
            e.preventDefault();
            const dx = e.clientX - dragState.lastX;
            const dy = e.clientY - dragState.lastY;
            dragState.lastX = e.clientX;
            dragState.lastY = e.clientY;
            if (dotnetRef && (dx !== 0 || dy !== 0)) {
                dotnetRef.invokeMethodAsync('OnNodeDrag', dx, dy);
                // Let the enclosing slot-layout behavior re-measure slots live while dragging,
                // so links follow the node in real time (not just after the drop).
                nodeEl.dispatchEvent(new CustomEvent('veloxdev-node-drag-move', { bubbles: true }));
            }
        };
        const onUp = function () {
            if (!dragState) return;
            dragState = null;
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
                    if (m.type !== 'childList') continue;
                    const hasSlotChange = [...m.addedNodes, ...m.removedNodes].some(
                        n => n.nodeType === 1 && (n.querySelector?.('[data-veloxdev-slot-id]') || n.matches?.('[data-veloxdev-slot-id]')));
                    if (hasSlotChange) { measure(); return; }
                }
            });
            mo.observe(hostEl, { childList: true, subtree: true });
        }

        return {
            dispose: function () {
                window.removeEventListener('resize', measure);
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
    // MINIMAP — drag to scroll (in JS), click to jump (via .NET).
    // ════════════════════════════════════════════════════════════
    function initMinimap(minimapEl, scrollerId, dotnetRef) {
        if (!minimapEl || !scrollerId) return null;
        let dragState = null;

        minimapEl.addEventListener('mousedown', function (e) {
            e.preventDefault();
            dragState = { startX: e.clientX, startY: e.clientY, hasMoved: false };
            if (dotnetRef) {
                const rect = minimapEl.getBoundingClientRect();
                const scroller = document.getElementById(scrollerId);
                // Navigate on press, not release: when the viewport is outside the fit-all area the
                // adapter centers it on all nodes immediately, so a single press snaps back to a
                // valid area instead of requiring a second click on the cluster. The async result
                // is deliberately ignored — navigation always happens on press (to the clicked
                // point when the viewport already shows all nodes, else to the fit-all viewport).
                dotnetRef.invokeMethodAsync('OnMinimapPress',
                    e.clientX - rect.left,
                    e.clientY - rect.top,
                    scroller ? scroller.scrollLeft : 0,
                    scroller ? scroller.scrollTop : 0);
            }
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
                    if (m) {
                        // World-space delta: the minimap is a uniform scale of world coordinates,
                        // so the viewport rect tracks the cursor when we scroll by dx/scale. This
                        // stays correct after the canvas is edge-extended (unlike a ratio over the
                        // raw scroll extent, which grows with the canvas and overshoots).
                        scrollBy(dx / m.scale, dy / m.scale);
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
            // Navigation already happened on mousedown (OnMinimapPress); nothing to do here.
            dragState = null;
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

    return {
        getCanvasTranslate,
        initSurface,
        getViewportSize,
        scrollToRatio,
        scrollToPosition,
        setMinimapMapping,
        initNodeDrag,
        initSlotConnection,
        initSlotLayout,
        initMinimap
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
export const initNodeDrag = window.veloxdevWorkflow.initNodeDrag;
export const initSlotConnection = window.veloxdevWorkflow.initSlotConnection;
export const initSlotLayout = window.veloxdevWorkflow.initSlotLayout;
export const initMinimap = window.veloxdevWorkflow.initMinimap;
