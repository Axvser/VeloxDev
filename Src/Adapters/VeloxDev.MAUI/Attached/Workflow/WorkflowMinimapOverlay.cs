using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
#endif
using WfAnchor = VeloxDev.WorkflowSystem.Anchor;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A minimap overlay for MAUI that renders a thumbnail overview of all nodes,
/// links, and the visible viewport in the top-right corner.
/// </summary>
public class WorkflowMinimapOverlay : GraphicsView, IDrawable, IWorkflowMinimapOverlay
{
    // ── Bindable Properties ──────────────────────────────────────────────────

    private static void OnVisualProp(BindableObject b, object o, object n) => ((WorkflowMinimapOverlay)b).MarkDirty();

    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(nameof(ScrollOffsetX), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(nameof(ScrollOffsetY), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(nameof(ContentOffsetX), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(nameof(ContentOffsetY), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportWidthProperty = BindableProperty.Create(nameof(ViewportWidth), typeof(double), typeof(WorkflowMinimapOverlay), 1d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportHeightProperty = BindableProperty.Create(nameof(ViewportHeight), typeof(double), typeof(WorkflowMinimapOverlay), 1d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty IsMinimapVisibleProperty = BindableProperty.Create(nameof(IsMinimapVisible), typeof(bool), typeof(WorkflowMinimapOverlay), true, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapWidthProperty = BindableProperty.Create(nameof(MinimapWidth), typeof(double), typeof(WorkflowMinimapOverlay), 200d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapHeightProperty = BindableProperty.Create(nameof(MinimapHeight), typeof(double), typeof(WorkflowMinimapOverlay), 140d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty RulerThicknessProperty = BindableProperty.Create(nameof(RulerThickness), typeof(double), typeof(WorkflowMinimapOverlay), 28d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty LinkStrokeThicknessProperty = BindableProperty.Create(nameof(LinkStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay), 2.0, propertyChanged: OnVisualProp);

    public static readonly BindableProperty MinimapBackgroundColorProperty = BindableProperty.Create(nameof(MinimapBackgroundColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(20, 25, 34, 210), propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapBorderColorProperty = BindableProperty.Create(nameof(MinimapBorderColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(148, 163, 184, 220), propertyChanged: OnVisualProp);
    public static readonly BindableProperty NodeFillColorProperty = BindableProperty.Create(nameof(NodeFillColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(56, 189, 248, 220), propertyChanged: OnVisualProp);
    public static readonly BindableProperty LinkStrokeColorProperty = BindableProperty.Create(nameof(LinkStrokeColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(180, 200, 220, 180), propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportStrokeColorProperty = BindableProperty.Create(nameof(ViewportStrokeColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(255, 255, 255, 240), propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportFillColorProperty = BindableProperty.Create(nameof(ViewportFillColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(255, 255, 255, 40), propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportStrokeThicknessProperty = BindableProperty.Create(nameof(ViewportStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay), 1.5, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapCornerRadiusProperty = BindableProperty.Create(nameof(MinimapCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay), 4.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapBorderThicknessProperty = BindableProperty.Create(nameof(MinimapBorderThickness), typeof(double), typeof(WorkflowMinimapOverlay), 1.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty NodeCornerRadiusProperty = BindableProperty.Create(nameof(NodeCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay), 1.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentPaddingProperty = BindableProperty.Create(nameof(ContentPadding), typeof(double), typeof(WorkflowMinimapOverlay), 2.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapMinSizeProperty = BindableProperty.Create(nameof(MinimapMinSize), typeof(double), typeof(WorkflowMinimapOverlay), 20.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ScrollViewerNameProperty = BindableProperty.Create(nameof(ScrollViewerName), typeof(string), typeof(WorkflowMinimapOverlay));

    public static readonly BindableProperty WorkflowTreeProperty = BindableProperty.Create(nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(WorkflowMinimapOverlay), null,
        propertyChanged: (b, o, n) => ((WorkflowMinimapOverlay)b).OnTreeChanged((IWorkflowTreeViewModel?)n));

    // ── CLR accessors ────────────────────────────────────────────────────────

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public bool IsMinimapVisible { get => (bool)GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }
    public double MinimapWidth { get => (double)GetValue(MinimapWidthProperty); set => SetValue(MinimapWidthProperty, value); }
    public double MinimapHeight { get => (double)GetValue(MinimapHeightProperty); set => SetValue(MinimapHeightProperty, value); }
    public double RulerThickness { get => (double)GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }
    public double LinkStrokeThickness { get => (double)GetValue(LinkStrokeThicknessProperty); set => SetValue(LinkStrokeThicknessProperty, value); }
    public Color? MinimapBackgroundColor { get => (Color?)GetValue(MinimapBackgroundColorProperty); set => SetValue(MinimapBackgroundColorProperty, value); }
    public Color? MinimapBorderColor { get => (Color?)GetValue(MinimapBorderColorProperty); set => SetValue(MinimapBorderColorProperty, value); }
    public Color? NodeFillColor { get => (Color?)GetValue(NodeFillColorProperty); set => SetValue(NodeFillColorProperty, value); }
    public Color? LinkStrokeColor { get => (Color?)GetValue(LinkStrokeColorProperty); set => SetValue(LinkStrokeColorProperty, value); }
    public Color? ViewportStrokeColor { get => (Color?)GetValue(ViewportStrokeColorProperty); set => SetValue(ViewportStrokeColorProperty, value); }
    public Color? ViewportFillColor { get => (Color?)GetValue(ViewportFillColorProperty); set => SetValue(ViewportFillColorProperty, value); }
    public double ViewportStrokeThickness { get => (double)GetValue(ViewportStrokeThicknessProperty); set => SetValue(ViewportStrokeThicknessProperty, value); }
    public double MinimapCornerRadius { get => (double)GetValue(MinimapCornerRadiusProperty); set => SetValue(MinimapCornerRadiusProperty, value); }
    public double MinimapBorderThickness { get => (double)GetValue(MinimapBorderThicknessProperty); set => SetValue(MinimapBorderThicknessProperty, value); }
    public double NodeCornerRadius { get => (double)GetValue(NodeCornerRadiusProperty); set => SetValue(NodeCornerRadiusProperty, value); }
    public double ContentPadding { get => (double)GetValue(ContentPaddingProperty); set => SetValue(ContentPaddingProperty, value); }
    public double MinimapMinSize { get => (double)GetValue(MinimapMinSizeProperty); set => SetValue(MinimapMinSizeProperty, value); }
    public string? ScrollViewerName { get => (string?)GetValue(ScrollViewerNameProperty); set => SetValue(ScrollViewerNameProperty, value); }

    // ── Internal types ───────────────────────────────────────────────────────

    // ── State ────────────────────────────────────────────────────────────────

    private WorkflowBounds _lastGlobalBounds;
    private readonly List<(double X, double Y, double W, double H)> _lastNodeRects = [];
    private WorkflowBounds _lastViewport;
    private bool _pendingRefresh = true;
    private bool _isDragging;
    private ContentView? _parentView;
#if WINDOWS
    // Native pan gesture: it rides the GraphicsView's captured manipulation pipeline
    // (ManipulationMode=All), so deltas keep arriving while the pointer is outside the
    // minimap — unlike routed PointerMoved listeners, which stop at the bounds because
    // only the capture owner receives pointer events once a manipulation begins.
    private PanGestureRecognizer? _panGesture;
    private float _dragStartX;
    private float _dragStartY;
    private PointerEventHandler? _nativePressedHandler;
    private PointerEventHandler? _nativeMovedHandler;
    private PointerEventHandler? _nativeReleasedHandler;
    // One deferred retry per handler epoch: HandlerChanged fires before the platform
    // view exists, so the first attach attempt is allowed to retry on the UI thread.
    private bool _platformAttachPending;
#endif

    private PointerGestureRecognizer? _parentPointerRecognizer;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];
    private IWorkflowTreeViewModel? _subscribedTree;
    private ScrollView? _scrollView;

    // Drawing intermediates (float for MAUI ICanvas)
    private float _mmW, _mmH, _ox, _oy, _sc;
    private WorkflowBounds _drawGb;

    public WorkflowMinimapOverlay()
    {
        Drawable = this;
        HeightRequest = MinimapHeight;
        WidthRequest = MinimapWidth;

        StartInteraction += OnStartInteraction;
        DragInteraction += OnDragInteraction;
        EndInteraction += OnEndInteraction;
        Loaded += OnLoaded;

#if WINDOWS
        // Attached statically (not lazily on drag start) so a press is always captured
        // even if the gesture suppresses MAUI's own touch interaction on the GraphicsView.
        // The drag end is owned by this gesture's Completed/Canceled.
        _panGesture = new PanGestureRecognizer();
        _panGesture.PanUpdated += OnPanUpdated;
        GestureRecognizers.Add(_panGesture);
#endif
    }

    private void OnLoaded(object? s, EventArgs e)
    {
        // Walk up parent hierarchy to find the root ContentView (WorkflowView).
        // Used for ScrollView lookup and drag-outside-bounds pointer tracking.
        Element? el = this;
        while (el is not null)
        {
            if (el is ContentView cv)
            {
                if (_parentView is null) _parentView = cv;
                if (!string.IsNullOrWhiteSpace(ScrollViewerName) && _scrollView is null)
                    _scrollView = cv.FindByName<ScrollView>(ScrollViewerName);
                break;
            }
            el = el.Parent;
        }

#if WINDOWS
        // The element is in the live tree now, so the platform view exists — attach the
        // native pointer handlers here (OnHandlerChanged runs before the view is created).
        AttachPlatformPressedHandler();
#endif
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if WINDOWS
        // New handler epoch: allow a fresh deferred retry if the platform view is not
        // created yet at this point.
        _platformAttachPending = false;
        AttachPlatformPressedHandler();
#endif
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);
#if WINDOWS
        // Detach from the outgoing platform view so the handlers don't leak across
        // handler recreations (e.g. page navigation).
        if (args.OldHandler?.PlatformView is UIElement oldEl)
        {
            if (_nativePressedHandler is not null)
            {
                oldEl.RemoveHandler(UIElement.PointerPressedEvent, _nativePressedHandler);
                _nativePressedHandler = null;
            }
            if (_nativeMovedHandler is not null)
            {
                oldEl.RemoveHandler(UIElement.PointerMovedEvent, _nativeMovedHandler);
                _nativeMovedHandler = null;
            }
            if (_nativeReleasedHandler is not null)
            {
                oldEl.RemoveHandler(UIElement.PointerReleasedEvent, _nativeReleasedHandler);
                oldEl.RemoveHandler(UIElement.PointerCanceledEvent, _nativeReleasedHandler);
                oldEl.RemoveHandler(UIElement.PointerCaptureLostEvent, _nativeReleasedHandler);
                _nativeReleasedHandler = null;
            }
        }
#endif
    }

#if WINDOWS
    /// <summary>
    /// Hooks the minimap's own platform element. The press always originates there, so
    /// these handlers fire even when the pan gesture claims the manipulation and
    /// suppresses MAUI's touch interaction (StartInteraction). With the GraphicsView's
    /// manipulation holding pointer capture, PointerMoved on this same element keeps
    /// arriving outside the bounds — a second, independent out-of-bounds input path on
    /// top of the pan gesture's manipulation deltas.
    /// </summary>
    private void AttachPlatformPressedHandler()
    {
        if (_nativePressedHandler is not null) return;
        if (this.Handler?.PlatformView is not UIElement mmEl)
        {
            // HandlerChanged is raised before MAUI creates the platform view (that happens
            // inside the handler's Setup). Defer one tick — the native element exists by the
            // time the UI thread processes it. Bounded by _platformAttachPending, so a view
            // that never materializes cannot spin.
            if (_platformAttachPending) return;
            _platformAttachPending = true;
            Dispatcher.Dispatch(() => AttachPlatformPressedHandler());
            return;
        }
        _nativePressedHandler = (s, e) =>
        {
            if (this.Handler?.PlatformView is not UIElement mm) return;
            var pt = e.GetCurrentPoint(mm).Position;
            _dragStartX = (float)pt.X;
            _dragStartY = (float)pt.Y;
            // Best-effort explicit capture: while captured, ONLY this element fires
            // pointer events, so PointerMoved below keeps flowing outside the bounds
            // until release. (Can only be taken during PointerPressed.)
            mm.CapturePointer(e.Pointer);
            // MAUI raises StartInteraction from its class handler before this instance
            // handler, so _isDragging is already true when the interaction ran; the probe
            // then only records the start. When the gesture suppresses the interaction,
            // the probe stands in for the full press handling.
            if (_isDragging) return;
            if (_pendingRefresh) RefreshMinimapData();
            var aw = (float)SafeDim(WidthRequest, 1);
            var ah = (float)SafeDim(HeightRequest, 1);
            ComputeDrawing(aw, ah);
            NavigateToWorld(_dragStartX, _dragStartY);
            _isDragging = true;
        };
        _nativeMovedHandler = (s, e) =>
        {
            if (!_isDragging) return;
            if (this.Handler?.PlatformView is not UIElement mm) return;
            var pt = e.GetCurrentPoint(mm).Position;
            NavigateToWorld((float)pt.X, (float)pt.Y);
        };
        _nativeReleasedHandler = (s, e) =>
        {
            if (!_isDragging) return;
            _isDragging = false;
        };
        mmEl.AddHandler(UIElement.PointerPressedEvent, _nativePressedHandler, true);
        mmEl.AddHandler(UIElement.PointerMovedEvent, _nativeMovedHandler, true);
        mmEl.AddHandler(UIElement.PointerReleasedEvent, _nativeReleasedHandler, true);
        mmEl.AddHandler(UIElement.PointerCanceledEvent, _nativeReleasedHandler, true);
        mmEl.AddHandler(UIElement.PointerCaptureLostEvent, _nativeReleasedHandler, true);
    }

    /// <summary>
    /// Drives the drag from native manipulation deltas. The manipulation holds pointer
    /// capture, so Running keeps arriving after the cursor leaves the minimap and only
    /// ends at the real release (Completed/Canceled) — matching the other frameworks.
    /// </summary>
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                break;
            case GestureStatus.Running:
                if (_isDragging)
                {
                    var x = _dragStartX + (float)e.TotalX;
                    var y = _dragStartY + (float)e.TotalY;
                    NavigateToWorld(x, y);
                }
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isDragging = false;
                break;
        }
    }
#endif

    // ── Tree management ──────────────────────────────────────────────────────

    private void OnTreeChanged(IWorkflowTreeViewModel? newTree)
    {
        UnsubscribeFromTree();
        _subscribedTree = newTree;
        if (newTree is null) return;

        if (newTree.Nodes is INotifyCollectionChanged nc)
        {
            nc.CollectionChanged += OnNodesChanged;
            foreach (var n in newTree.Nodes) SubscribeNode(n);
        }
        if (newTree.Links is INotifyCollectionChanged lc)
        {
            lc.CollectionChanged += OnLinksChanged;
            foreach (var l in newTree.Links) SubscribeLink(l);
        }
        MarkDirty();
    }

    private void UnsubscribeFromTree()
    {
        if (_subscribedTree is null) return;
        if (_subscribedTree.Nodes is INotifyCollectionChanged nc) nc.CollectionChanged -= OnNodesChanged;
        foreach (var n in _subscribedNodes) if (n is INotifyPropertyChanged npc) npc.PropertyChanged -= OnNodePropChanged;
        _subscribedNodes.Clear();
        if (_subscribedTree.Links is INotifyCollectionChanged lc) lc.CollectionChanged -= OnLinksChanged;
        foreach (var l in _subscribedLinks)
        {
            if (l.Sender is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotPropChanged;
            if (l.Receiver is INotifyPropertyChanged rp) rp.PropertyChanged -= OnSlotPropChanged;
        }
        _subscribedLinks.Clear();
        _subscribedTree = null;
    }

    private void SubscribeNode(IWorkflowNodeViewModel n)
    {
        if (_subscribedNodes.Add(n) && n is INotifyPropertyChanged npc) npc.PropertyChanged += OnNodePropChanged;
    }

    private void SubscribeLink(IWorkflowLinkViewModel l)
    {
        if (!_subscribedLinks.Add(l)) return;
        if (l.Sender is INotifyPropertyChanged sp) sp.PropertyChanged += OnSlotPropChanged;
        if (l.Receiver is INotifyPropertyChanged rp) rp.PropertyChanged += OnSlotPropChanged;
    }

    private void OnNodesChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (var i in e.NewItems) if (i is IWorkflowNodeViewModel n) SubscribeNode(n);
        if (e.OldItems is not null) foreach (var i in e.OldItems) if (i is IWorkflowNodeViewModel n && _subscribedNodes.Remove(n) && n is INotifyPropertyChanged npc) npc.PropertyChanged -= OnNodePropChanged;
        MarkDirty();
    }

    private void OnLinksChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (var i in e.NewItems) if (i is IWorkflowLinkViewModel l) SubscribeLink(l);
        if (e.OldItems is not null) foreach (var i in e.OldItems) if (i is IWorkflowLinkViewModel l && _subscribedLinks.Remove(l))
            {
                if (l.Sender is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotPropChanged;
                if (l.Receiver is INotifyPropertyChanged rp) rp.PropertyChanged -= OnSlotPropChanged;
            }
        MarkDirty();
    }

    private void OnNodePropChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size)) MarkDirty();
    }

    private void OnSlotPropChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor)) MarkDirty();
    }

    private bool _invalidatePending;

    private void MarkDirty()
    {
        _pendingRefresh = true;
        // A single frame can write several visual DPs back-to-back (ApplyVisibleRegion
        // sets ScrollOffsetX/Y + ContentOffsetX/Y + ViewportWidth/Height together), and a
        // minimap drag re-writes them per scroll delta. Each DP write is a full minimap
        // Invalidate — coalesce them into ONE redraw per frame by flushing on the next
        // main-thread dispatch instead of invalidating inline.
        if (_invalidatePending)
        {
            return;
        }

        _invalidatePending = true;
        // Invalidate requires the main thread; BeginInvokeOnMainThread both marshals
        // background sources and gives the coalescing window so back-to-back writes in
        // the same frame land in one flush.
        MainThread.BeginInvokeOnMainThread(FlushInvalidate);
    }

    private void FlushInvalidate()
    {
        _invalidatePending = false;
        Invalidate();
    }

    // ── Data refresh ─────────────────────────────────────────────────────────

    private void RefreshMinimapData()
    {
        _pendingRefresh = false;
        var tree = WorkflowTree;
        if (tree is null) { ClearCache(); return; }

        _lastNodeRects.Clear();

        if (tree.Nodes is not null)
            foreach (var node in tree.Nodes)
            {
                var (nx, ny, nw, nh) = (
                    double.IsNaN(node.Anchor.Horizontal) ? 0 : node.Anchor.Horizontal,
                    double.IsNaN(node.Anchor.Vertical) ? 0 : node.Anchor.Vertical,
                    Math.Max(1, node.Size.Width),
                    Math.Max(1, node.Size.Height));
                _lastNodeRects.Add((nx, ny, nw, nh));
            }
        _lastGlobalBounds = WorkflowBounds.FromNodes(_lastNodeRects);

        var rawVpX = WorkflowSurfaceMath.ToWorld(ScrollOffsetX, ContentOffsetX);
        var rawVpY = WorkflowSurfaceMath.ToWorld(ScrollOffsetY, ContentOffsetY);
        var vpX = double.IsNaN(rawVpX) ? 0 : rawVpX;
        var vpY = double.IsNaN(rawVpY) ? 0 : rawVpY;
        var vpW = double.IsNaN(ViewportWidth) ? 1 : Math.Max(1, ViewportWidth);
        var vpH = double.IsNaN(ViewportHeight) ? 1 : Math.Max(1, ViewportHeight);
        _lastViewport = WorkflowBounds.FromNode(vpX, vpY, vpW, vpH);
    }

    private void ClearCache()
    {
        _lastNodeRects.Clear();
        _lastGlobalBounds = default;
        _lastViewport = default;
    }

    // ── Compute float intermediates for drawing ─────────────────────────────

    private void ComputeDrawing(float availWidth, float availHeight)
    {
        var gb = _lastGlobalBounds;
        _drawGb = gb;
        var minSz = Math.Max(1f, (float)MinimapMinSize);

        // availWidth/Height can be NaN from WidthRequest during layout transitions.
        if (float.IsNaN(availWidth) || float.IsNaN(availHeight))
        {
            _mmW = minSz;
            _mmH = minSz;
            _sc = 1f;
            _ox = 0f;
            _oy = 0f;
            return;
        }

        _mmW = Math.Max(minSz, Math.Min((float)MinimapWidth, availWidth));
        _mmH = Math.Max(minSz, Math.Min((float)MinimapHeight, availHeight));
        var pad = (float)Math.Max(0, ContentPadding);
        var drawW = _mmW - pad * 2;
        var drawH = _mmH - pad * 2;

        // MinimapFit: scale = min(drawW/max(1,cw), drawH/max(1,ch)),
        // origin = pad + (draw − content·scale)/2.  Preserve MAUI's NaN/Infinity guard
        // on the fit scale (gb bounds can be NaN from a node with a NaN Size).
        var (fitOx, fitOy, fitSc) = WorkflowSurfaceMath.MinimapFit(
            (float)gb.Width, (float)gb.Height, drawW, drawH, pad);
        _sc = float.IsNaN((float)fitSc) || float.IsInfinity((float)fitSc) ? 1f : (float)fitSc;
        _ox = (float)fitOx;
        _oy = (float)fitOy;

        // Final NaN guard for _ox/_oy — if they're NaN all hit-testing breaks.
        if (float.IsNaN(_ox)) _ox = 0f;
        if (float.IsNaN(_oy)) _oy = 0f;
    }

    // ── Touch input ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the viewport rectangle's render position in minimap coordinates,
    /// clamped so it always stays within the minimap's visible area.
    /// This is the position used for drawing AND hit testing.
    /// All return values are guaranteed non-NaN.
    /// </summary>
    private (float X, float Y, float W, float H) GetClampedViewportRect()
    {
        var vp = _lastViewport;
        var gb = _lastGlobalBounds;
        if (vp.IsEmpty || gb.IsEmpty || _sc <= 0 || float.IsNaN(_sc))
            return (0, 0, 0, 0);

        var w = Math.Max(2f, SafeFloatMul(vp.Width, _sc));
        var h = Math.Max(2f, SafeFloatMul(vp.Height, _sc));
        if (float.IsNaN(w) || float.IsNaN(h))
            return (0, 0, 0, 0);

        // MinimapViewportRect maps the world viewport onto the minimap via the same fit
        // transform the content uses and clamps the indicator inside the minimap bounds
        // (min indicator size 2px, matching the old inline math).
        var (x, y, rw, rh) = WorkflowSurfaceMath.MinimapViewportRect(
            _ox, _oy, _sc,
            vp.Left, vp.Top, vp.Width, vp.Height,
            gb.Left, gb.Top,
            _mmW, _mmH, minRectSize: 2.0);

        // Preserve MAUI's final NaN guards on the helper's raw output.
        return (float.IsNaN((float)x) ? 0f : (float)x,
                float.IsNaN((float)y) ? 0f : (float)y,
                float.IsNaN((float)rw) ? 0f : (float)rw,
                float.IsNaN((float)rh) ? 0f : (float)rh);
    }

    /// <summary>Safe float multiplication guarding against NaN.</summary>
    private static float SafeFloatMul(double a, double b)
        => double.IsNaN(a) || double.IsNaN(b) ? 0f : (float)(a * b);

    /// <summary>Returns a safe dimension value, guarding against NaN and <=0.</summary>
    private static double SafeDim(double value, double fallback)
        => double.IsNaN(value) || value <= 0 ? fallback : value;

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
            if (_isDragging) return;
            if (_pendingRefresh) RefreshMinimapData();
            var aw = (float)SafeDim(WidthRequest, 1);
            var ah = (float)SafeDim(HeightRequest, 1);
            ComputeDrawing(aw, ah);

            if (e.Touches is null || e.Touches.Length == 0) return;
            var pt = e.Touches[0];

            // Match the Jalium adapter: the clicked point always becomes the viewport
            // center — no grab-anchor on the indicator block, so pressing anywhere
            // recenters the view.
#if WINDOWS
            _dragStartX = pt.X;
            _dragStartY = pt.Y;
#endif
            NavigateToWorld(pt.X, pt.Y);
            _isDragging = true;

            SubscribeDragCapture();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // GraphicsView touch events can fire with stale/empty Touches on WinUI
            // (dotnet/maui #13452).  This is non-fatal.
            System.Diagnostics.Debug.WriteLine($"[Minimap] StartInteraction error: {ex.Message}");
        }
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
            if (!_isDragging) return;
            if (e.Touches is null || e.Touches.Length == 0) return;
            var pt = e.Touches[0];
            NavigateToWorld(pt.X, pt.Y);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"[Minimap] DragInteraction error: {ex.Message}");
        }
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
#if WINDOWS
            // MAUI raises EndInteraction the instant the pointer leaves the minimap — and
            // again on release. The native handlers (mmEl PointerReleased / pan Completed)
            // own the drag end on Windows: they keep delivering out-of-bounds moves and
            // end at the real release, so EndInteraction must not tear the drag down here.
            return;
#else
            _isDragging = false;
            UnsubscribeDragCapture();
#endif
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"[Minimap] EndInteraction error: {ex.Message}");
        }
    }

    private void SubscribeDragCapture()
    {
#if WINDOWS
        // No-op: the native pan gesture and the platform PointerPressed probe are attached
        // statically (constructor / OnHandlerChanged), so there is nothing to subscribe
        // lazily. The drag end is owned by the pan gesture's Completed/Canceled.
#else
        SubscribeParentGestureCapture();
#endif
    }

    private void UnsubscribeDragCapture()
    {
#if WINDOWS
        // No-op: the static handlers stay attached for the lifetime of the view and are
        // detached in OnHandlerChanging.
#else
        UnsubscribeParentGestureCapture();
#endif
    }

    private void SubscribeParentGestureCapture()
    {
        if (_parentView is null || _parentPointerRecognizer is not null) return;
        _parentPointerRecognizer = new PointerGestureRecognizer();
        _parentPointerRecognizer.PointerMoved += OnParentPointerMoved;
        _parentPointerRecognizer.PointerReleased += OnParentPointerReleased;
        _parentView.GestureRecognizers.Add(_parentPointerRecognizer);
    }

    private void UnsubscribeParentGestureCapture()
    {
        if (_parentPointerRecognizer is null || _parentView is null) return;
        _parentView.GestureRecognizers.Remove(_parentPointerRecognizer);
        _parentPointerRecognizer.PointerMoved -= OnParentPointerMoved;
        _parentPointerRecognizer.PointerReleased -= OnParentPointerReleased;
        _parentPointerRecognizer = null;
    }

    private void OnParentPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(this);
        if (pos is null) return;
        NavigateToWorld((float)pos.Value.X, (float)pos.Value.Y);
    }

    private void OnParentPointerReleased(object? sender, PointerEventArgs e)
    {
        _isDragging = false;
        UnsubscribeDragCapture();
    }

    // ── Input / drag ─────────────────────────────────────────────────────────

    private float _lastScrollX = float.MinValue;
    private float _lastScrollY = float.MinValue;

    /// <summary>
    /// Navigate the main workflow viewport to world coordinates (adjX, adjY).
    /// When the target scroll position exceeds the current canvas bounds, the
    /// canvas model expands (PositiveOffset/NegativeOffset) and ApplyLayout
    /// translates the content accordingly.  The IsRefreshing guard in
    /// WorkflowSurfaceBehavior prevents the cascade that caused the original
    /// crash — expansion is safe now.
    /// </summary>
    private void NavigateToWorld(float adjX, float adjY)
    {
        if (_drawGb.IsEmpty || _sc <= 0) return;

        // MinimapToWorld: world = (mm − origin)/scale + contentLeft; MinimapToScroll then
        // converts the world target to the scroll offset that centers it on the viewport.
        var (wcx, wcy) = WorkflowSurfaceMath.MinimapToWorld(
            adjX, adjY, _ox, _oy, _sc, _drawGb.Left, _drawGb.Top);
        var (scrollX, scrollY) = WorkflowSurfaceMath.MinimapToScroll(
            wcx, wcy, ViewportWidth, ViewportHeight, ContentOffsetX, ContentOffsetY);
        if (_scrollView is null || WorkflowTree?.Layout is not { } layout) return;

        // Max scroll extent comes from the layout MODEL (ActualSize = OriginSize + offsets),
        // which grows synchronously the moment ClampScrollOffset writes the overshoot below.
        // _scrollView.ContentSize lags the async MAUI layout pass, so clamping against it
        // pins the target at the current edge; the 2px throttle then skips the scroll and the
        // canvas never grows — a self-locking stop at the boundary.  The canvas pan
        // (WorkflowSurfaceBehavior) uses the model for exactly this reason.
        var svW = _scrollView.Width;
        var svH = _scrollView.Height;

        var maxH = ComputeMaxScroll(layout.ActualSize.Width, svW);
        var maxV = ComputeMaxScroll(layout.ActualSize.Height, svH);

        // Expand canvas model when drag reaches edge.  ClampScrollOffset writes the
        // overshoot into NegativeOffset (before origin) or PositiveOffset (past the edge)
        // only when it exceeds the 0.5f dead-band (sub-pixel jitter), returning the clamped
        // scroll offset.  The canvas only grows through ApplyLayout (driven by
        // WorkflowSurfaceBehavior.Refresh), so on expansion we apply layout NOW — mirroring
        // ApplyPanAsync — and recompute the max from the model, which already reflects the
        // growth.
        bool layoutChanged = (scrollX < 0 && -scrollX > 0.5f)
            || (scrollX > maxH && scrollX - maxH > 0.5f)
            || (scrollY < 0 && -scrollY > 0.5f)
            || (scrollY > maxV && scrollY - maxV > 0.5f);

        scrollX = WorkflowSurfaceMath.ClampScrollOffset(scrollX, maxH, layout, horizontal: true, threshold: 0.5f);
        scrollY = WorkflowSurfaceMath.ClampScrollOffset(scrollY, maxV, layout, horizontal: false, threshold: 0.5f);

        // Recompute max after expansion (model just grew synchronously).
        if (layoutChanged)
        {
            if (_parentView is not null)
            {
                WorkflowSurfaceBehavior.Refresh(_parentView);
            }
            maxH = ComputeMaxScroll(layout.ActualSize.Width, _scrollView.Width);
            maxV = ComputeMaxScroll(layout.ActualSize.Height, _scrollView.Height);
        }

        var clampedX = SafeClamp(scrollX, maxH);
        var clampedY = SafeClamp(scrollY, maxV);

        // Throttle: skip ScrollToAsync if the target hasn't changed meaningfully.
        if (Math.Abs(clampedX - _lastScrollX) < 2f &&
            Math.Abs(clampedY - _lastScrollY) < 2f)
        {
            return;
        }

        _lastScrollX = clampedX;
        _lastScrollY = clampedY;

        SafeScrollTo(_scrollView, clampedX, clampedY);
    }

    /// <summary>
    /// Compute max scroll offset from content and viewport dimensions,
    /// fully guarded against NaN/zero/infinity from async MAUI layout.
    /// </summary>
    private static float ComputeMaxScroll(double content, double viewport)
    {
        if (double.IsNaN(content) || content <= 0 ||
            double.IsNaN(viewport) || viewport <= 0)
            return 0f;
        return (float)Math.Max(0, content - viewport);
    }

    /// <summary>
    /// Clamp scroll offset within valid range, guarding against NaN/Infinity
    /// that may propagate from intermediate layout state.
    /// </summary>
    private static float SafeClamp(double value, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0f;
        if (double.IsNaN(max) || double.IsInfinity(max)) return 0f;
        return (float)Math.Max(0, Math.Min(value, max));
    }

    /// <summary>
    /// Call ScrollToAsync with exception protection.  NaN/Infinity values
    /// cause ArgumentException crashes in the native scroll viewer.
    /// </summary>
    private static void SafeScrollTo(ScrollView? sv, float x, float y)
    {
        if (sv is null) return;
        if (float.IsNaN(x) || float.IsInfinity(x) ||
            float.IsNaN(y) || float.IsInfinity(y))
            return;
        try
        {
            _ = sv.ScrollToAsync(x, y, false);
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"[Minimap] Scroll error: {ex.Message}");
        }
    }

    // ── IDrawable implementation ─────────────────────────────────────────────

    void IDrawable.Draw(ICanvas canvas, RectF dirtyRect)
    {
        try
        {
            // The MAUI GraphicsView Draw callback has no exception protection on WinUI
            // (dotnet/maui #14567). If a render call throws (e.g. NaN coordinates),
            // the exception leaks directly into the WinUI UnhandledException.
            if (!IsMinimapVisible) return;
            if (_pendingRefresh) RefreshMinimapData();

            // dirtyRect.Width/Height may be NaN (a known MAUI WinUI issue).
            // float/double.IsNaN is required: NaN > 0 is false, and NaN <= 0 is also false.
            var dw = dirtyRect.Width;
            var dh = dirtyRect.Height;
            var w = (!float.IsNaN(dw) && dw > 0)
                ? dw : (float)SafeDim(WidthRequest, 1);
            var h = (!float.IsNaN(dh) && dh > 0)
                ? dh : (float)SafeDim(HeightRequest, 1);
            if (w <= 0 || h <= 0) return;
            if (WorkflowTree?.Nodes is null || WorkflowTree.Nodes.Count == 0) return;

            ComputeDrawing(w, h);
            var gb = _drawGb;
            if (gb.IsEmpty || gb.Width <= 0 || gb.Height <= 0 || _sc <= 0) return;

            var cr = Math.Max(0, (float)MinimapCornerRadius);

            if (MinimapBackgroundColor is not null)
            {
                canvas.FillColor = MinimapBackgroundColor;
                canvas.FillRoundedRectangle(0, 0, _mmW, _mmH, cr);
            }
            if (MinimapBorderColor is not null)
            {
                canvas.StrokeColor = MinimapBorderColor;
                canvas.StrokeSize = (float)MinimapBorderThickness;
                canvas.DrawRoundedRectangle(0, 0, _mmW, _mmH, cr);
            }

            canvas.SaveState();
            canvas.ClipRectangle(0, 0, _mmW, _mmH);
            try
            {
                if (NodeFillColor is not null)
                {
                    canvas.FillColor = NodeFillColor;
                    var ncr = Math.Max(0, (float)NodeCornerRadius);
                    foreach (var (nx, ny, nw, nh) in _lastNodeRects)
                    {
                        // MinimapLocal: local = origin + (world − contentOrigin)·scale.
                        var (lx, ly) = WorkflowSurfaceMath.MinimapLocal(nx, ny, gb.Left, gb.Top, _ox, _oy, _sc);
                        canvas.FillRoundedRectangle(
                            (float)lx, (float)ly,
                            Math.Max(2f, (float)(nw * _sc)),
                            Math.Max(2f, (float)(nh * _sc)), ncr);
                    }
                }

                var (vpx, vpy, vpw, vph) = GetClampedViewportRect();
                if (vpw > 0 && vph > 0 && !float.IsNaN(vpx) && !float.IsNaN(vpy))
                {
                    var ncr = Math.Max(0, (float)NodeCornerRadius);

                    if (ViewportFillColor is not null)
                    {
                        canvas.FillColor = ViewportFillColor;
                        canvas.FillRoundedRectangle(vpx, vpy, vpw, vph, ncr);
                    }
                    if (ViewportStrokeColor is not null)
                    {
                        canvas.StrokeColor = ViewportStrokeColor;
                        canvas.StrokeSize = (float)ViewportStrokeThickness;
                        canvas.DrawRoundedRectangle(vpx, vpy, vpw, vph, ncr);
                    }
                }
            }
            finally { canvas.RestoreState(); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Minimap] Draw error: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
