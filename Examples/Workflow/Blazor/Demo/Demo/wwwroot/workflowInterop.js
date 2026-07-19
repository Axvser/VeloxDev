// VeloxDev Workflow Blazor Demo — Interaction helpers
// Node dragging, canvas pan, scroll tracking, minimap, live link updates.

window.workflowInterop = {
    // ════════════════════════════════════════════════════════════
    // NODE DRAGGING — updates SVG links in real time
    // ════════════════════════════════════════════════════════════
    _dragState: null,
    _buildLinkPoints: null, // set by dotnet

    initNodeDrag: function (canvasElement, dotnetRef) {
        const self = this;
        canvasElement.addEventListener('mousedown', function (e) {
            if (e.button !== 0) return; // left button only
            const nodeEl = e.target.closest('.wf-node');
            if (!nodeEl || nodeEl.closest('select, option, input, button, .ctrl-footer, .btn-ctrl')) return;
            const idx = nodeEl.getAttribute('data-node-index');
            if (idx === null) return;
            e.stopPropagation(); // prevent canvas pan from also starting
            self._startNodeDrag(nodeEl, dotnetRef, parseInt(idx), e);
        });
    },

    _startNodeDrag: function (element, dotnetRef, nodeIndex, e) {
        const self = this;
        const startX = e.clientX;
        const startY = e.clientY;
        const origLeft = parseFloat(element.style.left) || 0;
        const origTop = parseFloat(element.style.top) || 0;
        const scrollEl = element.closest('.wf-deco-content');
        const scrollLeft = scrollEl ? scrollEl.scrollLeft : 0;
        const scrollTop = scrollEl ? scrollEl.scrollTop : 0;

        // Find all SVG polylines to update during drag
        const svg = element.closest('.wf-canvas')?.querySelector('.wf-links-svg');
        const polylines = svg ? Array.from(svg.querySelectorAll('.wf-link-line')) : [];
        // Get all positioned nodes for link endpoint calculation
        const allNodes = element.closest('.wf-canvas')?.querySelectorAll('.wf-node') || [];

        function updateLinks() {
            // After node is moved, all link endpoints change — simplest is to
            // rebuild all polyline points by reading current node positions.
            // We call back to .NET to get fresh link data.
            dotnetRef.invokeMethodAsync('OnGetLinkPoints')
                .then(function (ptsArray) {
                    if (!ptsArray) return;
                    for (let i = 0; i < ptsArray.length && i < polylines.length; i++) {
                        polylines[i].setAttribute('points', ptsArray[i]);
                    }
                })
                .catch(function () {});
        }

        this._dragState = {
            startX, startY, origLeft, origTop,
            currentX: origLeft, currentY: origTop,
            dotnetRef, nodeIndex, element,
            scrollLeft, scrollTop, updateLinks
        };

        const onMove = function (me) {
            me.preventDefault();
            const dx = (me.clientX || 0) - self._dragState.startX;
            const dy = (me.clientY || 0) - self._dragState.startY;
            const newLeft = self._dragState.origLeft + dx;
            const newTop = self._dragState.origTop + dy;
            element.style.left = newLeft + 'px';
            element.style.top = newTop + 'px';
            self._dragState.currentX = newLeft;
            self._dragState.currentY = newTop;
            // Throttled link update every 50ms
            if (!self._dragState._pendingLinkUpdate) {
                self._dragState._pendingLinkUpdate = true;
                requestAnimationFrame(function () {
                    self._dragState._pendingLinkUpdate = false;
                    updateLinks();
                });
            }
        };
        const onUp = function () {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            if (self._dragState) {
                dotnetRef.invokeMethodAsync('OnNodeDragEnd', nodeIndex,
                    self._dragState.currentX, self._dragState.currentY);
                self._dragState = null;
            }
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    },

    // ════════════════════════════════════════════════════════════
    // CANVAS PAN — middle mouse, space+left, or left drag on empty canvas
    // also auto-expands the canvas container when reaching edges
    // ════════════════════════════════════════════════════════════
    initCanvasPan: function (wrapperElement, dotnetRef) {
        const scrollEl = wrapperElement.querySelector('.wf-deco-content');
        if (!scrollEl) return;

        let panState = null;
        let spaceHeld = false;

        function expandCanvas() {
            const innerEl = scrollEl.querySelector('.wf-canvas');
            if (!innerEl) return;
            const iw = parseFloat(innerEl.style.width) || 0;
            const ih = parseFloat(innerEl.style.height) || 0;
            const sw = scrollEl.scrollWidth;
            const sh = scrollEl.scrollHeight;
            const cw = scrollEl.clientWidth;
            const ch = scrollEl.clientHeight;
            // If content is smaller than viewport, expand it
            if (sw < cw + 400) {
                const newW = Math.max(iw, cw + 800);
                innerEl.style.width = newW + 'px';
                innerEl.querySelector('.wf-links-svg')?.setAttribute('width', String(newW));
            }
            if (sh < ch + 400) {
                const newH = Math.max(ih, ch + 800);
                innerEl.style.height = newH + 'px';
                innerEl.querySelector('.wf-links-svg')?.setAttribute('height', String(newH));
            }
        }

        document.addEventListener('keydown', function (e) { if (e.code === 'Space' && !e.repeat) { spaceHeld = true; e.preventDefault(); } });
        document.addEventListener('keyup', function (e) { if (e.code === 'Space') { spaceHeld = false; } });

        scrollEl.addEventListener('mousedown', function (e) {
            if (e.button === 1) { e.preventDefault(); startPan(e); return; }
            if (e.button === 0 && spaceHeld) { e.preventDefault(); startPan(e); return; }
            if (e.button === 0 && !e.target.closest('.wf-node, select, input, button, textarea')) {
                startPan(e);
            }
        });

        function startPan(e) {
            e.preventDefault();
            panState = { startX: e.clientX, startY: e.clientY, scrollLeft: scrollEl.scrollLeft, scrollTop: scrollEl.scrollTop };
            scrollEl.style.cursor = 'grabbing';
        }

        document.addEventListener('mousemove', function (e) {
            if (!panState) return;
            const dx = e.clientX - panState.startX;
            const dy = e.clientY - panState.startY;
            const newLeft = panState.scrollLeft - dx;
            const newTop = panState.scrollTop - dy;
            // Auto-expand if hitting boundaries
            if (newLeft <= 0 || newLeft >= scrollEl.scrollWidth - scrollEl.clientWidth - 50) expandCanvas();
            if (newTop <= 0 || newTop >= scrollEl.scrollHeight - scrollEl.clientHeight - 50) expandCanvas();
            scrollEl.scrollLeft = newLeft;
            scrollEl.scrollTop = newTop;
        });

        document.addEventListener('mouseup', function () {
            if (panState) { scrollEl.style.cursor = ''; panState = null; }
        });

        scrollEl.addEventListener('auxclick', function (e) { if (e.button === 1) e.preventDefault(); });

        // Also auto-expand when scrolling with scrollbar reaches edges
        scrollEl.addEventListener('scroll', function () {
            const s = scrollEl;
            if (s.scrollLeft + s.clientWidth >= s.scrollWidth - 50 || s.scrollTop + s.clientHeight >= s.scrollHeight - 50) {
                expandCanvas();
            }
        });
    },

    // ════════════════════════════════════════════════════════════
    // SCROLL TRACKING
    // ════════════════════════════════════════════════════════════
    initScrollTracking: function (wrapperElement, dotnetRef) {
        const scrollEl = wrapperElement.querySelector('.wf-deco-content');
        if (!scrollEl) return;
        scrollEl.addEventListener('scroll', function () {
            dotnetRef.invokeMethodAsync('OnScrollChanged', scrollEl.scrollLeft, scrollEl.scrollTop);
        });
    },

    // ════════════════════════════════════════════════════════════
    // MINIMAP — drag to scroll, click to jump
    // ════════════════════════════════════════════════════════════
    _minimapDragState: null,

    initMinimap: function (wrapperElement, dotnetRef) {
        const minimapElement = wrapperElement.querySelector('.wf-minimap');
        if (!minimapElement) return;
        const self = this;
        let dragState = null;

        minimapElement.addEventListener('mousedown', function (e) {
            e.preventDefault();
            dragState = {
                startX: e.clientX, startY: e.clientY,
                hasMoved: false, wrapperElement, minimapElement
            };
        });

        document.addEventListener('mousemove', function (e) {
            if (!dragState) return;
            const dx = e.clientX - dragState.startX;
            const dy = e.clientY - dragState.startY;
            if (Math.abs(dx) > 3 || Math.abs(dy) > 3) {
                dragState.hasMoved = true;
                const rect = dragState.minimapElement.getBoundingClientRect();
                const scrollEl = dragState.wrapperElement.querySelector('.wf-deco-content');
                if (!scrollEl) return;
                const maxSX = Math.max(0, scrollEl.scrollWidth - scrollEl.clientWidth);
                const maxSY = Math.max(0, scrollEl.scrollHeight - scrollEl.clientHeight);
                if (maxSX > 0) scrollEl.scrollLeft += (dx / rect.width) * maxSX;
                if (maxSY > 0) scrollEl.scrollTop += (dy / rect.height) * maxSY;
                dragState.startX = e.clientX;
                dragState.startY = e.clientY;
            }
        });

        document.addEventListener('mouseup', function () {
            if (!dragState) return;
            if (!dragState.hasMoved) {
                // It was a click — calculate jump position
                const scrollEl = dragState.wrapperElement.querySelector('.wf-deco-content');
                if (scrollEl) {
                    const rect = dragState.minimapElement.getBoundingClientRect();
                    const scrollW = scrollEl.scrollWidth - scrollEl.clientWidth;
                    const scrollH = scrollEl.scrollHeight - scrollEl.clientHeight;
                    const relX = dragState.startX - rect.left;
                    const relY = dragState.startY - rect.top;
                    if (scrollW > 0) scrollEl.scrollLeft = (relX / rect.width) * scrollW;
                    if (scrollH > 0) scrollEl.scrollTop = (relY / rect.height) * scrollH;
                }
            }
            dragState = null;
        });
    },

    scrollTo: function (wrapperElement, x, y) {
        const scrollEl = wrapperElement.querySelector('.wf-deco-content');
        if (scrollEl) { scrollEl.scrollLeft = x; scrollEl.scrollTop = y; }
    },

    // ════════════════════════════════════════════════════════════
    // VIEWPORT SIZE
    // ════════════════════════════════════════════════════════════
    getViewportSize: function (wrapperElement) {
        const scrollEl = wrapperElement.querySelector('.wf-deco-content');
        if (scrollEl) return { w: scrollEl.clientWidth, h: scrollEl.clientHeight };
        return { w: 800, h: 600 };
    },

    // ════════════════════════════════════════════════════════════
    // RULER — create ruler overlays on top of scroll container
    // ════════════════════════════════════════════════════════════
    initRulers: function (wrapperElement, config) {
        const decorator = wrapperElement.querySelector('.wf-decorator');
        const scrollEl = decorator ? decorator.querySelector('.wf-deco-content') : null;
        if (!decorator || !scrollEl) return;

        const rt = config?.ruler ?? 28;
        const sp = config?.spacing ?? 40;
        const rbg = config?.rulerBg ?? '#252526';
        const tc = config?.tickColor ?? '#555555';
        const dc = config?.dividerColor ?? '#3A3D40';

        // Create ruler layer
        let layer = decorator.querySelector('.wf-ruler-layer');
        if (!layer) {
            layer = document.createElement('div');
            layer.className = 'wf-ruler-layer';
            decorator.appendChild(layer);
        }
        layer.innerHTML = '';

        // Corner
        const corner = document.createElement('div');
        corner.className = 'wf-ruler-corner';
        corner.style.cssText = 'width:' + rt + 'px;height:' + rt + 'px;background:' + rbg + ';border-right:1px solid ' + dc + ';border-bottom:1px solid ' + dc + ';';
        layer.appendChild(corner);

        // Top ruler
        const topRuler = document.createElement('div');
        topRuler.className = 'wf-ruler-top';
        topRuler.style.cssText = 'height:' + rt + 'px;left:' + rt + 'px;background:' + rbg + ';border-bottom:1px solid ' + dc + ';' +
            'background-image:' +
            'repeating-linear-gradient(to right, transparent ' + (sp - 1) + 'px, ' + tc + ' ' + (sp - 1) + 'px, ' + tc + ' ' + sp + 'px),' +
            'linear-gradient(to bottom, transparent calc(100% - 6px), ' + tc + ' calc(100% - 6px));';
        layer.appendChild(topRuler);

        // Left ruler
        const leftRuler = document.createElement('div');
        leftRuler.className = 'wf-ruler-left';
        leftRuler.style.cssText = 'width:' + rt + 'px;top:' + rt + 'px;background:' + rbg + ';border-right:1px solid ' + dc + ';' +
            'background-image:' +
            'repeating-linear-gradient(to bottom, transparent ' + (sp - 1) + 'px, ' + tc + ' ' + (sp - 1) + 'px, ' + tc + ' ' + sp + 'px),' +
            'linear-gradient(to right, transparent calc(100% - 6px), ' + tc + ' calc(100% - 6px));';
        layer.appendChild(leftRuler);

        function sync() {
            const sw = Math.max(scrollEl.scrollWidth, scrollEl.clientWidth);
            const sh = Math.max(scrollEl.scrollHeight, scrollEl.clientHeight);
            topRuler.style.width = Math.max(sw - rt, 0) + 'px';
            leftRuler.style.height = Math.max(sh - rt, 0) + 'px';
        }

        scrollEl.addEventListener('scroll', sync);
        window.addEventListener('resize', sync);
        if (window.ResizeObserver) {
            const ro = new ResizeObserver(sync);
            ro.observe(scrollEl);
        }
        sync();
        setTimeout(sync, 300);
    },
    updateSvgLinks: function (canvasElement, ptsArray) {
        const svg = canvasElement.querySelector('.wf-links-svg');
        if (!svg) return;
        const polylines = svg.querySelectorAll('.wf-link-line');
        for (let i = 0; i < ptsArray.length && i < polylines.length; i++) {
            polylines[i].setAttribute('points', ptsArray[i]);
        }
    }
};
