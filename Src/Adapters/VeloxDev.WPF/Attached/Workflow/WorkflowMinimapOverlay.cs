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
    private double _dragOffsetX, _dragOffsetY;
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
        if (IsVisible) InvalidateVisual();
    }

    // ── Data refresh ─────────────────────────────────────────────────────────

    private void RefreshMinimapData()
    {
        _pendingRefresh = false;
        var tree = WorkflowTree;
        if (tree is null) { ClearCache(); return; }

        var globalBounds = default(BoundsRect);
        _lastNodeRects.Clear();
        bool first = true;

        if (tree.Nodes is not null)
            foreach (var node in tree.Nodes)
            {
                var (nx, ny, nw, nh) = (node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height);
                _lastNodeRects.Add((nx, ny, nw, nh));
                var nr = BoundsRect.FromNode(nx, ny, nw, nh);
                if (first) { globalBounds = nr; first = false; }
                else globalBounds = BoundsRect.Union(globalBounds, nr);
            }

        _lastGlobalBounds = globalBounds;

        var vw = Math.Max(1, ViewportWidth);
        var vh = Math.Max(1, ViewportHeight);
        _lastViewport = BoundsRect.FromNode(ScrollOffsetX - ContentOffsetX, ScrollOffsetY - ContentOffsetY, vw, vh);
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

        var l = ox + (vp.Left - gb.Left) * sc;
        var t = oy + (vp.Top - gb.Top) * sc;
        var w = Math.Max(2.0, vp.Width * sc);
        var h = Math.Max(2.0, vp.Height * sc);
        l = Math.Max(0, Math.Min(mmW - w, l));
        t = Math.Max(0, Math.Min(mmH - h, t));
        return new Rect(l, t, w, h);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        var pt = e.GetPosition(this);
        var vpRect = GetViewportRectInMinimap();
        if (vpRect is null || !vpRect.Value.Contains(pt)) return;

        _dragOffsetX = pt.X - (vpRect.Value.X + vpRect.Value.Width / 2);
        _dragOffsetY = pt.Y - (vpRect.Value.Y + vpRect.Value.Height / 2);
        NavigateToWorld(pt.X - _dragOffsetX, pt.Y - _dragOffsetY);
        _isDragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pt = e.GetPosition(this);
        NavigateToWorld(pt.X - _dragOffsetX, pt.Y - _dragOffsetY);
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

    private (double Ox, double Oy, double MmW, double MmH, double Sc) ComputeTransform(BoundsRect gb)
    {
        var margin = 0.0; // WPF margin is handled by Grid alignment
        var minSz = Math.Max(1, MinimapMinSize);
        var mmW = Math.Max(minSz, Math.Min(MinimapWidth, RenderSize.Width - margin * 2));
        var mmH = Math.Max(minSz, Math.Min(MinimapHeight, RenderSize.Height - margin * 2));
        var pad = Math.Max(0, ContentPadding);
        var drawW = mmW - pad * 2;
        var drawH = mmH - pad * 2;
        var sc = Math.Min(drawW / Math.Max(1, gb.Width), drawH / Math.Max(1, gb.Height));
        var sw = gb.Width * sc;
        var sh = gb.Height * sc;
        return (pad + (drawW - sw) / 2, pad + (drawH - sh) / 2, mmW, mmH, sc);
    }

    private void NavigateToWorld(double adjX, double adjY)
    {
        var gb = _lastGlobalBounds;
        if (gb.IsEmpty) return;
        var (ox, oy, _, _, sc) = ComputeTransform(gb);
        if (sc <= 0) return;

        var wcx = (adjX - ox) / sc + gb.Left;
        var wcy = (adjY - oy) / sc + gb.Top;
        var scrollX = (wcx - ViewportWidth / 2) + ContentOffsetX;
        var scrollY = (wcy - ViewportHeight / 2) + ContentOffsetY;

        if (_scrollViewer is not null && WorkflowTree?.Layout is { } layout)
        {
            var maxH = Math.Max(0, _scrollViewer.ScrollableWidth);
            var maxV = Math.Max(0, _scrollViewer.ScrollableHeight);

            if (scrollX < 0)
            {
                layout.NegativeOffset = new Offset(layout.NegativeOffset.Horizontal + (-scrollX), layout.NegativeOffset.Vertical);
                scrollX = 0;
            }
            else if (scrollX > maxH)
            {
                layout.PositiveOffset = new Offset(layout.PositiveOffset.Horizontal + (scrollX - maxH), layout.PositiveOffset.Vertical);
                scrollX = maxH;
            }

            if (scrollY < 0)
            {
                layout.NegativeOffset = new Offset(layout.NegativeOffset.Horizontal, layout.NegativeOffset.Vertical + (-scrollY));
                scrollY = 0;
            }
            else if (scrollY > maxV)
            {
                layout.PositiveOffset = new Offset(layout.PositiveOffset.Horizontal, layout.PositiveOffset.Vertical + (scrollY - maxV));
                scrollY = maxV;
            }

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
        var drawW = mmW - pad * 2;
        var drawH = mmH - pad * 2;
        var sc = Math.Min(drawW / gb.Width, drawH / gb.Height);
        var sw = gb.Width * sc;
        var sh = gb.Height * sc;
        var ox = pad + (drawW - sw) / 2;
        var oy = pad + (drawH - sh) / 2;

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
                    var r = new Rect(
                        ox + (nx - gb.Left) * sc, oy + (ny - gb.Top) * sc,
                        Math.Max(2.0, nw * sc), Math.Max(2.0, nh * sc));
                    dc.DrawRoundedRectangle(NodeBrush, null, r, ncr, ncr);
                }
            }

            // Viewport indicator
            var vp = _lastViewport;
            if (!vp.IsEmpty)
            {
                var vpx = ox + (vp.Left - gb.Left) * sc;
                var vpy = oy + (vp.Top - gb.Top) * sc;
                var vpw = Math.Max(2.0, vp.Width * sc);
                var vph = Math.Max(2.0, vp.Height * sc);
                vpx = Math.Max(0, Math.Min(mmW - vpw, vpx));
                vpy = Math.Max(0, Math.Min(mmH - vph, vpy));
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
