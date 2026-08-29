using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A minimap overlay for WPF that renders a thumbnail overview of all nodes,
/// links, and the visible viewport in the top-right corner.
/// Only the viewport indicator rectangle can be dragged to navigate.
/// </summary>
public class WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay
{
    // ── Dependency Properties ────────────────────────────────────────────────

    public static readonly DependencyProperty ScrollOffsetXProperty =
        DependencyProperty.Register(nameof(ScrollOffsetX), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ScrollOffsetYProperty =
        DependencyProperty.Register(nameof(ScrollOffsetY), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ContentOffsetXProperty =
        DependencyProperty.Register(nameof(ContentOffsetX), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ContentOffsetYProperty =
        DependencyProperty.Register(nameof(ContentOffsetY), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty WorkflowTreeProperty =
        DependencyProperty.Register(nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(null, (d, e) => ((WorkflowMinimapOverlay)d).OnTreeChanged((IWorkflowTreeViewModel?)e.NewValue)));

    public static readonly DependencyProperty ViewportWidthProperty =
        DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(1d, OnPropChanged));

    public static readonly DependencyProperty ViewportHeightProperty =
        DependencyProperty.Register(nameof(ViewportHeight), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(1d, OnPropChanged));

    public static readonly DependencyProperty IsMinimapVisibleProperty =
        DependencyProperty.Register(nameof(IsMinimapVisible), typeof(bool), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(true, OnPropChanged));

    public static readonly DependencyProperty MinimapWidthProperty =
        DependencyProperty.Register(nameof(MinimapWidth), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(200d, OnPropChanged));

    public static readonly DependencyProperty MinimapHeightProperty =
        DependencyProperty.Register(nameof(MinimapHeight), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(140d, OnPropChanged));

    public static readonly DependencyProperty RulerThicknessProperty =
        DependencyProperty.Register(nameof(RulerThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(28d, OnPropChanged));

    public static readonly DependencyProperty LinkStrokeThicknessProperty =
        DependencyProperty.Register(nameof(LinkStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(2.0, OnPropChanged));

    // ── Brush / Style properties ─────────────────────────────────────────────

    public static readonly DependencyProperty MinimapBackgroundProperty =
        DependencyProperty.Register(nameof(MinimapBackground), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(210, 20, 25, 34)), OnPropChanged));

    public static readonly DependencyProperty MinimapBorderBrushProperty =
        DependencyProperty.Register(nameof(MinimapBorderBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(220, 148, 163, 184)), OnPropChanged));

    public static readonly DependencyProperty NodeBrushProperty =
        DependencyProperty.Register(nameof(NodeBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(220, 56, 189, 248)), OnPropChanged));

    public static readonly DependencyProperty LinkBrushProperty =
        DependencyProperty.Register(nameof(LinkBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(180, 180, 200, 220)), OnPropChanged));

    public static readonly DependencyProperty ViewportStrokeProperty =
        DependencyProperty.Register(nameof(ViewportStroke), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), OnPropChanged));

    public static readonly DependencyProperty ViewportFillProperty =
        DependencyProperty.Register(nameof(ViewportFill), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), OnPropChanged));

    public static readonly DependencyProperty ViewportStrokeThicknessProperty =
        DependencyProperty.Register(nameof(ViewportStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(1.5, OnPropChanged));

    public static readonly DependencyProperty MinimapCornerRadiusProperty =
        DependencyProperty.Register(nameof(MinimapCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(4.0, OnPropChanged));

    public static readonly DependencyProperty MinimapBorderThicknessProperty =
        DependencyProperty.Register(nameof(MinimapBorderThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(1.0, OnPropChanged));

    public static readonly DependencyProperty NodeCornerRadiusProperty =
        DependencyProperty.Register(nameof(NodeCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(1.0, OnPropChanged));

    public static readonly DependencyProperty ContentPaddingProperty =
        DependencyProperty.Register(nameof(ContentPadding), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(2.0, OnPropChanged));

    public static readonly DependencyProperty MinimapMinSizeProperty =
        DependencyProperty.Register(nameof(MinimapMinSize), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(20.0, OnPropChanged));

    public static readonly DependencyProperty ScrollViewerNameProperty =
        DependencyProperty.Register(nameof(ScrollViewerName), typeof(string), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(null));

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

    public WorkflowMinimapOverlay()
    {
        Width = MinimapWidth;
        Height = MinimapHeight;
        IsHitTestVisible = true;
        Focusable = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var pt = hitTestParameters.HitPoint;
        if (pt.X >= 0 && pt.X < RenderSize.Width && pt.Y >= 0 && pt.Y < RenderSize.Height)
            return new PointHitTestResult(this, pt);
        return null;
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ScrollViewerName)) return;

        // Walk up the visual tree to find the UserControl (name scope owner).
        // FindName on the UserControl CAN find all named elements in its XAML,
        // whereas Window.FindName cannot see inside UserControl namescopes.
        DependencyObject? el = this;
        while (el is not null)
        {
            if (el is UserControl uc)
            {
                var found = uc.FindName(ScrollViewerName);
                if (found is ScrollViewer sv)
                    _scrollViewer = sv;
                break;
            }
            el = VisualTreeHelper.GetParent(el);
        }
    }

    private void OnUnloaded(object? s, RoutedEventArgs e)
    {
        // Prevent memory leak: unsubscribe from all tree events and clear
        // strong references to ViewModel objects when the overlay is removed
        // from the visual tree (e.g. closing a tab).
        UnsubscribeFromTree();
        _scrollViewer = null;
    }

    // ── Tree change / subscription ──────────────────────────────────────────

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

        _pendingRefresh = true;
        InvalidateVisual();
    }

    private void UnsubscribeFromTree()
    {
        if (_subscribedTree is not null)
        {
            if (_subscribedTree.Nodes is INotifyCollectionChanged nc) nc.CollectionChanged -= OnNodesChanged;
            if (_subscribedTree.Links is INotifyCollectionChanged lc) lc.CollectionChanged -= OnLinksChanged;
        }
        foreach (var n in _subscribedNodes) if (n is INotifyPropertyChanged npc) npc.PropertyChanged -= OnNodePropChanged;
        _subscribedNodes.Clear();

        foreach (var l in _subscribedLinks)
        {
            if (l.Sender is INotifyPropertyChanged s) s.PropertyChanged -= OnSlotPropChanged;
            if (l.Receiver is INotifyPropertyChanged r) r.PropertyChanged -= OnSlotPropChanged;
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
        if (l.Sender is INotifyPropertyChanged s) s.PropertyChanged += OnSlotPropChanged;
        if (l.Receiver is INotifyPropertyChanged r) r.PropertyChanged += OnSlotPropChanged;
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
        if (!IsVisible) return;

        // OnNodePropChanged / OnSlotPropChanged can fire from the
        // MonoBehaviourManager loop thread (via BroadcastVisibleItemLayout).
        // InvalidateVisual requires the UI thread — dispatch if needed.
        if (Dispatcher.CheckAccess())
            InvalidateVisual();
        else
            Dispatcher.BeginInvoke(InvalidateVisual);
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
                var (nx, ny, nw, nh) = (node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height);
                _lastNodeRects.Add((nx, ny, nw, nh));
            }

        _lastGlobalBounds = WorkflowBounds.FromNodes(_lastNodeRects);

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

    // ── Hit test / drag ─────────────────────────────────────────────────────

    private Rect? GetViewportRectInMinimap()
    {
        var vp = _lastViewport;
        var gb = _lastGlobalBounds;
        if (vp.IsEmpty || gb.IsEmpty) return default;

        var (ox, oy, mmW, mmH, sc) = ComputeTransform(gb);
        if (sc <= 0) return null;

        var (l, t, w, h) = WorkflowSurfaceMath.MinimapViewportRect(
            ox, oy, sc, vp.Left, vp.Top, vp.Width, vp.Height, gb.Left, gb.Top, mmW, mmH, minRectSize: 2.0);
        return new Rect(l, t, w, h);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        var pt = e.GetPosition(this);

        // Match the Jalium adapter: the clicked point always becomes the viewport center —
        // no grab-anchor on the indicator block, so pressing anywhere recenters the view.
        NavigateToWorld(pt.X, pt.Y);
        _isDragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pt = e.GetPosition(this);
        NavigateToWorld(pt.X, pt.Y);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _isDragging = false;
        base.OnLostMouseCapture(e);
    }

    // ── Transform ─────────────────────────────────────────────────────────────

    private (double Ox, double Oy, double MmW, double MmH, double Sc) ComputeTransform(WorkflowBounds gb)
    {
        var margin = 0.0; // WPF margin is handled by Grid alignment
        var minSz = Math.Max(1, MinimapMinSize);
        var mmW = Math.Max(minSz, Math.Min(MinimapWidth, RenderSize.Width - margin * 2));
        var mmH = Math.Max(minSz, Math.Min(MinimapHeight, RenderSize.Height - margin * 2));
        var pad = Math.Max(0, ContentPadding);
        var (ox, oy, sc) = WorkflowSurfaceMath.MinimapFit(gb.Width, gb.Height, mmW - pad * 2, mmH - pad * 2, pad);
        return (ox, oy, mmW, mmH, sc);
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

            _scrollViewer.ScrollToHorizontalOffset(Math.Max(0, Math.Min(scrollX, maxH)));
            _scrollViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(scrollY, maxV)));
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        // Draw transparent background to establish visual content for hit-testing
        var sz = RenderSize;
        if (sz.Width <= 0 || sz.Height <= 0) return;
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(sz));

        if (!IsMinimapVisible) return;
        if (_pendingRefresh) RefreshMinimapData();
        if (WorkflowTree?.Nodes is null || WorkflowTree.Nodes.Count == 0) return;

        var minSz = Math.Max(1, MinimapMinSize);
        var mmW = Math.Max(minSz, Math.Min(MinimapWidth, sz.Width));
        var mmH = Math.Max(minSz, Math.Min(MinimapHeight, sz.Height));
        var mmRect = new Rect(0, 0, mmW, mmH);
        var cr = Math.Max(0, MinimapCornerRadius);

        // Minimap background
        if (MinimapBackground is not null)
            dc.DrawRoundedRectangle(MinimapBackground, null, mmRect, cr, cr);
        if (MinimapBorderBrush is not null)
            dc.DrawRoundedRectangle(null, new Pen(MinimapBorderBrush, MinimapBorderThickness), mmRect, cr, cr);

        var gb = _lastGlobalBounds;
        if (gb.IsEmpty || gb.Width <= 0 || gb.Height <= 0) return;

        var pad = Math.Max(0, ContentPadding);
        var (ox, oy, sc) = WorkflowSurfaceMath.MinimapFit(gb.Width, gb.Height, mmW - pad * 2, mmH - pad * 2, pad);

        var clipGeometry = new RectangleGeometry(mmRect);
        dc.PushClip(clipGeometry);
        try
        {
            // Nodes
            if (NodeBrush is not null)
            {
                var ncr = Math.Max(0, NodeCornerRadius);
                foreach (var (nx, ny, nw, nh) in _lastNodeRects)
                {
                    var (l, t) = WorkflowSurfaceMath.MinimapLocal(nx, ny, gb.Left, gb.Top, ox, oy, sc);
                    var r = new Rect(l, t,
                        WorkflowSurfaceMath.MinThumbSize(nw, sc, 2.0),
                        WorkflowSurfaceMath.MinThumbSize(nh, sc, 2.0));
                    dc.DrawRoundedRectangle(NodeBrush, null, r, ncr, ncr);
                }
            }

            // Viewport indicator
            var vp = _lastViewport;
            if (!vp.IsEmpty)
            {
                var (vpx, vpy, vpw, vph) = WorkflowSurfaceMath.MinimapViewportRect(
                    ox, oy, sc, vp.Left, vp.Top, vp.Width, vp.Height, gb.Left, gb.Top, mmW, mmH, minRectSize: 2.0);
                var vr = new Rect(vpx, vpy, vpw, vph);
                var ncr = Math.Max(0, NodeCornerRadius);
                if (ViewportFill is not null)
                    dc.DrawRoundedRectangle(ViewportFill, null, vr, ncr, ncr);
                if (ViewportStroke is not null)
                    dc.DrawRoundedRectangle(null, new Pen(ViewportStroke, ViewportStrokeThickness), vr, ncr, ncr);
            }
        }
        finally
        {
            dc.Pop();
        }
    }
}
