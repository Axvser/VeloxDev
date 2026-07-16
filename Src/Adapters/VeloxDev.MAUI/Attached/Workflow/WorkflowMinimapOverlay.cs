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

    private struct BoundsRect
    {
        public double Left, Top, Width, Height;
        public readonly double Right => Left + Width;
        public readonly double Bottom => Top + Height;
        public readonly bool IsEmpty => Width <= 0 || Height <= 0;

        public static BoundsRect Union(BoundsRect a, BoundsRect b)
        {
            if (a.IsEmpty) return b;
            if (b.IsEmpty) return a;
            var l = Math.Min(a.Left, b.Left);
            var t = Math.Min(a.Top, b.Top);
            var r = Math.Max(a.Right, b.Right);
            var btm = Math.Max(a.Bottom, b.Bottom);
            return new BoundsRect { Left = l, Top = t, Width = r - l, Height = btm - t };
        }

        public static BoundsRect FromNode(double x, double y, double w, double h)
            => new() { Left = x, Top = y, Width = w, Height = h };
    }

    // ── State ────────────────────────────────────────────────────────────────

    private BoundsRect _lastGlobalBounds;
    private readonly List<(double X, double Y, double W, double H)> _lastNodeRects = [];
    private BoundsRect _lastViewport;
    private bool _pendingRefresh = true;
    private bool _isDragging;
    private float _dragOffsetX, _dragOffsetY;
    private ContentView? _parentView;
#if WINDOWS
    private PointerEventHandler? _nativeMovedHandler;
    private PointerEventHandler? _nativeReleasedHandler;
    private bool _nativeDragSubscribed;
    private bool _nativePointerCaptured;
#endif
    private PointerGestureRecognizer? _parentPointerRecognizer;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];
    private IWorkflowTreeViewModel? _subscribedTree;
    private ScrollView? _scrollView;

    // Drawing intermediates (float for MAUI ICanvas)
    private float _mmW, _mmH, _ox, _oy, _sc;
    private BoundsRect _drawGb;

    public WorkflowMinimapOverlay()
    {
        Drawable = this;
        HeightRequest = MinimapHeight;
        WidthRequest = MinimapWidth;

        StartInteraction += OnStartInteraction;
        DragInteraction += OnDragInteraction;
        EndInteraction += OnEndInteraction;
        Loaded += OnLoaded;
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
    }

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

    private void MarkDirty() { _pendingRefresh = true; Invalidate(); }

    // ── Data refresh ─────────────────────────────────────────────────────────

    private void RefreshMinimapData()
    {
        _pendingRefresh = false;
        var tree = WorkflowTree;
        if (tree is null) { ClearCache(); return; }

        var gb = default(BoundsRect);
        _lastNodeRects.Clear();
        bool first = true;

        if (tree.Nodes is not null)
            foreach (var node in tree.Nodes)
            {
                var (nx, ny, nw, nh) = (node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height);
                _lastNodeRects.Add((nx, ny, nw, nh));
                var nr = BoundsRect.FromNode(nx, ny, nw, nh);
                if (first) { gb = nr; first = false; } else gb = BoundsRect.Union(gb, nr);
            }
        _lastGlobalBounds = gb;

        _lastViewport = BoundsRect.FromNode(ScrollOffsetX - ContentOffsetX, ScrollOffsetY - ContentOffsetY,
            Math.Max(1, ViewportWidth), Math.Max(1, ViewportHeight));
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
        _mmW = Math.Max(minSz, Math.Min((float)MinimapWidth, availWidth));
        _mmH = Math.Max(minSz, Math.Min((float)MinimapHeight, availHeight));
        var pad = (float)Math.Max(0, ContentPadding);
        var drawW = _mmW - pad * 2;
        var drawH = _mmH - pad * 2;
        _sc = Math.Min(drawW / Math.Max(1, (float)gb.Width), drawH / Math.Max(1, (float)gb.Height));
        var sw = (float)gb.Width * _sc;
        var sh = (float)gb.Height * _sc;
        _ox = pad + (drawW - sw) / 2;
        _oy = pad + (drawH - sh) / 2;
    }

    // ── Touch input ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the viewport rectangle's render position in minimap coordinates,
    /// clamped so it always stays within the minimap's visible area.
    /// This is the position used for drawing AND hit testing.
    /// </summary>
    private (float X, float Y, float W, float H) GetClampedViewportRect()
    {
        var vp = _lastViewport;
        var gb = _lastGlobalBounds;
        if (vp.IsEmpty || gb.IsEmpty || _sc <= 0)
            return (0, 0, 0, 0);

        var w = Math.Max(2f, (float)(vp.Width * _sc));
        var h = Math.Max(2f, (float)(vp.Height * _sc));
        var x = Math.Max(0, Math.Min(_mmW - w, _ox + (float)((vp.Left - gb.Left) * _sc)));
        var y = Math.Max(0, Math.Min(_mmH - h, _oy + (float)((vp.Top - gb.Top) * _sc)));
        return (x, y, w, h);
    }

    private bool GetViewportHitTest(float px, float py)
    {
        var (l, t, w, h) = GetClampedViewportRect();
        return w > 0 && h > 0 && px >= l && px <= l + w && py >= t && py <= t + h;
    }

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (_isDragging) return;
        if (_pendingRefresh) RefreshMinimapData();
        var aw = (float)Math.Max(WidthRequest, 1);
        var ah = (float)Math.Max(HeightRequest, 1);
        ComputeDrawing(aw, ah);

        var pt = e.Touches[0];
        if (!GetViewportHitTest(pt.X, pt.Y)) return;

        // Use the CLAMPED rendering position for drag offset calculation,
        // so dragging the rectangle at the minimap edge works correctly.
        var (l, t, w, h) = GetClampedViewportRect();
        _dragOffsetX = pt.X - (l + w / 2);
        _dragOffsetY = pt.Y - (t + h / 2);
        NavigateToWorld(pt.X - _dragOffsetX, pt.Y - _dragOffsetY);
        _isDragging = true;

        // Subscribe to pointer events on the Page (covers full window)
        // so drag continues even when the pointer leaves the minimap.
        SubscribeDragCapture();
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (!_isDragging) return;
        var pt = e.Touches[0];
        NavigateToWorld(pt.X - _dragOffsetX, pt.Y - _dragOffsetY);
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        _isDragging = false;
        UnsubscribeDragCapture();
    }

    private void SubscribeDragCapture()
    {
#if WINDOWS
        SubscribeNativeDragCapture();
#else
        SubscribeParentGestureCapture();
#endif
    }

    private void UnsubscribeDragCapture()
    {
#if WINDOWS
        UnsubscribeNativeDragCapture();
#else
        UnsubscribeParentGestureCapture();
#endif
    }

#if WINDOWS
    private void SubscribeNativeDragCapture()
    {
        if (_parentView?.Handler?.PlatformView is not FrameworkElement fe) return;
        if (_nativeDragSubscribed) return;
        _nativeDragSubscribed = true;
        _nativePointerCaptured = false;

        _nativeMovedHandler = (s, e) =>
        {
            if (!_isDragging) return;

            // Capture pointer on first move so tracking continues outside bounds.
            if (!_nativePointerCaptured)
            {
                fe.CapturePointer(e.Pointer);
                _nativePointerCaptured = true;
            }

            var parentPt = e.GetCurrentPoint(fe).Position;

            if (this.Handler?.PlatformView is UIElement mmElement)
            {
                var transform = mmElement.TransformToVisual(fe);
                var mmOrigin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                var mmRelX = parentPt.X - mmOrigin.X;
                var mmRelY = parentPt.Y - mmOrigin.Y;
                NavigateToWorld((float)mmRelX - _dragOffsetX, (float)mmRelY - _dragOffsetY);
            }
        };

        _nativeReleasedHandler = (s, e) =>
        {
            _isDragging = false;
            UnsubscribeNativeDragCapture();
        };

        // handledEventsToo:true ensures we get pointer events even when the
        // minimap GraphicsView (child) handles the interaction first.
        fe.AddHandler(UIElement.PointerMovedEvent, _nativeMovedHandler, true);
        fe.AddHandler(UIElement.PointerReleasedEvent, _nativeReleasedHandler, true);
        fe.AddHandler(UIElement.PointerCanceledEvent, _nativeReleasedHandler, true);
        fe.AddHandler(UIElement.PointerCaptureLostEvent, _nativeReleasedHandler, true);
    }

    private void UnsubscribeNativeDragCapture()
    {
        if (_parentView?.Handler?.PlatformView is not FrameworkElement fe) return;
        _nativeDragSubscribed = false;
        _nativePointerCaptured = false;

        if (_nativeMovedHandler is not null)
        {
            fe.RemoveHandler(UIElement.PointerMovedEvent, _nativeMovedHandler);
            fe.RemoveHandler(UIElement.PointerReleasedEvent, _nativeReleasedHandler);
            fe.RemoveHandler(UIElement.PointerCanceledEvent, _nativeReleasedHandler);
            fe.RemoveHandler(UIElement.PointerCaptureLostEvent, _nativeReleasedHandler);
            _nativeMovedHandler = null;
            _nativeReleasedHandler = null;
        }
    }
#endif

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
        NavigateToWorld((float)pos.Value.X - _dragOffsetX, (float)pos.Value.Y - _dragOffsetY);
    }

    private void OnParentPointerReleased(object? sender, PointerEventArgs e)
    {
        _isDragging = false;
        UnsubscribeDragCapture();
    }

    private CancellationTokenSource? _scrollCts;
    private float _lastScrollX = float.MinValue;
    private float _lastScrollY = float.MinValue;

    private void NavigateToWorld(float adjX, float adjY)
    {
        if (_drawGb.IsEmpty || _sc <= 0) return;
        var wcx = (adjX - _ox) / _sc + _drawGb.Left;
        var wcy = (adjY - _oy) / _sc + _drawGb.Top;
        var scrollX = (wcx - (float)ViewportWidth / 2) + (float)ContentOffsetX;
        var scrollY = (wcy - (float)ViewportHeight / 2) + (float)ContentOffsetY;
        if (_scrollView is null || WorkflowTree?.Layout is not { } layout) return;

        var maxH = (float)Math.Max(0, _scrollView.ContentSize.Width - _scrollView.Width);
        var maxV = (float)Math.Max(0, _scrollView.ContentSize.Height - _scrollView.Height);
        bool layoutChanged = false;

        if (scrollX < 0)
        {
            var addX = -scrollX;
            if (addX > 0.5f)
            {
                layout.NegativeOffset = new Offset(
                    layout.NegativeOffset.Horizontal + addX,
                    layout.NegativeOffset.Vertical);
                scrollX = 0;
                layoutChanged = true;
            }
        }
        else if (scrollX > maxH)
        {
            var addX = scrollX - maxH;
            if (addX > 0.5f)
            {
                layout.PositiveOffset = new Offset(
                    layout.PositiveOffset.Horizontal + addX,
                    layout.PositiveOffset.Vertical);
                scrollX = maxH;
                layoutChanged = true;
            }
        }

        if (scrollY < 0)
        {
            var addY = -scrollY;
            if (addY > 0.5f)
            {
                layout.NegativeOffset = new Offset(
                    layout.NegativeOffset.Horizontal,
                    layout.NegativeOffset.Vertical + addY);
                scrollY = 0;
                layoutChanged = true;
            }
        }
        else if (scrollY > maxV)
        {
            var addY = scrollY - maxV;
            if (addY > 0.5f)
            {
                layout.PositiveOffset = new Offset(
                    layout.PositiveOffset.Horizontal,
                    layout.PositiveOffset.Vertical + addY);
                scrollY = maxV;
                layoutChanged = true;
            }
        }

        // Only scroll when there's meaningful change or layout was expanded
        var clampedX = Math.Max(0, Math.Min(scrollX, maxH));
        var clampedY = Math.Max(0, Math.Min(scrollY, maxV));

        // Throttle: skip ScrollToAsync if the target position hasn't changed
        // meaningfully since the last call. This avoids flooding MAUI's layout
        // system during continuous minimap drag.
        if (!layoutChanged &&
            Math.Abs(clampedX - _lastScrollX) < 2f &&
            Math.Abs(clampedY - _lastScrollY) < 2f)
            return;

        _lastScrollX = (float)clampedX;
        _lastScrollY = (float)clampedY;

        if (layoutChanged)
        {
            // When canvas expanded, wait for layout to settle then scroll
            _scrollCts?.Cancel();
            _scrollCts = new CancellationTokenSource();
            var ct = _scrollCts.Token;
            _ = Task.Delay(16, ct).ContinueWith(async _ =>
            {
                if (!ct.IsCancellationRequested)
                {
                    await MainThread.InvokeOnMainThreadAsync(
                        () => _scrollView?.ScrollToAsync(clampedX, clampedY, false));
                }
            }, ct);
        }
        else
        {
            _ = _scrollView.ScrollToAsync(clampedX, clampedY, false);
        }
    }

    // ── IDrawable implementation ─────────────────────────────────────────────

    void IDrawable.Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (!IsMinimapVisible) return;
        if (_pendingRefresh) RefreshMinimapData();

        // Use dirtyRect for actual drawing area; fall back to WidthRequest if layout not ready
        var w = dirtyRect.Width > 0 ? dirtyRect.Width : (float)Math.Max(WidthRequest, 1);
        var h = dirtyRect.Height > 0 ? dirtyRect.Height : (float)Math.Max(HeightRequest, 1);
        if (w <= 0 || h <= 0) return;
        if (WorkflowTree?.Nodes is null || WorkflowTree.Nodes.Count == 0) return;

        ComputeDrawing(w, h);
        var gb = _drawGb;
        if (gb.IsEmpty || gb.Width <= 0 || gb.Height <= 0 || _sc <= 0) return;

        var cr = Math.Max(0, (float)MinimapCornerRadius);

        // Background
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
            // Nodes
            if (NodeFillColor is not null)
            {
                canvas.FillColor = NodeFillColor;
                var ncr = Math.Max(0, (float)NodeCornerRadius);
                foreach (var (nx, ny, nw, nh) in _lastNodeRects)
                    canvas.FillRoundedRectangle(
                        _ox + (float)((nx - gb.Left) * _sc),
                        _oy + (float)((ny - gb.Top) * _sc),
                        Math.Max(2f, (float)(nw * _sc)),
                        Math.Max(2f, (float)(nh * _sc)), ncr);
            }

            // Viewport indicator — use GetClampedViewportRect for consistency
            // with hit testing and drag interaction.
            var (vpx, vpy, vpw, vph) = GetClampedViewportRect();
            if (vpw > 0 && vph > 0)
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
}
