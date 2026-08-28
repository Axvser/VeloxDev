using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Self-contained minimap. Content-fit: maps the bounding box of all node cards into
/// the minimap (nodes only, 2× padding, centered), draws a translucent white viewport rect
/// clamped inside the minimap, and drag-to-pans by reusing the surface's edge-aware navigation
/// (grows Layout.Positive/NegativeOffset when dragged past an edge).</summary>
public class WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay
{
    private const double ContentPad = 8;

    private static readonly SolidColorBrush s_bg = new(Color.FromArgb(0xD2, 0x14, 0x19, 0x22));
    private static readonly SolidColorBrush s_border = new(Color.FromArgb(0xDC, 0x94, 0xA3, 0xB8));
    private static readonly SolidColorBrush s_node = new(Color.FromArgb(0xDC, 0x38, 0xBD, 0xF8));
    private static readonly SolidColorBrush s_viewportFill = new(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush s_viewportStroke = new(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
    private static readonly Pen s_viewportPen = new(s_viewportStroke, 1);

    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        "ScrollOffsetX", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        "ScrollOffsetY", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        "ContentOffsetX", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        "ContentOffsetY", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        "ViewportWidth", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        "ViewportHeight", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty IsMinimapVisibleProperty = DependencyProperty.Register(
        "IsMinimapVisible", typeof(bool), typeof(WorkflowMinimapOverlay), new PropertyMetadata(true, OnVisualChanged));
    public static readonly DependencyProperty WorkflowTreeProperty = DependencyProperty.Register(
        "WorkflowTree", typeof(IWorkflowTreeViewModel), typeof(WorkflowMinimapOverlay), new PropertyMetadata(null, OnTreeChanged));

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public bool IsMinimapVisible { get => (bool)GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }
    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }

    /// <summary>Assigned by the composing control (WorkflowTreeView) for drag-to-pan.</summary>
    public ScrollViewer? ScrollViewer { get; set; }

    private readonly Rect _bounds = new(0, 0, 1, 1);
    private IWorkflowTreeViewModel? _tree;
    private bool _dragging;

    public WorkflowMinimapOverlay()
    {
        Width = 200;
        Height = 140;
        ClipToBounds = true;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMiniMouseDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMiniMouseMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMiniMouseUp));
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.InvalidateVisual();
        }
    }

    private static void OnTreeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WorkflowMinimapOverlay overlay)
        {
            return;
        }

        overlay.UnsubscribeTree();
        overlay._tree = (IWorkflowTreeViewModel?)e.NewValue;
        overlay.SubscribeTree();
        overlay.InvalidateVisual();
    }

    private void SubscribeTree()
    {
        if (_tree is null)
        {
            return;
        }

        _tree.Nodes.CollectionChanged += OnNodesChanged;
        _tree.Links.CollectionChanged += OnLinksChanged;
        foreach (var node in _tree.Nodes)
        {
            SubscribeNode(node);
        }
    }

    private void UnsubscribeTree()
    {
        if (_tree is null)
        {
            return;
        }

        _tree.Nodes.CollectionChanged -= OnNodesChanged;
        _tree.Links.CollectionChanged -= OnLinksChanged;
        foreach (var node in _tree.Nodes)
        {
            UnsubscribeNode(node);
        }
    }

    private void SubscribeNode(IWorkflowNodeViewModel node)
    {
        if (node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += OnNodeChanged;
        }
    }

    private void UnsubscribeNode(IWorkflowNodeViewModel node)
    {
        if (node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged -= OnNodeChanged;
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is IWorkflowNodeViewModel node)
                {
                    UnsubscribeNode(node);
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is IWorkflowNodeViewModel node)
                {
                    SubscribeNode(node);
                }
            }
        }

        InvalidateVisual();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            InvalidateVisual();
        }
    }

    // ── Content-fit mapping ─────────────────────────────────────────────────

    private Rect ComputeBounds()
    {
        if (_tree is null || _tree.Nodes.Count == 0)
        {
            return _bounds;
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var node in _tree.Nodes)
        {
            minX = Math.Min(minX, node.Anchor.Horizontal);
            minY = Math.Min(minY, node.Anchor.Vertical);
            maxX = Math.Max(maxX, node.Anchor.Horizontal + node.Size.Width);
            maxY = Math.Max(maxY, node.Anchor.Vertical + node.Size.Height);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private (double Ox, double Oy, double Scale) ComputeTransform(Rect bounds)
    {
        double drawW = Math.Max(1, Width - ContentPad * 2);
        double drawH = Math.Max(1, Height - ContentPad * 2);
        return WorkflowSurfaceMath.MinimapFit(bounds.Width, bounds.Height, drawW, drawH, ContentPad);
    }

    private void PanToMini(Point mini)
    {
        if (_tree is null)
        {
            return;
        }

        var bounds = ComputeBounds();
        var (ox, oy, scale) = ComputeTransform(bounds);
        if (scale <= 0)
        {
            return;
        }

        var (wx, wy) = WorkflowSurfaceMath.MinimapToWorld(mini.X, mini.Y, ox, oy, scale, bounds.X, bounds.Y);
        NavigateToWorld(wx, wy);
    }

    /// <summary>Centers the view on a world point, growing the canvas if the target scroll runs past an edge.</summary>
    public void NavigateToWorld(double wx, double wy)
    {
        if (_tree is null || ScrollViewer is null)
        {
            return;
        }

        var layout = _tree.Layout;
        var maxH = Math.Max(0, ScrollViewer.ScrollableWidth);
        var maxV = Math.Max(0, ScrollViewer.ScrollableHeight);
        var (scrollX, scrollY) = WorkflowSurfaceMath.MinimapToScroll(
            wx, wy, ScrollViewer.ViewportWidth, ScrollViewer.ViewportHeight, ContentOffsetX, ContentOffsetY);
        scrollX = WorkflowSurfaceMath.ClampScrollOffset(scrollX, maxH, layout, horizontal: true);
        scrollY = WorkflowSurfaceMath.ClampScrollOffset(scrollY, maxV, layout, horizontal: false);
        ScrollViewer.ScrollToHorizontalOffset(Math.Max(0, Math.Min(scrollX, maxH)));
        ScrollViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(scrollY, maxV)));
    }

    // ── Mouse ──────────────────────────────────────────────────────────────

    private void OnMiniMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _dragging = true;
            CaptureMouse();
            PanToMini(e.GetPosition(this));
            e.Handled = true;
        }
    }

    private void OnMiniMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            PanToMini(e.GetPosition(this));
            e.Handled = true;
        }
    }

    private void OnMiniMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_dragging && e.ChangedButton == MouseButton.Left)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(s_bg, new Pen(s_border, 1), new Rect(0, 0, RenderSize.Width, RenderSize.Height), 4, 4);

        var bounds = ComputeBounds();
        var (ox, oy, scale) = ComputeTransform(bounds);
        if (scale <= 0)
        {
            return;
        }

        if (_tree is not null)
        {
            foreach (var node in _tree.Nodes)
            {
                var (lx, ly) = WorkflowSurfaceMath.MinimapLocal(node.Anchor.Horizontal, node.Anchor.Vertical, bounds.X, bounds.Y, ox, oy, scale);
                dc.DrawRoundedRectangle(s_node, null,
                    new Rect(lx, ly, Math.Max(1, node.Size.Width * scale), Math.Max(1, node.Size.Height * scale)), 2, 2);
            }
        }

        double worldLeft = WorkflowSurfaceMath.ToWorld(ScrollOffsetX, ContentOffsetX);
        double worldTop = WorkflowSurfaceMath.ToWorld(ScrollOffsetY, ContentOffsetY);
        var v = new Point(ox + (worldLeft - bounds.X) * scale, oy + (worldTop - bounds.Y) * scale);
        double vw = Math.Max(2, ViewportWidth * scale);
        double vh = Math.Max(2, ViewportHeight * scale);
        vw = Math.Min(vw, Width);
        vh = Math.Min(vh, Height);
        v.X = Math.Max(0, Math.Min(Width - vw, v.X));
        v.Y = Math.Max(0, Math.Min(Height - vh, v.Y));
        dc.DrawRectangle(s_viewportFill, s_viewportPen, new Rect(v.X, v.Y, vw, vh));
    }
}
