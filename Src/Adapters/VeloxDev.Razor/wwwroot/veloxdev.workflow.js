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
    function initSurface(scrollerEl, canvasEl, dotnetRef) {
        if (!scrollerEl || !canvasEl) return null;
        let panState = null;
        let spaceHeld = false;

        function expandCanvas() {
            const iw = parseFloat(canvasEl.style.width) || 0;
            const ih = parseFloat(canvasEl.style.height) || 0;
            const cw = scrollerEl.clientWidth;
            const ch = scrollerEl.clientHeight;
            let changed = false;
            if (scrollerEl.scrollWidth < cw + 400) {
                canvasEl.style.width = Math.max(iw, cw + 800) + 'px';
                changed = true;
            }
            if (scrollerEl.scrollHeight < ch + 400) {
                canvasEl.style.height = Math.max(ih, ch + 800) + 'px';
                changed = true;
            }
            return changed;
        }

        function report() {
            if (dotnetRef) {
                dotnetRef.invokeMethodAsync('OnSurfaceScroll',
                    scrollerEl.scrollLeft, scrollerEl.scrollTop,
                    scrollerEl.clientWidth, scrollerEl.clientHeight);
            }
        }

        const onScroll = function () {
            if (scrollerEl.scrollLeft + scrollerEl.clientWidth >= scrollerEl.scrollWidth - 50 ||
                scrollerEl.scrollTop + scrollerEl.clientHeight >= scrollerEl.scrollHeight - 50) {
                expandCanvas();
            }
            report();
        };

        function startPan(e) {
            e.preventDefault();
            panState = {
                startX: e.clientX, startY: e.clientY,
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
            const dx = e.clientX - panState.startX;
            const dy = e.clientY - panState.startY;
            const nl = panState.scrollLeft - dx;
            const nt = panState.scrollTop - dy;
            if (nl <= 0 || nl >= scrollerEl.scrollWidth - scrollerEl.clientWidth - 50) expandCanvas();
            if (nt <= 0 || nt >= scrollerEl.scrollHeight - scrollerEl.clientHeight - 50) expandCanvas();
            scrollerEl.scrollLeft = nl;
            scrollerEl.scrollTop = nt;
        };
        const onUp = function () {
            if (panState) { scrollerEl.style.cursor = ''; panState = null; }
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
        scrollerEl.addEventListener('auxclick', function (e) { if (e.button === 1) e.preventDefault(); });

        // Initial report (fills viewport size before the user interacts).
        report();

        return {
            dispose: function () {
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
            }
        };
        const onUp = function () {
            if (!dragState) return;
            dragState = null;
            if (dotnetRef) dotnetRef.invokeMethodAsync('OnNodeDragEnd');
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
            if (dotnetRef) dotnetRef.invokeMethodAsync('OnSlotConnectionStart');
        });

        const onMove = function (e) {
            if (!active || !dotnetRef) return;
            const canvasEl = slotEl.closest('.veloxdev-wf-canvas');
            const scrollerEl = slotEl.closest('.veloxdev-wf-scroll');
            if (!canvasEl || !scrollerEl) return;
            const rect = canvasEl.getBoundingClientRect();
            const tr = getCanvasTranslate(canvasEl);
            const worldX = (e.clientX - rect.left) + scrollerEl.scrollLeft - tr.x;
            const worldY = (e.clientY - rect.top) + scrollerEl.scrollTop - tr.y;
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
            const targetId = targetEl ? targetEl.getAttribute('data-slot-id') : null;
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

        function measure() {
            if (!dotnetRef) return;
            const canvasEl = hostEl.closest('.veloxdev-wf-canvas');
            const scrollerEl = hostEl.closest('.veloxdev-wf-scroll');
            if (!canvasEl) return;
            const canvasRect = canvasEl.getBoundingClientRect();
            const tr = getCanvasTranslate(canvasEl);
            const batch = [];
            hostEl.querySelectorAll('[data-veloxdev-slot-id]').forEach(function (el) {
                const id = el.getAttribute('data-veloxdev-slot-id');
                if (!id) return;
                const r = el.getBoundingClientRect();
                if (r.width <= 0 && r.height <= 0) return;
                const cx = (r.left + r.width / 2) - canvasRect.left + (scrollerEl ? scrollerEl.scrollLeft : 0) - tr.x;
                const cy = (r.top + r.height / 2) - canvasRect.top + (scrollerEl ? scrollerEl.scrollTop : 0) - tr.y;
                batch.push([id, cx, cy]);
            });
            if (batch.length) dotnetRef.invokeMethodAsync('OnSlotLayoutBatch', batch);
        }

        measure();
        window.addEventListener('resize', measure);
        let ro = null;
        if (window.ResizeObserver) {
            ro = new ResizeObserver(measure);
            ro.observe(hostEl);
        }

        return {
            dispose: function () {
                window.removeEventListener('resize', measure);
                if (ro) ro.disconnect();
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
        });

        const onMove = function (e) {
            if (!dragState) return;
            const dx = e.clientX - dragState.startX;
            const dy = e.clientY - dragState.startY;
            if (Math.abs(dx) > 3 || Math.abs(dy) > 3) {
                dragState.hasMoved = true;
                const scroller = document.getElementById(scrollerId);
                if (scroller) {
                    const rect = minimapEl.getBoundingClientRect();
                    const maxSX = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
                    const maxSY = Math.max(0, scroller.scrollHeight - scroller.clientHeight);
                    if (maxSX > 0) scroller.scrollLeft += (dx / rect.width) * maxSX;
                    if (maxSY > 0) scroller.scrollTop += (dy / rect.height) * maxSY;
                }
                dragState.startX = e.clientX;
                dragState.startY = e.clientY;
            }
        };
        const onUp = function () {
            if (!dragState) return;
            if (!dragState.hasMoved && dotnetRef) {
                const rect = minimapEl.getBoundingClientRect();
                dotnetRef.invokeMethodAsync('OnMinimapNavigate',
                    (dragState.startX - rect.left) / rect.width,
                    (dragState.startY - rect.top) / rect.height);
            }
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
export const initNodeDrag = window.veloxdevWorkflow.initNodeDrag;
export const initSlotConnection = window.veloxdevWorkflow.initSlotConnection;
export const initSlotLayout = window.veloxdevWorkflow.initSlotLayout;
export const initMinimap = window.veloxdevWorkflow.initMinimap;
