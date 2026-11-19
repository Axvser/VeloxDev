using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using PlatformInput = Jalium.UI.Input;
using Wf = VeloxDev.WorkflowSystem;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Self-contained minimap. Content-fit: maps the bounding box of all node cards into
/// the minimap (nodes only, 2× padding, centered), draws a translucent white viewport rect
/// clamped inside the minimap, and drag-to-pans by reusing the surface's edge-aware navigation
/// (grows Layout.Positive/NegativeOffset when dragged past an edge).</summary>
public class WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay
{

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

    public double ScrollOffsetX { get => Read(ScrollOffsetXProperty, 0.0); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => Read(ScrollOffsetYProperty, 0.0); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => Read(ContentOffsetXProperty, 0.0); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => Read(ContentOffsetYProperty, 0.0); set => SetValue(ContentOffsetYProperty, value); }
    /// <inheritdoc />
    public double RulerBand => 0;
    public double ViewportWidth { get => Read(ViewportWidthProperty, 0.0); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => Read(ViewportHeightProperty, 0.0); set => SetValue(ViewportHeightProperty, value); }
    public bool IsMinimapVisible { get => Read(IsMinimapVisibleProperty, true); set => SetValue(IsMinimapVisibleProperty, value); }
    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }

    // 值类型的附着属性都注册了非 null 默认值，所以取回来必是那个类型。`GetValue` 的返回类型是 `object?`，
    // 直接强转会让编译器报「取消装箱可能为 null」—— 与其到处写 `!`，不如把那个默认值在这里写出来，
    // 顺带让「DP 没注册默认值时取到什么」有定义。
    private T Read<T>(DependencyProperty property, T fallback) where T : struct
        => GetValue(property) is T value ? value : fallback;

    /// <summary>Assigned by the composing control for drag-to-pan, or resolved from <see cref="ScrollViewerName"/>.</summary>
    public ScrollViewer? ScrollViewer { get; set; }

    /// <summary>The minimap's width; drives <see cref="FrameworkElement.Width"/>.</summary>
    public static readonly DependencyProperty MinimapWidthProperty = DependencyProperty.Register(
        nameof(MinimapWidth), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(200d, OnMetricsChanged));

    /// <summary>The minimap's height; drives <see cref="FrameworkElement.Height"/>.</summary>
    public static readonly DependencyProperty MinimapHeightProperty = DependencyProperty.Register(
        nameof(MinimapHeight), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(140d, OnMetricsChanged));

    /// <summary>The smallest side a node thumbnail is drawn at, in minimap pixels.</summary>
    public static readonly DependencyProperty MinimapMinSizeProperty = DependencyProperty.Register(
        nameof(MinimapMinSize), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(1d, OnVisualChanged));

    /// <summary>The inset between the minimap's edge and the content it fits.</summary>
    public static readonly DependencyProperty ContentPaddingProperty = DependencyProperty.Register(
        nameof(ContentPadding), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(8d, OnVisualChanged));

    /// <summary>The minimap's fill.</summary>
    public static readonly DependencyProperty MinimapBackgroundProperty = DependencyProperty.Register(
        nameof(MinimapBackground), typeof(Brush), typeof(WorkflowMinimapOverlay),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0xD2, 0x14, 0x19, 0x22)), OnVisualChanged));

    /// <summary>The minimap's outline.</summary>
    public static readonly DependencyProperty MinimapBorderBrushProperty = DependencyProperty.Register(
        nameof(MinimapBorderBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0xDC, 0x94, 0xA3, 0xB8)), OnVisualChanged));

    /// <summary>The outline's width.</summary>
    public static readonly DependencyProperty MinimapBorderThicknessProperty = DependencyProperty.Register(
        nameof(MinimapBorderThickness), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(1d, OnVisualChanged));

    /// <summary>The minimap's corner radius.</summary>
    public static readonly DependencyProperty MinimapCornerRadiusProperty = DependencyProperty.Register(
        nameof(MinimapCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(4d, OnVisualChanged));

    /// <summary>The node thumbnails' fill.</summary>
    public static readonly DependencyProperty NodeBrushProperty = DependencyProperty.Register(
        nameof(NodeBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0xDC, 0x38, 0xBD, 0xF8)), OnVisualChanged));

    /// <summary>The node thumbnails' corner radius.</summary>
    public static readonly DependencyProperty NodeCornerRadiusProperty = DependencyProperty.Register(
        nameof(NodeCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(2d, OnVisualChanged));

    /// <summary>The viewport rectangle's fill.</summary>
    public static readonly DependencyProperty ViewportFillProperty = DependencyProperty.Register(
        nameof(ViewportFill), typeof(Brush), typeof(WorkflowMinimapOverlay),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), OnVisualChanged));

    /// <summary>The viewport rectangle's outline.</summary>
    public static readonly DependencyProperty ViewportStrokeProperty = DependencyProperty.Register(
        nameof(ViewportStroke), typeof(Brush), typeof(WorkflowMinimapOverlay),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)), OnVisualChanged));

    /// <summary>The viewport rectangle's outline width.</summary>
    public static readonly DependencyProperty ViewportStrokeThicknessProperty = DependencyProperty.Register(
        nameof(ViewportStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(1d, OnVisualChanged));

    /// <summary>The minimap's width.</summary>
    public double MinimapWidth { get => Read(MinimapWidthProperty, 200d); set => SetValue(MinimapWidthProperty, value); }

    /// <summary>The minimap's height.</summary>
    public double MinimapHeight { get => Read(MinimapHeightProperty, 140d); set => SetValue(MinimapHeightProperty, value); }

    /// <summary>The smallest side a node thumbnail is drawn at.</summary>
    public double MinimapMinSize { get => Read(MinimapMinSizeProperty, 1d); set => SetValue(MinimapMinSizeProperty, value); }

    /// <summary>The inset between the minimap's edge and the content it fits.</summary>
    public double ContentPadding { get => Read(ContentPaddingProperty, 8d); set => SetValue(ContentPaddingProperty, value); }

    /// <summary>The minimap's fill.</summary>
    public Brush MinimapBackground { get => GetValue(MinimapBackgroundProperty) as Brush ?? Brushes.Transparent; set => SetValue(MinimapBackgroundProperty, value); }

    /// <summary>The minimap's outline.</summary>
    public Brush MinimapBorderBrush { get => GetValue(MinimapBorderBrushProperty) as Brush ?? Brushes.Transparent; set => SetValue(MinimapBorderBrushProperty, value); }

    /// <summary>The outline's width.</summary>
    public double MinimapBorderThickness { get => Read(MinimapBorderThicknessProperty, 1d); set => SetValue(MinimapBorderThicknessProperty, value); }

    /// <summary>The minimap's corner radius.</summary>
    public double MinimapCornerRadius { get => Read(MinimapCornerRadiusProperty, 4d); set => SetValue(MinimapCornerRadiusProperty, value); }

    /// <summary>The node thumbnails' fill.</summary>
    public Brush NodeBrush { get => GetValue(NodeBrushProperty) as Brush ?? Brushes.Transparent; set => SetValue(NodeBrushProperty, value); }

    /// <summary>The node thumbnails' corner radius.</summary>
    public double NodeCornerRadius { get => Read(NodeCornerRadiusProperty, 2d); set => SetValue(NodeCornerRadiusProperty, value); }

    /// <summary>The viewport rectangle's fill.</summary>
    public Brush ViewportFill { get => GetValue(ViewportFillProperty) as Brush ?? Brushes.Transparent; set => SetValue(ViewportFillProperty, value); }

    /// <summary>The viewport rectangle's outline.</summary>
    public Brush ViewportStroke { get => GetValue(ViewportStrokeProperty) as Brush ?? Brushes.Transparent; set => SetValue(ViewportStrokeProperty, value); }

    /// <summary>The viewport rectangle's outline width.</summary>
    public double ViewportStrokeThickness { get => Read(ViewportStrokeThicknessProperty, 1d); set => SetValue(ViewportStrokeThicknessProperty, value); }

    /// <summary>
    /// The name of the <see cref="ScrollViewer"/> the minimap pans, resolved in the template's name scope.
    /// </summary>
    /// <remarks>
    /// Markup can name the viewer instead of handing over an object, which keeps the overlay declared purely in
    /// the template — the same contract the surface behavior uses for its own parts.
    /// </remarks>
    public static readonly DependencyProperty ScrollViewerNameProperty = DependencyProperty.Register(
        nameof(ScrollViewerName),
        typeof(string),
        typeof(WorkflowMinimapOverlay),
        new PropertyMetadata(null, OnScrollViewerNameChanged));

    /// <summary>The name of the <see cref="ScrollViewer"/> the minimap pans.</summary>
    public string? ScrollViewerName
    {
        get => (string?)GetValue(ScrollViewerNameProperty);
        set => SetValue(ScrollViewerNameProperty, value);
    }

    private static void OnScrollViewerNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkflowMinimapOverlay overlay)
        {
            overlay.ResolveScrollViewer();
        }
    }

    // 名字作用域要等到装进树里才建好，所以 Loaded 时再兜一次。
    private void ResolveScrollViewer()
    {
        if (ScrollViewerName is { Length: > 0 } name && FindName(name) is ScrollViewer viewer)
        {
            ScrollViewer = viewer;
        }
    }

    // 节点缩略框与内容包围盒的缓存：视口、颜色、尺寸的变化只影响那一趟 O(1) 的变换，不该让重画再走一遍节点表。
    // 只有节点自己动了（或树换了）才置脏重算 —— 与 WPF / Avalonia / WinUI / MAUI 的同名脏标记同一条规矩。
    private readonly List<(double X, double Y, double W, double H)> _nodeRects = [];
    private Rect _contentBounds = new(0, 0, 1, 1);
    private bool _pendingRefresh = true;
    private double _cachedScaleX = double.NaN;
    private double _cachedScaleY = double.NaN;
    private IWorkflowTreeViewModel? _tree;
    private bool _dragging;

    public WorkflowMinimapOverlay()
    {
        Width = MinimapWidth;
        Height = MinimapHeight;
        ClipToBounds = true;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMiniMouseDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMiniMouseMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMiniMouseUp));
        Loaded += (_, _) => ResolveScrollViewer();
    }

    // 尺寸 DP 是数据，元素自己的 Width/Height 是布局 —— 改前者要把后者带过去。
    private static void OnMetricsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WorkflowMinimapOverlay overlay)
        {
            return;
        }

        overlay.Width = overlay.MinimapWidth;
        overlay.Height = overlay.MinimapHeight;
        overlay.InvalidateVisual();
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
        overlay.MarkContentDirty();
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

        MarkContentDirty();
    }

    // 连线不参与内容适配（包围盒只看节点），所以只重画、不重算。
    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            MarkContentDirty();
        }
    }

    // ── 内容适配映射 ────────────────────────────────────────────────────────

    private void MarkContentDirty()
    {
        _pendingRefresh = true;
        InvalidateVisual();
    }

    // 重画与拖拽映射都从这里取内容：脏了、或缩放变了，就重算一趟（缩略框 + 包围盒一起拿齐），否则用上一趟的快照。
    private void EnsureContent()
    {
        // 节点位置是「锚点 ÷ 当前缩放」的折叠值，缩放一变它就变，而缩放**不会**让任何节点发 PropertyChanged ——
        // 所以这里必须连着比缩放，否则缩放之后小地图会继续按旧比例画（比一个脏标记值钱的一行）。
        var scale = _tree?.Layout?.Scale;
        var sx = scale?.Horizontal ?? 1d;
        var sy = scale?.Vertical ?? 1d;

        if (!_pendingRefresh && sx == _cachedScaleX && sy == _cachedScaleY)
        {
            return;
        }

        _pendingRefresh = false;
        _cachedScaleX = sx;
        _cachedScaleY = sy;
        _nodeRects.Clear();
        _contentBounds = new Rect(0, 0, 1, 1);

        if (_tree is null || _tree.Nodes.Count == 0)
        {
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var node in _tree.Nodes)
        {
            var (x, y, w, h) = (node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height);
            _nodeRects.Add((x, y, w, h));
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + w);
            maxY = Math.Max(maxY, y + h);
        }

        _contentBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private (double Ox, double Oy, double Scale) ComputeTransform(Rect bounds)
    {
        var padding = ContentPadding;
        double drawW = Math.Max(1, Width - padding * 2);
        double drawH = Math.Max(1, Height - padding * 2);
        return WorkflowSurfaceMath.MinimapFit(bounds.Width, bounds.Height, drawW, drawH, padding);
    }

    private void PanToMini(Point mini)
    {
        if (_tree is null)
        {
            return;
        }

        EnsureContent();
        var bounds = _contentBounds;
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
        ScrollViewer.ScrollToHorizontalOffset(WorkflowSurfaceMath.ClampValue(scrollX, 0, maxH));
        ScrollViewer.ScrollToVerticalOffset(WorkflowSurfaceMath.ClampValue(scrollY, 0, maxV));
    }

    // ── 鼠标 ────────────────────────────────────────────────────────────────

    private void OnMiniMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == PlatformInput.MouseButton.Left)
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
        if (_dragging && e.ChangedButton == PlatformInput.MouseButton.Left)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(
            MinimapBackground,
            MinimapBorderThickness > 0 ? new Pen(MinimapBorderBrush, MinimapBorderThickness) : null,
            new Rect(0, 0, RenderSize.Width, RenderSize.Height),
            MinimapCornerRadius, MinimapCornerRadius);

        EnsureContent();
        var bounds = _contentBounds;
        var (ox, oy, scale) = ComputeTransform(bounds);
        if (scale <= 0)
        {
            return;
        }

        foreach (var (nx, ny, nw, nh) in _nodeRects)
        {
            var (lx, ly) = WorkflowSurfaceMath.MinimapLocal(nx, ny, bounds.X, bounds.Y, ox, oy, scale);
            dc.DrawRoundedRectangle(NodeBrush, null,
                new Rect(lx, ly,
                    WorkflowSurfaceMath.MinThumbSize(nw, scale, MinimapMinSize),
                    WorkflowSurfaceMath.MinThumbSize(nh, scale, MinimapMinSize)),
                NodeCornerRadius, NodeCornerRadius);
        }

        var worldLeft = WorkflowSurfaceMath.ToWorld(ScrollOffsetX, ContentOffsetX);
        var worldTop = WorkflowSurfaceMath.ToWorld(ScrollOffsetY, ContentOffsetY);
        var (vx, vy, vw, vh) = WorkflowSurfaceMath.MinimapViewportRect(
            ox, oy, scale, worldLeft, worldTop, ViewportWidth, ViewportHeight,
            bounds.X, bounds.Y, Width, Height, minRectSize: 2);
        dc.DrawRectangle(
            ViewportFill,
            ViewportStrokeThickness > 0 ? new Pen(ViewportStroke, ViewportStrokeThickness) : null,
            new Rect(vx, vy, vw, vh));
    }
}
