using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Windows.Foundation;
using Windows.UI;
using WfAnchor = VeloxDev.WorkflowSystem.Anchor;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A minimap overlay for WinUI that renders a thumbnail overview of all nodes,
/// links, and the visible viewport in the top-right corner.
/// Uses XAML shapes (Rectangle, Line) for rendering.
/// </summary>
public class WorkflowMinimapOverlay : Canvas, IWorkflowMinimapOverlay
{
    // ── Dependency Properties ────────────────────────────────────────────────

    public static readonly DependencyProperty ScrollOffsetXProperty =
        DependencyProperty.Register(nameof(ScrollOffsetX), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ScrollOffsetYProperty =
        DependencyProperty.Register(nameof(ScrollOffsetY), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ContentOffsetXProperty =
        DependencyProperty.Register(nameof(ContentOffsetX), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ContentOffsetYProperty =
        DependencyProperty.Register(nameof(ContentOffsetY), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty WorkflowTreeProperty =
        DependencyProperty.Register(nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(null, (d, e) => ((WorkflowMinimapOverlay)d).OnTreeChanged((IWorkflowTreeViewModel?)e.NewValue)));

    public static readonly DependencyProperty ViewportWidthProperty =
        DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1d, OnPropChanged));

    public static readonly DependencyProperty ViewportHeightProperty =
        DependencyProperty.Register(nameof(ViewportHeight), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1d, OnPropChanged));

    public static readonly DependencyProperty IsMinimapVisibleProperty =
        DependencyProperty.Register(nameof(IsMinimapVisible), typeof(bool), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(true, OnPropChanged));

    public static readonly DependencyProperty MinimapWidthProperty =
        DependencyProperty.Register(nameof(MinimapWidth), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(200d, OnPropChanged));

    public static readonly DependencyProperty MinimapHeightProperty =
        DependencyProperty.Register(nameof(MinimapHeight), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(140d, OnPropChanged));

    public static readonly DependencyProperty RulerThicknessProperty =
        DependencyProperty.Register(nameof(RulerThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(28d, OnPropChanged));

    public static readonly DependencyProperty LinkStrokeThicknessProperty =
        DependencyProperty.Register(nameof(LinkStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(2.0, OnPropChanged));

    public static readonly DependencyProperty MinimapBackgroundProperty =
        DependencyProperty.Register(nameof(MinimapBackground), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(210, 20, 25, 34)), OnPropChanged));

    public static readonly DependencyProperty MinimapBorderBrushProperty =
        DependencyProperty.Register(nameof(MinimapBorderBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(220, 148, 163, 184)), OnPropChanged));

    public static readonly DependencyProperty NodeBrushProperty =
        DependencyProperty.Register(nameof(NodeBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(220, 56, 189, 248)), OnPropChanged));

    public static readonly DependencyProperty LinkBrushProperty =
        DependencyProperty.Register(nameof(LinkBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(180, 180, 200, 220)), OnPropChanged));

    public static readonly DependencyProperty ViewportStrokeProperty =
        DependencyProperty.Register(nameof(ViewportStroke), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), OnPropChanged));

    public static readonly DependencyProperty ViewportFillProperty =
        DependencyProperty.Register(nameof(ViewportFill), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), OnPropChanged));

    public static readonly DependencyProperty ViewportStrokeThicknessProperty =
        DependencyProperty.Register(nameof(ViewportStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1.5, OnPropChanged));

    public static readonly DependencyProperty MinimapCornerRadiusProperty =
        DependencyProperty.Register(nameof(MinimapCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(4.0, OnPropChanged));

    public static readonly DependencyProperty MinimapBorderThicknessProperty =
        DependencyProperty.Register(nameof(MinimapBorderThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1.0, OnPropChanged));

    public static readonly DependencyProperty NodeCornerRadiusProperty =
        DependencyProperty.Register(nameof(NodeCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1.0, OnPropChanged));

    public static readonly DependencyProperty ContentPaddingProperty =
        DependencyProperty.Register(nameof(ContentPadding), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(2.0, OnPropChanged));

    public static readonly DependencyProperty MinimapMinSizeProperty =
        DependencyProperty.Register(nameof(MinimapMinSize), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(20.0, OnPropChanged));

    public static readonly DependencyProperty ScrollViewerNameProperty =
        DependencyProperty.Register(nameof(ScrollViewerName), typeof(string), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(null));    // ── CLR accessors ────────────────────────────────────────────────────────

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
    public Brush? MinimapBackground { get => (Brush?)GetValue(MinimapBackgroundProperty); set => SetValue(MinimapBackgroundProperty, value); }
    public Brush? MinimapBorderBrush { get => (Brush?)GetValue(MinimapBorderBrushProperty); set => SetValue(MinimapBorderBrushProperty, value); }
    public Brush? NodeBrush { get => (Brush?)GetValue(NodeBrushProperty); set => SetValue(NodeBrushProperty, value); }
    public Brush? LinkBrush { get => (Brush?)GetValue(LinkBrushProperty); set => SetValue(LinkBrushProperty, value); }
    public Brush? ViewportStroke { get => (Brush?)GetValue(ViewportStrokeProperty); set => SetValue(ViewportStrokeProperty, value); }
    public Brush? ViewportFill { get => (Brush?)GetValue(ViewportFillProperty); set => SetValue(ViewportFillProperty, value); }
    public double ViewportStrokeThickness { get => (double)GetValue(ViewportStrokeThicknessProperty); set => SetValue(ViewportStrokeThicknessProperty, value); }
    public double MinimapCornerRadius { get => (double)GetValue(MinimapCornerRadiusProperty); set => SetValue(MinimapCornerRadiusProperty, value); }
    public double MinimapBorderThickness { get => (double)GetValue(MinimapBorderThicknessProperty); set => SetValue(MinimapBorderThicknessProperty, value); }
    public double NodeCornerRadius { get => (double)GetValue(NodeCornerRadiusProperty); set => SetValue(NodeCornerRadiusProperty, value); }
    public double ContentPadding { get => (double)GetValue(ContentPaddingProperty); set => SetValue(ContentPaddingProperty, value); }
    public double MinimapMinSize { get => (double)GetValue(MinimapMinSizeProperty); set => SetValue(MinimapMinSizeProperty, value); }
    public string? ScrollViewerName { get => (string?)GetValue(ScrollViewerNameProperty); set => SetValue(ScrollViewerNameProperty, value); }

    // ── State ────────────────────────────────────────────────────────────────

    private WorkflowBounds _lastGlobalBounds;
    private readonly List<(double X, double Y, double W, double H)> _lastNodeRects = [];
    private WorkflowBounds _lastViewport;
    private bool _pendingRefresh = true;
    private bool _isDragging;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];
    private IWorkflowTreeViewModel? _subscribedTree;
    private ScrollViewer? _scrollViewer;

    // Shape pools
    private readonly List<Rectangle> _nodeRects = [];
    private Rectangle? _viewportRect;
    private Rectangle? _bgRect;
    private Rectangle? _borderRect;

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _refreshTimer;

    public WorkflowMinimapOverlay()
    {
        Width = MinimapWidth;
        Height = MinimapHeight;

        // Only subscribe timer if we're on UI thread
        try
        {
            _refreshTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.CreateTimer();
            if (_refreshTimer is not null)
            {
                _refreshTimer.Interval = TimeSpan.FromMilliseconds(16);
                _refreshTimer.Tick += (s, e) => RebuildShapes();
            }
        }
        catch { }

        PointerPressed += OnPointerPressedHandler;
        PointerMoved += OnPointerMovedHandler;
        PointerReleased += OnPointerReleasedHandler;
        PointerCaptureLost += OnPointerCaptureLostHandler;
        Loaded += (s, e) => ResolveScrollViewer();
    }

    private void ResolveScrollViewer()
    {
        if (string.IsNullOrWhiteSpace(ScrollViewerName)) return;
        // Walk up to the UserControl (name scope root) and find by name
        FrameworkElement? el = this;
        while (el is not null)
        {
            if (el is UserControl uc)
            {
                var found = uc.FindName(ScrollViewerName);
                if (found is ScrollViewer sv)
                {
                    if (_scrollViewer is not null) _scrollViewer.SizeChanged -= OnScrollViewerResized;
                    _scrollViewer = sv;
                    // The behavior pushes ViewportWidth on ViewChanged (scroll), but ViewChanged does
                    // NOT fire for a viewport-SIZE change — and ScrollViewer.ViewportWidth can lag at
                    // SizeChanged mid-layout. Read the ScrollViewer's settled viewport on ITS resize so
                    // the draggable block follows the real visible area when the window shrinks.
                    _scrollViewer.SizeChanged += OnScrollViewerResized;
                }
                return;
            }
            el = VisualTreeHelper.GetParent(el) as FrameworkElement;
        }
    }

    private void OnScrollViewerResized(object? sender, SizeChangedEventArgs e)
    {
        // Defer past the current layout pass so the size is settled; then push the ACTUAL visible
        // area and re-render. ActualWidth/Height = the element's rendered size — the real visible
        // region — whereas ScrollViewer.ViewportWidth can report the effective/larger value; the
        // block must shrink to the actual area when the window shrinks.
        // ScrollOffsetX/Y are unchanged by a pure resize (the block's top-left stays put).
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_scrollViewer is null) return;
            ViewportWidth = Math.Max(0, _scrollViewer.ActualWidth);
            ViewportHeight = Math.Max(0, _scrollViewer.ActualHeight);
            MarkDirty();
        });
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
        if (_subscribedNodes.Add(n) && n is INotifyPropertyChanged npc)
            npc.PropertyChanged += OnNodePropChanged;
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
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
            MarkDirty();
    }

    private void OnSlotPropChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor))
            MarkDirty();
    }

    private static void OnPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WorkflowMinimapOverlay)d).MarkDirty();

    private void MarkDirty()
    {
        _pendingRefresh = true;
        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        if (_refreshTimer is not null)
        {
            // Throttle, not debounce: restarting a running timer on every MarkDirty
            // means it never ticks during a continuous pan — the minimap only redraws
            // once movement stops. Start only when idle so it ticks at the fixed 16 ms
            // cadence while updates keep arriving.
            if (!_refreshTimer.IsRunning)
            {
                _refreshTimer.Start();
            }
        }
        else
        {
            RebuildShapes();
        }
    }

    // ── Data refresh ─────────────────────────────────────────────────────────

    private void RefreshMinimapData()
    {
        _pendingRefresh = false;
        var tree = WorkflowTree;
        if (tree is null) { ClearCache(); return; }

        var globalBounds = default(WorkflowBounds);
        _lastNodeRects.Clear();
        bool first = true;

        if (tree.Nodes is not null)
            foreach (var node in tree.Nodes)
            {
                var (nx, ny, nw, nh) = (node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height);
                _lastNodeRects.Add((nx, ny, nw, nh));
                var nr = WorkflowBounds.FromNode(nx, ny, nw, nh);
                if (first) { globalBounds = nr; first = false; }
                else globalBounds = WorkflowBounds.Union(globalBounds, nr);
            }

        _lastGlobalBounds = globalBounds;

        var vw = Math.Max(1, ViewportWidth);
        var vh = Math.Max(1, ViewportHeight);
        _lastViewport = WorkflowBounds.FromNode(
            WorkflowSurfaceMath.ToWorld(ScrollOffsetX, ContentOffsetX),
            WorkflowSurfaceMath.ToWorld(ScrollOffsetY, ContentOffsetY),
            vw, vh);
    }

    private void ClearCache()
    {
        _lastNodeRects.Clear();
        _lastGlobalBounds = default;
        _lastViewport = default;
    }

    // ── Transform ─────────────────────────────────────────────────────────────

    private (double Ox, double Oy, double MmW, double MmH, double Sc) ComputeTransform(WorkflowBounds gb)
    {
        var margin = 0.0;
        var minSz = Math.Max(1, MinimapMinSize);
        var mmW = Math.Max(minSz, Math.Min(MinimapWidth, ActualWidth - margin * 2));
        var mmH = Math.Max(minSz, Math.Min(MinimapHeight, ActualHeight - margin * 2));
        var pad = Math.Max(0, ContentPadding);
        var (ox, oy, sc) = WorkflowSurfaceMath.MinimapFit(gb.Width, gb.Height, mmW - pad * 2, mmH - pad * 2, pad);
        return (ox, oy, mmW, mmH, sc);
    }

    // ── Pointer ──────────────────────────────────────────────────────────────

    private Rect? GetViewportRectInMinimap()
    {
        var vp = _lastViewport;
        var gb = _lastGlobalBounds;
        if (vp.IsEmpty || gb.IsEmpty) return null;

        var (ox, oy, mmW, mmH, sc) = ComputeTransform(gb);
        if (sc <= 0) return null;

        // Shared fit + clamp: maps the world-space viewport through the minimap fit and
        // keeps the block inside the minimap even when the viewport exceeds the content.
        var (l, t, w, h) = WorkflowSurfaceMath.MinimapViewportRect(
            ox, oy, sc, vp.Left, vp.Top, vp.Width, vp.Height,
            gb.Left, gb.Top, mmW, mmH, minRectSize: 2.0);
        return new Rect(l, t, w, h);
    }

    private void OnPointerPressedHandler(object? sender, PointerRoutedEventArgs e)
    {
        if (_isDragging) return;
        var pt = e.GetCurrentPoint(this).Position;

        // Match the Jalium adapter: the clicked point always becomes the viewport center —
        // no grab-anchor on the indicator block, so pressing anywhere recenters the view.
        NavigateToWorld(pt.X, pt.Y);
        _isDragging = true;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMovedHandler(object? sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        var pt = e.GetCurrentPoint(this).Position;
        NavigateToWorld(pt.X, pt.Y);
        e.Handled = true;
    }

    private void OnPointerReleasedHandler(object? sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        e.Handled = true;
    }

    private void OnPointerCaptureLostHandler(object? sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }

    private void NavigateToWorld(double adjX, double adjY)
    {
        var gb = _lastGlobalBounds;
        if (gb.IsEmpty) return;
        var (ox, oy, _, _, sc) = ComputeTransform(gb);
        if (sc <= 0) return;

        var (wcx, wcy) = WorkflowSurfaceMath.MinimapToWorld(adjX, adjY, ox, oy, sc, gb.Left, gb.Top);
        var (scrollX, scrollY) = WorkflowSurfaceMath.MinimapToScroll(
            wcx, wcy, ViewportWidth, ViewportHeight, ContentOffsetX, ContentOffsetY);
        if (_scrollViewer is not null && WorkflowTree?.Layout is { } layout)
        {
            var maxH = Math.Max(0, _scrollViewer.ScrollableWidth);
            var maxV = Math.Max(0, _scrollViewer.ScrollableHeight);

            scrollX = WorkflowSurfaceMath.ClampScrollOffset(scrollX, maxH, layout, horizontal: true);
            scrollY = WorkflowSurfaceMath.ClampScrollOffset(scrollY, maxV, layout, horizontal: false);

            _scrollViewer.ChangeView(
                Math.Max(0, Math.Min(scrollX, maxH)),
                Math.Max(0, Math.Min(scrollY, maxV)),
                null, true);
        }
    }

    // ── Shape rendering ──────────────────────────────────────────────────────

    private void RebuildShapes()
    {
        _refreshTimer?.Stop();

        if (!IsMinimapVisible)
        {
            Children.Clear();
            _nodeRects.Clear();
            _viewportRect = _bgRect = _borderRect = null;
            return;
        }

        if (_pendingRefresh) RefreshMinimapData();

        var mmW = Math.Max(Math.Max(1, MinimapMinSize), Math.Min(MinimapWidth, ActualWidth));
        var mmH = Math.Max(Math.Max(1, MinimapMinSize), Math.Min(MinimapHeight, ActualHeight));

        var gb = _lastGlobalBounds;
        bool hasData = gb.Width > 0 && gb.Height > 0;

        // Ensure background/border
        if (_bgRect is null)
        {
            _bgRect = new Rectangle();
            Children.Add(_bgRect);
        }
        _bgRect.Width = mmW;
        _bgRect.Height = mmH;
        _bgRect.Fill = MinimapBackground;
        _bgRect.RadiusX = _bgRect.RadiusY = Math.Max(0, MinimapCornerRadius);

        if (MinimapBorderBrush is not null)
        {
            if (_borderRect is null)
            {
                _borderRect = new Rectangle();
                Children.Add(_borderRect);
            }
            _borderRect.Width = mmW;
            _borderRect.Height = mmH;
            _borderRect.Stroke = MinimapBorderBrush;
            _borderRect.StrokeThickness = MinimapBorderThickness;
            _borderRect.RadiusX = _borderRect.RadiusY = Math.Max(0, MinimapCornerRadius);
        }

        if (!hasData || WorkflowTree?.Nodes is null || WorkflowTree.Nodes.Count == 0)
        {
            foreach (var r in _nodeRects) r.Visibility = Visibility.Collapsed;
            _viewportRect?.Visibility = Visibility.Collapsed;
            return;
        }

        var (ox, oy, _, _, sc) = ComputeTransform(gb);

        // Nodes
        var ncr = Math.Max(0, NodeCornerRadius);
        while (_nodeRects.Count < _lastNodeRects.Count)
        {
            var rect = new Rectangle();
            _nodeRects.Add(rect);
            Children.Add(rect);
        }
        for (int i = 0; i < _nodeRects.Count; i++)
        {
            var rect = _nodeRects[i];
            if (i < _lastNodeRects.Count)
            {
                var (nx, ny, nw, nh) = _lastNodeRects[i];
                var (rx, ry) = WorkflowSurfaceMath.MinimapLocal(nx, ny, gb.Left, gb.Top, ox, oy, sc);
                var rw = WorkflowSurfaceMath.MinThumbSize(nw, sc, 2.0);
                var rh = WorkflowSurfaceMath.MinThumbSize(nh, sc, 2.0);
                SetLeft(rect, rx);
                SetTop(rect, ry);
                rect.Width = rw;
                rect.Height = rh;
                rect.Fill = NodeBrush;
                rect.RadiusX = rect.RadiusY = ncr;
                rect.Visibility = Visibility.Visible;
            }
            else
            {
                rect.Visibility = Visibility.Collapsed;
            }
        }

        // Viewport indicator
        var vp = _lastViewport;
        if (!vp.IsEmpty)
        {
            if (_viewportRect is null)
            {
                _viewportRect = new Rectangle();
                Children.Add(_viewportRect);
            }
            // Same shared fit + clamp as GetViewportRectInMinimap.
            var (vpx, vpy, vpw, vph) = WorkflowSurfaceMath.MinimapViewportRect(
                ox, oy, sc, vp.Left, vp.Top, vp.Width, vp.Height,
                gb.Left, gb.Top, mmW, mmH, minRectSize: 2.0);
            SetLeft(_viewportRect, vpx);
            SetTop(_viewportRect, vpy);
            _viewportRect.Width = vpw;
            _viewportRect.Height = vph;
            _viewportRect.Fill = ViewportFill;
            _viewportRect.Stroke = ViewportStroke;
            _viewportRect.StrokeThickness = ViewportStrokeThickness;
            _viewportRect.RadiusX = _viewportRect.RadiusY = ncr;
            _viewportRect.Visibility = Visibility.Visible;
        }
        else
        {
            _viewportRect?.Visibility = Visibility.Collapsed;
        }
    }
}
