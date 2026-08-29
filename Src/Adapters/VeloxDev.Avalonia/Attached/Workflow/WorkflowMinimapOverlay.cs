using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
public class WorkflowMinimapOverlay : Control, IWorkflowMinimapOverlay
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

    // ── Cached state ─────────────────────────────────────────────────────────

    private WorkflowBounds _lastGlobalBounds;
    private readonly List<(double X, double Y, double W, double H)> _lastNodeRects = [];
    private WorkflowBounds _lastViewport;
    private bool _pendingRefresh = true;
    private bool _isDragging;

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
        // NOTE: VisualExtensions.GetVisualRoot() was removed in Avalonia 12 and
        // e.Root's return type changed (IRenderRoot in 11, Visual in 12), so walk
        // up via GetVisualParent() whose signature is identical in both 11.x and 12.x.
        if (!string.IsNullOrWhiteSpace(ScrollViewerName))
        {
            Visual root = this;
            while (root.GetVisualParent() is { } parent)
                root = parent;
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
        if (!IsVisible) return;

        // OnNodePropChanged can fire from the MonoBehaviourManager loop thread.
        // InvalidateVisual requires the UI thread — dispatch if needed.
        if (Dispatcher.UIThread.CheckAccess())
            InvalidateVisual();
        else
            Dispatcher.UIThread.Post(InvalidateVisual);
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

        _lastNodeRects.Clear();

        if (tree.Nodes is not null)
            foreach (var node in tree.Nodes)
            {
                var (nx, ny, nw, nh) = (node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height);
                _lastNodeRects.Add((nx, ny, nw, nh));
            }

        _lastGlobalBounds = WorkflowBounds.FromNodes(_lastNodeRects);

        // Viewport in world coordinates — use bindable ViewportWidth/Height
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

    // ── Pointer / drag: only on the viewport indicator rect ──────────────────

    private Rect? GetViewportRectInMinimap()
    {
        var vp = _lastViewport;
        var gb = _lastGlobalBounds;
        if (vp.IsEmpty || gb.IsEmpty) return null;

        var (ox, oy, mmW, mmH, sc) = ComputeTransform(gb);
        if (sc <= 0) return null;

        var (l, t, w, h) = WorkflowSurfaceMath.MinimapViewportRect(
            ox, oy, sc, vp.Left, vp.Top, vp.Width, vp.Height, gb.Left, gb.Top, mmW, mmH, minRectSize: 2.0);
        return new Rect(l, t, w, h);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_isDragging) return;

        var pt = e.GetPosition(this);

        // Match the Jalium adapter: the clicked point always becomes the viewport center —
        // no grab-anchor on the indicator block, so pressing anywhere recenters the view.
        NavigateToWorld(pt.X, pt.Y);

        _isDragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;

        var pt = e.GetPosition(this);
        NavigateToWorld(pt.X, pt.Y);
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

    private (double OriginX, double OriginY, double MmW, double MmH, double Scale) ComputeTransform(WorkflowBounds globalBounds)
    {
        var margin = Math.Max(0, MinimapMargin);
        var minSize = Math.Max(1, MinimapMinSize);
        var mmW = Math.Max(minSize, Math.Min(MinimapWidth, Bounds.Width - margin * 2));
        var mmH = Math.Max(minSize, Math.Min(MinimapHeight, Bounds.Height - margin * 2));
        var pad = Math.Max(0, ContentPadding);
        var drawW = mmW - pad * 2;
        var drawH = mmH - pad * 2;
        var (ox, oy, sc) = WorkflowSurfaceMath.MinimapFit(
            globalBounds.Width, globalBounds.Height, drawW, drawH, pad);
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
            var maxH = WorkflowSurfaceMath.ScrollMax(_scrollViewer.Extent.Width, _scrollViewer.Viewport.Width);
            var maxV = WorkflowSurfaceMath.ScrollMax(_scrollViewer.Extent.Height, _scrollViewer.Viewport.Height);

            scrollX = WorkflowSurfaceMath.ClampScrollOffset(scrollX, maxH, layout, horizontal: true);
            scrollY = WorkflowSurfaceMath.ClampScrollOffset(scrollY, maxV, layout, horizontal: false);

            _scrollViewer.Offset = new Vector(
                WorkflowSurfaceMath.ClampValue(scrollX, 0, maxH),
                WorkflowSurfaceMath.ClampValue(scrollY, 0, maxV));
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
        var (ox, oy, sc) = WorkflowSurfaceMath.MinimapFit(gb.Width, gb.Height, drawW, drawH, pad);

        using (context.PushClip(mmRect))
        {
            // Nodes
            if (NodeBrush is not null)
            {
                var ncr = (float)Math.Max(0, NodeCornerRadius);
                foreach (var (nx, ny, nw, nh) in _lastNodeRects)
                {
                    var (l, t) = WorkflowSurfaceMath.MinimapLocal(nx, ny, gb.Left, gb.Top, ox, oy, sc);
                    context.FillRectangle(NodeBrush,
                        new Rect(l, t,
                            WorkflowSurfaceMath.MinThumbSize(nw, sc, 2.0),
                            WorkflowSurfaceMath.MinThumbSize(nh, sc, 2.0)), ncr);
                }
            }

            // Viewport indicator
            var vp = _lastViewport;
            if (!vp.IsEmpty)
            {
                var (vpx, vpy, vpw, vph) = WorkflowSurfaceMath.MinimapViewportRect(
                    ox, oy, sc, vp.Left, vp.Top, vp.Width, vp.Height, gb.Left, gb.Top, mmW, mmH, minRectSize: 2.0);
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
