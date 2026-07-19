// VeloxDev Workflow Blazor Demo — Interaction helpers
// Node dragging, scroll tracking, minimap pan.

window.workflowInterop = {
    // ── Node dragging (delegated) ──────────────────────────
    initNodeDrag: function (canvasElement, dotnetRef) {
        const self = this;
        canvasElement.addEventListener('mousedown', function (e) {
            const nodeEl = e.target.closest('.wf-node');
            if (!nodeEl || nodeEl.closest('select, option, input, button')) return; // ignore form elements
            const idx = nodeEl.getAttribute('data-node-index');
            if (idx === null) return;
            self.startDrag(nodeEl, dotnetRef, parseInt(idx));
        });
    },

    dragState: null,

    startDrag: function (element, dotnetRef, nodeIndex) {
        const self = this;
        const onMove = function (e) {
            e.preventDefault();
            const clientX = e.clientX || e.touches?.[0]?.clientX || 0;
            const clientY = e.clientY || e.touches?.[0]?.clientY || 0;
            const dx = clientX - self.dragState.startX;
            const dy = clientY - self.dragState.startY;
            const newLeft = self.dragState.origLeft + dx;
            const newTop = self.dragState.origTop + dy;
            element.style.left = newLeft + 'px';
            element.style.top = newTop + 'px';
            self.dragState.currentX = newLeft;
            self.dragState.currentY = newTop;
        };
        const onUp = function () {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            if (self.dragState) {
                dotnetRef.invokeMethodAsync('OnNodeDragEnd', nodeIndex, self.dragState.currentX, self.dragState.currentY);
            }
            self.dragState = null;
        };
        const rect = element.getBoundingClientRect();
        const clientX = e.clientX || e.touches?.[0]?.clientX || 0;
        const clientY = e.clientY || e.touches?.[0]?.clientY || 0;
        this.dragState = {
            startX: clientX,
            startY: clientY,
            origLeft: parseFloat(element.style.left) || 0,
            origTop: parseFloat(element.style.top) || 0,
            currentX: parseFloat(element.style.left) || 0,
            currentY: parseFloat(element.style.top) || 0
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    },

    // ── Scroll tracking ───────────────────────────────────
    initScrollTracking: function (wrapperElement, dotnetRef) {
        // The scrollable element is .wf-deco-content inside the decorator
        const scrollElement = wrapperElement.querySelector('.wf-deco-content');
        if (!scrollElement) return;
        scrollElement.addEventListener('scroll', function () {
            dotnetRef.invokeMethodAsync('OnScrollChanged', scrollElement.scrollLeft, scrollElement.scrollTop);
        });
    },

    // ── Minimap click-to-scroll ───────────────────────────
    initMinimapClick: function (minimapElement, wrapperElement, dotnetRef) {
        minimapElement.addEventListener('click', function (e) {
            const rect = minimapElement.getBoundingClientRect();
            const relX = e.clientX - rect.left;
            const relY = e.clientY - rect.top;
            const w = rect.width;
            const h = rect.height;
            dotnetRef.invokeMethodAsync('OnMinimapClick', relX, relY, w, h);
        });
    },

    // ── Scroll the decorator content area ─────────────────
    scrollTo: function (wrapperElement, x, y) {
        const scrollElement = wrapperElement.querySelector('.wf-deco-content');
        if (scrollElement) {
            scrollElement.scrollLeft = x;
            scrollElement.scrollTop = y;
        }
    },

    // ── Get viewport size ─────────────────────────────────
    getViewportSize: function (wrapperElement) {
        const scrollElement = wrapperElement.querySelector('.wf-deco-content');
        if (scrollElement) {
            return { w: scrollElement.clientWidth, h: scrollElement.clientHeight };
        }
        return { w: 800, h: 600 };
    },

    // ── Start node drag by finding element from canvas ────
    startNodeDrag: function (canvasElement, nodeIndex, dotnetRef, event) {
        const nodeEl = canvasElement.querySelector(`.wf-node[data-node-index="${nodeIndex}"]`);
        if (nodeEl) {
            this.startDrag(nodeEl, dotnetRef, nodeIndex);
        }
    }
};
