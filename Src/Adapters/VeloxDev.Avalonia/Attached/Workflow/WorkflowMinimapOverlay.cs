using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A minimap overlay positioned in the top-right corner of the workflow container.
/// Renders a thumbnail overview of all nodes (rectangles), links (lines),
/// and the current visible viewport (highlighted frame).
/// 
/// Only occupies the minimap panel area (top-right), so it never obscures
/// the rulers or coordinate grid.
/// Supports toggling visibility and dragging the viewport indicator rectangle.
/// </summary>
public class WorkflowMinimapOverlay : Control
{
    // ── Styled properties ────────────────────────────────────────────────────

    public static readonly StyledProperty<double> ScrollOffsetXProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ScrollOffsetX));

    public static readonly StyledProperty<double> ScrollOffsetYProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ScrollOffsetY));

    public static readonly StyledProperty<double> ContentOffsetXProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ContentOffsetX));

    public static readonly StyledProperty<double> ContentOffsetYProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ContentOffsetY));

    /// <summary>The workflow tree view model to visualize.</summary>
    public static readonly StyledProperty<IWorkflowTreeViewModel?> WorkflowTreeProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, IWorkflowTreeViewModel?>(nameof(WorkflowTree));

    /// <summary>The visible container width (for viewport computation).</summary>
    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ViewportWidth));

    /// <summary>The visible container height (for viewport computation).</summary>
    public static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ViewportHeight));

    /// <summary>Whether the minimap is visible.</summary>
    public static readonly StyledProperty<bool> IsMinimapVisibleProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, bool>(nameof(IsMinimapVisible), true);

    /// <summary>Name of the ScrollViewer to navigate when the viewport rect is dragged.</summary>
    public static readonly StyledProperty<string> ScrollViewerNameProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, string>(nameof(ScrollViewerName), string.Empty);

    /// <summary>
    /// Height of the ruler area (at top/left) drawn by the grid decorator.
    /// The minimap uses this to position itself below the ruler.
    /// Default 28 matches <c>WorkflowGridDecorator.RulerThickness</c>.
    /// </summary>
    public static readonly StyledProperty<double> RulerThicknessProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(RulerThickness), 28d);

    public static readonly StyledProperty<double> MinimapWidthProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(MinimapWidth), 200d);

    public static readonly StyledProperty<double> MinimapHeightProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(MinimapHeight), 140d);

    public static readonly StyledProperty<double> MinimapMarginProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(MinimapMargin), 8d);

    public static readonly StyledProperty<double> LinkStrokeThicknessProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(LinkStrokeThickness), 2.0);

    // ── Brushes ──────────────────────────────────────────────────────────────

    public static readonly StyledProperty<IBrush?> MinimapBackgroundProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, IBrush?>(nameof(MinimapBackground),
            new SolidColorBrush(Color.FromArgb(210, 20, 25, 34)));

    public static readonly StyledProperty<IBrush?> MinimapBorderBrushProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, IBrush?>(nameof(MinimapBorderBrush),
            new SolidColorBrush(Color.FromArgb(220, 148, 163, 184)));

    public static readonly StyledProperty<IBrush?> NodeBrushProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, IBrush?>(nameof(NodeBrush),
            new SolidColorBrush(Color.FromArgb(220, 56, 189, 248)));

    public static readonly StyledProperty<IBrush?> LinkBrushProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, IBrush?>(nameof(LinkBrush),
            new SolidColorBrush(Color.FromArgb(180, 180, 200, 220)));

    public static readonly StyledProperty<IBrush?> ViewportStrokeProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, IBrush?>(nameof(ViewportStroke),
            new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)));

    public static readonly StyledProperty<IBrush?> ViewportFillProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, IBrush?>(nameof(ViewportFill),
            new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)));

    public static readonly StyledProperty<double> ViewportStrokeThicknessProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ViewportStrokeThickness), 1.5);

    /// <summary>Corner radius for the minimap background and border rectangles.</summary>
    public static readonly StyledProperty<double> MinimapCornerRadiusProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(MinimapCornerRadius), 4d);

    /// <summary>Border stroke thickness for the minimap outline.</summary>
    public static readonly StyledProperty<double> MinimapBorderThicknessProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(MinimapBorderThickness), 1d);

    /// <summary>Corner radius for node and viewport indicator rectangles inside the minimap.</summary>
    public static readonly StyledProperty<double> NodeCornerRadiusProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(NodeCornerRadius), 1d);

    /// <summary>Content padding in device-independent pixels inside the minimap area.</summary>
    public static readonly StyledProperty<double> ContentPaddingProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ContentPadding), 2d);

    /// <summary>Minimum size clamp for the minimap (both width and height) to prevent degenerate rendering.</summary>
    public static readonly StyledProperty<double> MinimapMinSizeProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(MinimapMinSize), 20d);

    // ── CLR accessors ────────────────────────────────────────────────────────

    public double ScrollOffsetX { get => GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public IWorkflowTreeViewModel? WorkflowTree { get => GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public double ViewportWidth { get => GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public bool IsMinimapVisible { get => GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }
    public double MinimapWidth { get => GetValue(MinimapWidthProperty); set => SetValue(MinimapWidthProperty, value); }
    public double MinimapHeight { get => GetValue(MinimapHeightProperty); set => SetValue(MinimapHeightProperty, value); }
    public double MinimapMargin { get => GetValue(MinimapMarginProperty); set => SetValue(MinimapMarginProperty, value); }
    public double LinkStrokeThickness { get => GetValue(LinkStrokeThicknessProperty); set => SetValue(LinkStrokeThicknessProperty, value); }
    public IBrush? MinimapBackground { get => GetValue(MinimapBackgroundProperty); set => SetValue(MinimapBackgroundProperty, value); }
    public IBrush? MinimapBorderBrush { get => GetValue(MinimapBorderBrushProperty); set => SetValue(MinimapBorderBrushProperty, value); }
    public IBrush? NodeBrush { get => GetValue(NodeBrushProperty); set => SetValue(NodeBrushProperty, value); }
    public IBrush? LinkBrush { get => GetValue(LinkBrushProperty); set => SetValue(LinkBrushProperty, value); }
    public IBrush? ViewportStroke { get => GetValue(ViewportStrokeProperty); set => SetValue(ViewportStrokeProperty, value); }
    public IBrush? ViewportFill { get => GetValue(ViewportFillProperty); set => SetValue(ViewportFillProperty, value); }
    public double ViewportStrokeThickness { get => GetValue(ViewportStrokeThicknessProperty); set => SetValue(ViewportStrokeThicknessProperty, value); }
    public double RulerThickness { get => GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }
    public double MinimapCornerRadius { get => GetValue(MinimapCornerRadiusProperty); set => SetValue(MinimapCornerRadiusProperty, value); }
    public double MinimapBorderThickness { get => GetValue(MinimapBorderThicknessProperty); set => SetValue(MinimapBorderThicknessProperty, value); }
    public double NodeCornerRadius { get => GetValue(NodeCornerRadiusProperty); set => SetValue(NodeCornerRadiusProperty, value); }
    public double ContentPadding { get => GetValue(ContentPaddingProperty); set => SetValue(ContentPaddingProperty, value); }
    public double MinimapMinSize { get => GetValue(MinimapMinSizeProperty); set => SetValue(MinimapMinSizeProperty, value); }
    public string ScrollViewerName { get => GetValue(ScrollViewerNameProperty); set => SetValue(ScrollViewerNameProperty, value); }

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
            var left = Math.Min(a.Left, b.Left);
            var top = Math.Min(a.Top, b.Top);
            var right = Math.Max(a.Right, b.Right);
            var bottom = Math.Max(a.Bottom, b.Bottom);
            return new BoundsRect { Left = left, Top = top, Width = right - left, Height = bottom - top };
        }

        public static BoundsRect FromNode(double x, double y, double w, double h)
            => new() { Left = x, Top = y, Width = w, Height = h };
    }

    // ── Cached state ─────────────────────────────────────────────────────────

    private BoundsRect _lastGlobalBounds;
    private readonly List<(double X, double Y, double W, double H)> _lastNodeRects = [];
    private readonly List<(double X1, double Y1, double X2, double Y2)> _lastLinkEndpoints = [];
    private BoundsRect _lastViewport;
    private bool _pendingRefresh = true;
    private bool _isDragging;
    private double _dragOffsetX, _dragOffsetY;

    static WorkflowMinimapOverlay()
    {
        AffectsRender<WorkflowMinimapOverlay>(
            ScrollOffsetXProperty, ScrollOffsetYProperty,
            ContentOffsetXProperty, ContentOffsetYProperty,
            WorkflowTreeProperty,
            ViewportWidthProperty, ViewportHeightProperty,
            IsMinimapVisibleProperty,
            MinimapWidthProperty, MinimapHeightProperty, MinimapMarginProperty, MinimapMinSizeProperty,
            MinimapBackgroundProperty, MinimapBorderBrushProperty, MinimapCornerRadiusProperty,
            MinimapBorderThicknessProperty,
            NodeBrushProperty, NodeCornerRadiusProperty,
            LinkBrushProperty, LinkStrokeThicknessProperty,
            ViewportStrokeProperty, ViewportFillProperty, ViewportStrokeThicknessProperty,
            ContentPaddingProperty,
            RulerThicknessProperty);
    }

    public WorkflowMinimapOverlay()
    {
        Width = MinimapWidth;
        Height = MinimapHeight;
    }

    // ── Lifetime ─────────────────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToTree(WorkflowTree);

        // Resolve ScrollViewer by recursively searching from the visual root.
        // FindControl only searches descendants, not the whole tree, so we
        // search by walking the full visual tree from the root.
        if (!string.IsNullOrWhiteSpace(ScrollViewerName))
        {
            var root = this.GetVisualRoot() as Visual;
            if (root is not null)
                _scrollViewer = FindByName<ScrollViewer>(root, ScrollViewerName);
        }

        _pendingRefresh = true;
        InvalidateVisual();
    }

    private static T? FindByName<T>(Visual root, string name) where T : Control
    {
        if (root is T match && match.Name == name)
            return match;

        foreach (var child in root.GetVisualChildren())
        {
            var found = FindByName<T>(child, name);
            if (found is not null)
                return found;
        }
        return null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromTree();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WorkflowTreeProperty)
            SubscribeToTree(change.NewValue as IWorkflowTreeViewModel);

        if (change.Property == WorkflowTreeProperty ||
            change.Property == ScrollOffsetXProperty ||
            change.Property == ScrollOffsetYProperty ||
            change.Property == ContentOffsetXProperty ||
            change.Property == ContentOffsetYProperty ||
            change.Property == ViewportWidthProperty ||
            change.Property == ViewportHeightProperty)
        {
            _pendingRefresh = true;
            InvalidateVisual();
        }
    }

    // ── Node & slot change tracking (real-time updates) ──────────────────

    private IWorkflowTreeViewModel? _subscribedTree;
    // Track slot anchor subscription so we can unsubscribe properly.
    // We use ConditionalWeakTable-style management via per-link tracking.
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private ScrollViewer? _scrollViewer;

    private void SubscribeToTree(IWorkflowTreeViewModel? tree)
    {
        UnsubscribeFromTree();
        _subscribedTree = tree;
        if (tree is null) return;

        if (tree.Nodes is INotifyCollectionChanged nodesCc)
        {
            nodesCc.CollectionChanged += OnNodesCollectionChanged;
            foreach (var node in tree.Nodes)
                SubscribeNodeEvents(node);
        }

        if (tree.Links is INotifyCollectionChanged linksCc)
        {
            linksCc.CollectionChanged += OnLinksCollectionChanged;
            foreach (var link in tree.Links)
                SubscribeLinkSlotEvents(link);
        }
    }

    private void UnsubscribeFromTree()
    {
        if (_subscribedTree is null) return;

        if (_subscribedTree.Nodes is INotifyCollectionChanged nodesCc)
            nodesCc.CollectionChanged -= OnNodesCollectionChanged;
        foreach (var node in _subscribedNodes)
            UnsubscribeNodeEvents(node);
        _subscribedNodes.Clear();

        if (_subscribedTree.Links is INotifyCollectionChanged linksCc)
            linksCc.CollectionChanged -= OnLinksCollectionChanged;
        foreach (var link in _subscribedLinks)
            UnsubscribeLinkSlotEvents(link);
        _subscribedLinks.Clear();

        _subscribedTree = null;
    }

    private void SubscribeNodeEvents(IWorkflowNodeViewModel node)
    {
        if (!_subscribedNodes.Add(node)) return;
        if (node is INotifyPropertyChanged npc)
            npc.PropertyChanged += OnNodePropertyChanged;
    }

    private void UnsubscribeNodeEvents(IWorkflowNodeViewModel node)
    {
        if (_subscribedNodes.Remove(node) && node is INotifyPropertyChanged npc)
            npc.PropertyChanged -= OnNodePropertyChanged;
    }

    private void SubscribeLinkSlotEvents(IWorkflowLinkViewModel link)
    {
        if (!_subscribedLinks.Add(link)) return;
        if (link.Sender is INotifyPropertyChanged snpc)
            snpc.PropertyChanged += OnSlotPropertyChanged;
        if (link.Receiver is INotifyPropertyChanged rnpc)
            rnpc.PropertyChanged += OnSlotPropertyChanged;
    }

    private void UnsubscribeLinkSlotEvents(IWorkflowLinkViewModel link)
    {
        if (_subscribedLinks.Remove(link))
        {
            if (link.Sender is INotifyPropertyChanged snpc)
                snpc.PropertyChanged -= OnSlotPropertyChanged;
            if (link.Receiver is INotifyPropertyChanged rnpc)
                rnpc.PropertyChanged -= OnSlotPropertyChanged;
        }
    }

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (var item in e.NewItems)
                if (item is IWorkflowNodeViewModel node)
                    SubscribeNodeEvents(node);
        if (e.OldItems is not null)
            foreach (var item in e.OldItems)
                if (item is IWorkflowNodeViewModel node)
                    UnsubscribeNodeEvents(node);
        InstanceMarkDirty();
    }

    private void OnLinksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (var item in e.NewItems)
                if (item is IWorkflowLinkViewModel link)
                    SubscribeLinkSlotEvents(link);
        if (e.OldItems is not null)
            foreach (var item in e.OldItems)
                if (item is IWorkflowLinkViewModel link)
                    UnsubscribeLinkSlotEvents(link);
        InstanceMarkDirty();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
            InstanceMarkDirty();
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor))
            InstanceMarkDirty();
    }

    private void InstanceMarkDirty()
    {
        _pendingRefresh = true;
        if (IsVisible) InvalidateVisual();
    }

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

        _lastLinkEndpoints.Clear();
        if (tree.Links is not null)
            foreach (var link in tree.Links)
                if (link.Sender?.Anchor is Anchor sa && link.Receiver?.Anchor is Anchor ra)
                    _lastLinkEndpoints.Add((sa.Horizontal, sa.Vertical, ra.Horizontal, ra.Vertical));

        // Viewport in world coordinates — use bindable ViewportWidth/Height
        var vw = Math.Max(1, ViewportWidth);
        var vh = Math.Max(1, ViewportHeight);
        _lastViewport = BoundsRect.FromNode(ScrollOffsetX - ContentOffsetX, ScrollOffsetY - ContentOffsetY, vw, vh);
    }

    private void ClearCache()
    {
        _lastNodeRects.Clear();
        _lastLinkEndpoints.Clear();
        _lastGlobalBounds = default;
        _lastViewport = default;
    }

    // ── Pointer / drag: only on the viewport indicator rect ──────────────────

    private Rect? GetViewportRectInMinimap()
    {
        var vp = _lastViewport;
        var gb = _lastGlobalBounds;
        if (vp.IsEmpty || gb.IsEmpty) return null;

        var (ox, oy, mmW, mmH, sc) = ComputeTransform(gb);
        if (sc <= 0) return null;

        var left = ox + (vp.Left - gb.Left) * sc;
        var top = oy + (vp.Top - gb.Top) * sc;
        var w = Math.Max(2.0, vp.Width * sc);
        var h = Math.Max(2.0, vp.Height * sc);

        // Clamp inside minimap content area (0,0..mmW,mmH)
        left = Math.Max(0, Math.Min(mmW - w, left));
        top = Math.Max(0, Math.Min(mmH - h, top));

        return new Rect(left, top, w, h);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_isDragging) return;

        var pt = e.GetPosition(this);
        var vpRect = GetViewportRectInMinimap();

        if (vpRect is null || !vpRect.Value.Contains(pt)) return;

        _dragOffsetX = pt.X - (vpRect.Value.X + vpRect.Value.Width / 2);
        _dragOffsetY = pt.Y - (vpRect.Value.Y + vpRect.Value.Height / 2);

        NavigateToWorld(pt.X - _dragOffsetX, pt.Y - _dragOffsetY);

        _isDragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;

        var pt = e.GetPosition(this);
        NavigateToWorld(pt.X - _dragOffsetX, pt.Y - _dragOffsetY);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    // ── Shared transform ─────────────────────────────────────────────────────

    private (double OriginX, double OriginY, double MmW, double MmH, double Scale) ComputeTransform(BoundsRect globalBounds)
    {
        var margin = Math.Max(0, MinimapMargin);
        var minSize = Math.Max(1, MinimapMinSize);
        var mmW = Math.Max(minSize, Math.Min(MinimapWidth, Bounds.Width - margin * 2));
        var mmH = Math.Max(minSize, Math.Min(MinimapHeight, Bounds.Height - margin * 2));
        var pad = Math.Max(0, ContentPadding);
        var drawW = mmW - pad * 2;
        var drawH = mmH - pad * 2;
        var sc = Math.Min(drawW / Math.Max(1, globalBounds.Width), drawH / Math.Max(1, globalBounds.Height));
        var sw = globalBounds.Width * sc;
        var sh = globalBounds.Height * sc;
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
            var maxH = Math.Max(0, _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width);
            var maxV = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);

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

            _scrollViewer.Offset = new Vector(
                Math.Max(0, Math.Min(scrollX, maxH)),
                Math.Max(0, Math.Min(scrollY, maxV)));
        }
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!IsMinimapVisible) return;

        if (_pendingRefresh) RefreshMinimapData();

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (WorkflowTree?.Nodes is null || WorkflowTree.Nodes.Count == 0) return;

        var margin = Math.Max(0, MinimapMargin);
        var minSize = Math.Max(1, MinimapMinSize);
        var mmW = Math.Max(minSize, Math.Min(MinimapWidth, bounds.Width - margin * 2));
        var mmH = Math.Max(minSize, Math.Min(MinimapHeight, bounds.Height - margin * 2));
        var mmRect = new Rect(0, 0, mmW, mmH);
        var cr = (float)Math.Max(0, MinimapCornerRadius);

        // Background with rounded corners
        if (MinimapBackground is not null)
            context.FillRectangle(MinimapBackground, mmRect, cr);
        if (MinimapBorderBrush is not null)
            context.DrawRectangle(null, new Pen(MinimapBorderBrush, MinimapBorderThickness), mmRect, cr);

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

        using (context.PushClip(mmRect))
        {
            // Links (under nodes) — wider stroke for better visibility
            if (LinkBrush is not null && _lastLinkEndpoints.Count > 0)
            {
                var lp = new Pen(LinkBrush, LinkStrokeThickness);
                foreach (var (x1, y1, x2, y2) in _lastLinkEndpoints)
                    context.DrawLine(lp,
                        new Point(ox + (x1 - gb.Left) * sc, oy + (y1 - gb.Top) * sc),
                        new Point(ox + (x2 - gb.Left) * sc, oy + (y2 - gb.Top) * sc));
            }

            // Nodes
            if (NodeBrush is not null)
            {
                var ncr = (float)Math.Max(0, NodeCornerRadius);
                foreach (var (nx, ny, nw, nh) in _lastNodeRects)
                    context.FillRectangle(NodeBrush,
                        new Rect(ox + (nx - gb.Left) * sc, oy + (ny - gb.Top) * sc,
                                 Math.Max(2.0, nw * sc), Math.Max(2.0, nh * sc)), ncr);
            }

            // Viewport indicator
            var vp = _lastViewport;
            if (!vp.IsEmpty)
            {
                var vpx = ox + (vp.Left - gb.Left) * sc;
                var vpy = oy + (vp.Top - gb.Top) * sc;
                var vpw = Math.Max(2.0, vp.Width * sc);
                var vph = Math.Max(2.0, vp.Height * sc);

                vpx = Math.Max(mmRect.X, Math.Min(mmRect.Right - vpw, vpx));
                vpy = Math.Max(mmRect.Y, Math.Min(mmRect.Bottom - vph, vpy));

                var vr = new Rect(vpx, vpy, vpw, vph);
                var ncr = (float)Math.Max(0, NodeCornerRadius);
                if (ViewportFill is not null)
                    context.FillRectangle(ViewportFill, vr, ncr);
                if (ViewportStroke is not null)
                    context.DrawRectangle(null, new Pen(ViewportStroke, ViewportStrokeThickness), vr, ncr);
            }
        }
    }
}
