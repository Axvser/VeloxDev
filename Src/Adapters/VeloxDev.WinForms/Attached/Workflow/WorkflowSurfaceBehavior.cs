using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms does not support attached properties, but this type mirrors the workflow surface API shape used by other adapters.
/// </summary>
public sealed class WorkflowSurfaceBehavior
{
    private sealed class SurfaceState : IMessageFilter
    {
        public bool IsEnabled { get; set; }
        public bool ZoomEnabled { get; set; }
        public string? ScrollViewerName { get; set; }
        public string? CanvasName { get; set; }
        public string? GridDecoratorName { get; set; }
        public string? PointerPressSourceName { get; set; }
        public string? MinimapOverlayName { get; set; }
        public IWorkflowTreeViewModel? WorkflowTree { get; set; }

        private const int WmMouseWheel = 0x020A;
        internal Control? _filterHost;

        /// <summary>
        /// Global pre-processing for the Ctrl+wheel zoom gesture. With no offset compensation the
        /// gesture must be intercepted before ANY scrollable control — including the workflow's
        /// scroll viewer and a node card's internal AutoScroll panels — has a chance to scroll. The
        /// wheel message is addressed to the control under the cursor (WM_MOUSEWHEEL targets the
        /// focused/focused-under-mouse window), so message handlers on the surface only ever see
        /// wheel events routed to the surface itself; a wheel over a child window is delivered to
        /// that child and never bubbles. This filter therefore resolves the surface host from the
        /// message's target control, zooms, marks the message handled so the native wheel message is
        /// dropped (no scroll anywhere), and swallows it (never forwards to the target).
        /// </summary>
        bool IMessageFilter.PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmMouseWheel || Control.ModifierKeys != Keys.Control)
            {
                return false;
            }

            var host = ResolveSurfaceHost(m.HWnd);
            if (host is null)
            {
                return false;
            }

            var tree = ResolveTree(host);
            if (tree is null)
            {
                return false;
            }

            var delta = unchecked((short)((uint)m.WParam.ToInt64() >> 16));
            // Wheel up (positive delta) zooms in: Scale is a collapse factor, so zoom-in divides it by 1/1.1.
            var factor = delta > 0 ? 1 / 1.1 : 1.1;
            var next = Math.Max(0.1, Math.Min(10, tree.Layout.Scale.Horizontal * factor));
            var layout = tree.Layout;

            if (layout.ZoomCenter == ZoomCenter.ViewportCenter)
            {
                var scrollOffset = ResolveScrollOffset(host, tree);
                var clientSize = ResolveClientSize(host);
                var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                    scrollOffset.Horizontal, scrollOffset.Vertical, clientSize.Width, clientSize.Height, layout);
                layout.CollapsePivot = new Anchor(wx, wy, 0);
                layout.Scale = new Scale(next, next);
                // Deep zoom-in collapses negative-world content to w/Scale past the fixed NegativeOffset;
                // grow the cover first (monotonic, no-op for positive-only content) so the PivotCenterScroll
                // below and the Refresh read the NEW ActualOffset.
                WorkflowSurfaceMath.EnsureNegativeCover(tree);
                var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, clientSize.Width, clientSize.Height);
                ApplyScrollOffset(host, tx, ty);
                Refresh(host);
            }
            else
            {
                layout.Scale = new Scale(next, next);
                // World-origin zoom keeps content top-left aligned: nothing downstream reads the new
                // ActualOffset, so push the grown cover (if any) through the repaint path explicitly.
                if (WorkflowSurfaceMath.EnsureNegativeCover(tree))
                {
                    Refresh(host);
                }
            }

            m.Result = IntPtr.Zero;
            return true; // swallow the message: the target control never scrolls
        }

        private Control? ResolveSurfaceHost(IntPtr hwnd)
        {
            var target = Control.FromHandle(hwnd);
            var host = target;
            while (host is not null)
            {
                if (host is not null && ReferenceEquals(host, _filterHost))
                {
                    return host;
                }

                if (States.TryGetValue(host, out var state) && state.ZoomEnabled)
                {
                    return host;
                }

                host = host.Parent;
            }

            return null;
        }
    }

    private static readonly ConditionalWeakTable<Control, SurfaceState> States = new();

    /// <summary>
    /// Gets whether the workflow surface behavior is enabled for the specified control.
    /// </summary>
    public static bool GetIsEnabled(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).IsEnabled;
    }

    /// <summary>
    /// Sets whether the workflow surface behavior is enabled for the specified control.
    /// </summary>
    public static void SetIsEnabled(Control element, bool value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).IsEnabled = value;

        if (value)
        {
            // When the host canvas is enabled, automatically orchestrate Win32 window styles to eliminate flicker/ghosting
            // from the repaint separation between the self-drawn canvas and child windows (node cards): the canvas window
            // gets WS_CLIPCHILDREN and its top-level form gets WS_EX_COMPOSITED (DWM composites the whole form tree). Host needs no changes.
            NativeWindowStyleHelper.EnsureClipChildren(element);
            NativeWindowStyleHelper.EnsureComposited(element);
        }
    }

    /// <summary>Gets whether Ctrl + mouse-wheel zoom is enabled for the specified surface control.</summary>
    public static bool GetZoomEnabled(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).ZoomEnabled;
    }

    /// <summary>Sets whether Ctrl + mouse-wheel zoom is enabled for the specified surface control.</summary>
    public static void SetZoomEnabled(Control element, bool value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var state = GetState(element);
        if (state.ZoomEnabled == value)
        {
            return;
        }

        state.ZoomEnabled = value;
        state._filterHost = value ? element : null;
        if (value)
        {
            element.MouseWheel += OnZoomMouseWheel;
            // A message filter catches the Ctrl+wheel gesture before any descendant control
            // (e.g. a node card's internal AutoScroll panel, or the surface's own scroll viewer)
            // can scroll with it. Hooking WndProc on the element only catches wheel events routed
            // to the element itself — wheel sent to a child window never reaches it.
            Application.AddMessageFilter(state);
        }
        else
        {
            element.MouseWheel -= OnZoomMouseWheel;
            Application.RemoveMessageFilter(state);
        }
    }

    private static void OnZoomMouseWheel(object? sender, MouseEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        var tree = ResolveTree(control);
        if (tree is null || Control.ModifierKeys != Keys.Control)
        {
            return;
        }

        // Wheel up (positive delta) zooms in: Scale is a collapse factor, so zoom-in divides it by 1/1.1.
        var factor = e.Delta > 0 ? 1 / 1.1 : 1.1;
        var next = Math.Max(0.1, Math.Min(10, tree.Layout.Scale.Horizontal * factor));
        var layout = tree.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter)
        {
            var scrollOffset = ResolveScrollOffset(control, tree);
            var clientSize = ResolveClientSize(control);
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                scrollOffset.Horizontal, scrollOffset.Vertical, clientSize.Width, clientSize.Height, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);
            // Deep zoom-in collapses negative-world content to w/Scale past the fixed NegativeOffset;
            // grow the cover first (monotonic, no-op for positive-only content) so the PivotCenterScroll
            // below and the Refresh read the NEW ActualOffset.
            WorkflowSurfaceMath.EnsureNegativeCover(tree);
            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, clientSize.Width, clientSize.Height);
            ApplyScrollOffset(control, tx, ty);
            Refresh(control);
        }
        else
        {
            layout.Scale = new Scale(next, next);
            // World-origin zoom keeps content top-left aligned: nothing downstream reads the new
            // ActualOffset, so push the grown cover (if any) through the repaint path explicitly.
            if (WorkflowSurfaceMath.EnsureNegativeCover(tree))
            {
                Refresh(control);
            }
        }

        // Mark the wheel event handled so the Ctrl+wheel gesture only zooms — without this the
        // MouseWheel bubbles up to the AutoScroll parent and scrolls the viewport while zooming.
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }
    }

    /// <summary>
    /// Gets the configured scroll viewer host name.
    /// </summary>
    public static string? GetScrollViewerName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).ScrollViewerName;
    }

    /// <summary>
    /// Sets the configured scroll viewer host name.
    /// </summary>
    public static void SetScrollViewerName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).ScrollViewerName = value;
        EnsureClipChildrenForName(element, value);
    }

    /// <summary>
    /// Gets the configured canvas host name.
    /// </summary>
    public static string? GetCanvasName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).CanvasName;
    }

    /// <summary>
    /// Sets the configured canvas host name.
    /// </summary>
    public static void SetCanvasName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).CanvasName = value;
        EnsureClipChildrenForName(element, value);
    }

    /// <summary>
    /// Gets the configured grid decorator host name.
    /// </summary>
    public static string? GetGridDecoratorName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).GridDecoratorName;
    }

    /// <summary>
    /// Sets the configured grid decorator host name.
    /// </summary>
    public static void SetGridDecoratorName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).GridDecoratorName = value;
        EnsureClipChildrenForName(element, value);
    }

    /// <summary>
    /// Gets the configured pointer press source host name.
    /// </summary>
    public static string? GetPointerPressSourceName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).PointerPressSourceName;
    }

    /// <summary>
    /// Sets the configured pointer press source host name.
    /// </summary>
    public static void SetPointerPressSourceName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).PointerPressSourceName = value;
    }

    /// <summary>
    /// Gets the configured minimap overlay host name.
    /// </summary>
    public static string? GetMinimapOverlayName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).MinimapOverlayName;
    }

    /// <summary>
    /// Sets the configured minimap overlay host name. When the named control implements
    /// <see cref="IWorkflowMinimapOverlay"/>, <see cref="Refresh"/> pushes scroll, content
    /// offset, viewport, and tree values into it on every refresh cycle.
    /// </summary>
    public static void SetMinimapOverlayName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).MinimapOverlayName = value;
        EnsureClipChildrenForName(element, value);
    }

    /// <summary>
    /// Gets the workflow tree bound to the surface host, if one was set via <see cref="SetWorkflowTree"/>.
    /// </summary>
    public static IWorkflowTreeViewModel? GetWorkflowTree(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).WorkflowTree;
    }

    /// <summary>
    /// Explicitly binds the workflow tree to the surface host so <see cref="Refresh"/> can push the
    /// visible viewport and decorator/minimap offsets. Mirrors setting the host <c>DataContext</c> in
    /// the XAML adapters.
    /// </summary>
    public static void SetWorkflowTree(Control element, IWorkflowTreeViewModel? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).WorkflowTree = value;
    }

    /// <summary>
    /// Requests the host to refresh its layout and redraw, mirroring other workflow surface adapters.
    /// In addition to <c>PerformLayout</c>/<c>Invalidate</c>, this pushes the current scroll/content
    /// offsets into any named <see cref="IWorkflowGridDecorator"/>/<see cref="IWorkflowMinimapOverlay"/>
    /// controls and updates the workflow tree's visible viewport from the host scroll position.
    /// </summary>
    public static void Refresh(Control host)
    {
        if (host is null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        var state = GetState(host);
        var tree = ResolveTree(host);
        var scrollOffset = ResolveScrollOffset(host, tree);
        var clientSize = ResolveClientSize(host);
        var contentOffset = tree?.Layout?.ActualOffset ?? new Offset();

        // Update the tree viewport so consumers (e.g. spatial virtualization) observe
        // the current visible region. Best-effort: never throw from a refresh cycle.
        if (tree is not null && clientSize.Width > 0 && clientSize.Height > 0)
        {
            try
            {
                tree.GetHelper().Viewport = new Viewport(
                    WorkflowSurfaceMath.ToWorld(scrollOffset.Horizontal, contentOffset.Horizontal),
                    WorkflowSurfaceMath.ToWorld(scrollOffset.Vertical, contentOffset.Vertical),
                    clientSize.Width,
                    clientSize.Height);
            }
            catch
            {
                // The tree helper may not support viewport writes on some hosts; ignore.
            }
        }

        if (!string.IsNullOrWhiteSpace(state.GridDecoratorName)
            && FindControlByName(host, state.GridDecoratorName!) is IWorkflowGridDecorator decorator)
        {
            decorator.ScrollOffsetX = scrollOffset.Horizontal;
            decorator.ScrollOffsetY = scrollOffset.Vertical;
            decorator.ContentOffsetX = contentOffset.Horizontal;
            decorator.ContentOffsetY = contentOffset.Vertical;
        }

        if (!string.IsNullOrWhiteSpace(state.MinimapOverlayName)
            && FindControlByName(host, state.MinimapOverlayName!) is IWorkflowMinimapOverlay minimap)
        {
            minimap.ScrollOffsetX = scrollOffset.Horizontal;
            minimap.ScrollOffsetY = scrollOffset.Vertical;
            minimap.ContentOffsetX = contentOffset.Horizontal;
            minimap.ContentOffsetY = contentOffset.Vertical;
            minimap.ViewportWidth = clientSize.Width;
            minimap.ViewportHeight = clientSize.Height;
            minimap.WorkflowTree = tree;
        }

        // Publish the translate transform as a notification carrier for host canvases.
        WorkflowCanvasTransformBehavior.Apply(host, contentOffset);

        host.PerformLayout();

        // Asynchronous invalidation: WM_PAINT is coalesced by the message loop. Refresh is called frequently by non-interactive
        // scenarios (runtime status refresh, scrolling, collection changes); always repainting synchronously would make the
        // self-drawn canvas redraw every frame and stutter, so the default stays asynchronous.
        // Exception: repaint synchronously while the host captures the mouse (canvas pan/drag in progress) — otherwise high-
        // frequency mouse messages keep deferring WM_PAINT and the node's old position and old links are not erased in time,
        // leaving ghosts. Node-drag synchronous repaint is triggered separately by WorkflowNodeDragBehavior, so not repeated here.
        host.Invalidate();
        if (host.Capture)
        {
            host.Update();
        }
    }

    private static IWorkflowTreeViewModel? ResolveTree(Control host)
    {
        if (GetWorkflowTree(host) is { } explicitTree)
        {
            return explicitTree;
        }

        var current = host;
        while (current is not null)
        {
            IWorkflowTreeViewModel? tree = ResolveValue(current, "ViewModel") as IWorkflowTreeViewModel;
            tree ??= ResolveValue(current, "DataContext") as IWorkflowTreeViewModel;
            tree ??= ResolveValue(current, "BindingContext") as IWorkflowTreeViewModel;
            tree ??= current.Tag as IWorkflowTreeViewModel;
            if (tree is not null)
            {
                return tree;
            }

            current = current.Parent;
        }

        return null;
    }

    private static Offset ResolveScrollOffset(Control host, IWorkflowTreeViewModel? tree)
    {
        // Effective scroll = the negative of the host's world-origin translate (pan), so
        // WorldAtViewportCenter sees scroll space consistent with node positioning. The node views
        // sit at node.Anchor + pan (+ ActualOffset for hosts that translate the content separately),
        // never at node.Anchor + ViewportOffset — falling back to ViewportOffset double-subtracts
        // the content offset and makes the captured pivot drift on every wheel notch.
        if (host is ScrollableControl scrollable && scrollable.AutoScroll)
        {
            // Full demo host: node translate = _panOffset + AutoScrollPosition; the scroll range
            // is clamped >= 0, so the pivot can only be reached within it (overscroll clamps).
            var pan = ResolvePanOffset(host) ?? new System.Drawing.Point();
            return new Offset(-(pan.X + scrollable.AutoScrollPosition.X), -(pan.Y + scrollable.AutoScrollPosition.Y));
        }

        // Signed-pan host (template / Trimmed demo): the canvas stays fixed over the viewport and
        // node views are positioned at node.Anchor + PanOffset, so effective scroll = -PanOffset.
        var signedPan = ResolvePanOffset(host);
        if (signedPan is not null)
        {
            return new Offset(-signedPan.Value.X, -signedPan.Value.Y);
        }

        // No pan translate exposed: fall back to the persisted viewport offset (world space).
        return tree?.Layout?.ViewportOffset ?? new Offset();
    }

    /// <summary>
    /// Applies a ViewportCenter-zoom scroll (effective scroll space) back into the host's pan
    /// translate. Mirrors the capture path in <see cref="ResolveScrollOffset"/>: an AutoScroll host
    /// receives AutoScrollPosition (negative-signed getter), a signed-pan host receives the scroll
    /// through its own OnMinimapScrollRequested(sx, sy) → _panOffset = (-sx, -sy) + ApplyPan — the
    /// exact "put world point (sx, sy) at the viewport origin" contract the recenter needs. Going
    /// through the host keeps the private pan field, node positions, grid/minimap and viewport all
    /// consistent — writing the canvas PanOffset property directly would be clobbered by the
    /// deferred ApplyPan the Layout property change schedules.
    /// </summary>
    private static void ApplyScrollOffset(Control host, double x, double y)
    {
        if (host is ScrollableControl scrollable && scrollable.AutoScroll)
        {
            // WinForms AutoScrollPosition setter negates its argument (getter = −setter), and the
            // node translate includes the pan offset, so to land the effective scroll at (x, y)
            // the setter must receive (x + panOffset). Verify: getter scr = −(x + pan), effective
            // scroll = −(pan + scr) = x. The (x + pan) shape mirrors the demo's own minimap
            // compensation (_panOffset = −sx − scroll → setter = sx + panOffset).
            var pan = ResolvePanOffset(host) ?? new System.Drawing.Point();
            scrollable.AutoScrollPosition = new System.Drawing.Point((int)Math.Round(x + pan.X), (int)Math.Round(y + pan.Y));
            return;
        }

        // Signed-pan host: recenter via its minimap-scroll handler (same "_panOffset = (-sx, -sy);
        // ApplyPan()" logic as panning). Skip the full demo (AutoScroll) — handled above — and any
        // control whose handler would recurse into the message filter.
        var target = ResolveNamedCanvas(host) ?? host;
        for (var p = target; p is not null; p = p.Parent)
        {
            var method = p.GetType().GetMethod(
                "OnMinimapScrollRequested",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(double), typeof(double) },
                modifiers: null);
            if (method is not null)
            {
                try
                {
                    method.Invoke(p, new object[] { x, y });
                }
                catch
                {
                    // Best-effort; the refresh cycle after this still re-pushes the viewport.
                }

                return;
            }
        }
    }

    /// <summary>Resolves the named canvas configured on the host state, if any.</summary>
    private static Control? ResolveNamedCanvas(Control host)
    {
        var state = GetState(host);
        return !string.IsNullOrWhiteSpace(state.CanvasName) ? FindControlByName(host, state.CanvasName!) : null;
    }

    /// <summary>
    /// Reads the signed pan translate a host exposes as a <c>Point PanOffset</c> property (the
    /// template / Trimmed demo surface canvas) or keeps in a private <c>_panOffset</c> field (the
    /// full demo's self-drawn canvas). Same reflective lookup as the node-view template's
    /// <c>GetCanvasPanOffset</c> — the surface canvas is a private nested control, so the adapter
    /// cannot name its type. Returns <see langword="null"/> when no pan translate exists.
    /// </summary>
    private static System.Drawing.Point? ResolvePanOffset(Control host)
    {
        // The pan translate lives on the named canvas (the host tree-view owns it privately and
        // pushes it in ApplyPan), so start the reflection from the canvas, not the host.
        var canvas = ResolveNamedCanvas(host);
        for (var p = canvas ?? host; p is not null; p = p.Parent)
        {
            var property = p.GetType().GetProperty("PanOffset");
            if (property?.CanRead == true && property.PropertyType == typeof(System.Drawing.Point))
            {
                return (System.Drawing.Point)property.GetValue(p)!;
            }

            // Full demo (self-drawn canvas): the pan lives in a private _panOffset field.
            var field = p.GetType().GetField(
                "_panOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.FieldType == typeof(System.Drawing.Point))
            {
                return (System.Drawing.Point)field.GetValue(p)!;
            }
        }

        return null;
    }

    private static System.Drawing.Size ResolveClientSize(Control host)
        => host.ClientSize.Width > 0 ? host.ClientSize : host.Size;

    private static void EnsureClipChildrenForName(Control root, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // Resolve the named control (e.g. PART_ScrollViewer / PART_Canvas / PART_GridDecorator) and automatically add
        // WS_CLIPCHILDREN to its window: these layered containers clip child regions while repainting, avoiding layer
        // flicker from covering node views/links. The control can be resolved through the control tree before its handle exists.
        if (FindControlByName(root, name!) is Control control)
        {
            NativeWindowStyleHelper.EnsureClipChildren(control);
        }
    }

    private static Control? FindControlByName(Control root, string name)
    {
        foreach (var control in EnumerateSelfAndDescendants(root))
        {
            if (string.Equals(control.Name, name, StringComparison.Ordinal))
            {
                return control;
            }
        }

        return null;
    }

    private static System.Collections.Generic.IEnumerable<Control> EnumerateSelfAndDescendants(Control root)
    {
        yield return root;
        foreach (var child in root.Controls.OfType<Control>())
        {
            yield return child;
            foreach (var descendant in EnumerateSelfAndDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static object? ResolveValue(Control control, string propertyName)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var property = control.GetType().GetProperty(propertyName, flags);
        if (property?.CanRead != true || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        return property.GetValue(control);
    }

    private static SurfaceState GetState(Control element)
        => States.GetValue(element, static _ => new SurfaceState());
}
