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
    /// <summary>The gap kept between the minimap and the content viewport's corner.</summary>
    public static readonly DependencyProperty MinimapMarginProperty = DependencyProperty.Register(
        nameof(MinimapMargin), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(12d, OnVisualChanged));
    /// <summary>The x of the content viewport's right edge in overlay-local pixels, excluding the vertical scrollbar.</summary>
    public static readonly DependencyProperty AvailableRightProperty = DependencyProperty.Register(
        nameof(AvailableRight), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0d, OnVisualChanged));
    /// <summary>The y of the content viewport's top edge, below the horizontal ruler band.</summary>
    public static readonly DependencyProperty AvailableTopProperty = DependencyProperty.Register(
        nameof(AvailableTop), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0d, OnVisualChanged));
    /// <summary>The y of the content viewport's bottom edge in overlay-local pixels, excluding the horizontal scrollbar.</summary>
    public static readonly DependencyProperty AvailableBottomProperty = DependencyProperty.Register(
        nameof(AvailableBottom), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0d, OnVisualChanged));
    public static readonly DependencyProperty WorkflowTreeProperty = DependencyProperty.Register(
        "WorkflowTree", typeof(ITreeComponent), typeof(WorkflowMinimapOverlay), new PropertyMetadata(null, OnTreeChanged));

    public double ScrollOffsetX { get => Read(ScrollOffsetXProperty, 0.0); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => Read(ScrollOffsetYProperty, 0.0); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => Read(ContentOffsetXProperty, 0.0); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => Read(ContentOffsetYProperty, 0.0); set => SetValue(ContentOffsetYProperty, value); }
    /// <inheritdoc />
    public double RulerBand => 0;
    public double ViewportWidth { get => Read(ViewportWidthProperty, 0.0); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => Read(ViewportHeightProperty, 0.0); set => SetValue(ViewportHeightProperty, value); }
    public bool IsMinimapVisible { get => Read(IsMinimapVisibleProperty, true); set => SetValue(IsMinimapVisibleProperty, value); }
    /// <inheritdoc />
    public double MinimapMargin { get => Read(MinimapMarginProperty, 12d); set => SetValue(MinimapMarginProperty, value); }
    /// <inheritdoc />
    public double AvailableRight { get => Read(AvailableRightProperty, 0d); set => SetValue(AvailableRightProperty, value); }
    /// <inheritdoc />
    public double AvailableTop { get => Read(AvailableTopProperty, 0d); set => SetValue(AvailableTopProperty, value); }
    /// <inheritdoc />
    public double AvailableBottom { get => Read(AvailableBottomProperty, 0d); set => SetValue(AvailableBottomProperty, value); }
    public ITreeComponent? WorkflowTree { get => (ITreeComponent?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }

    // 值类型的附着属性都注册了非 null 默认值，所以取回来必是那个类型。`GetValue` 的返回类型是 `object?`，
    // 直接强转会让编译器报「取消装箱可能为 null」—— 与其到处写 `!`，不如把那个默认值在这里写出来，
    // 顺带让「DP 没注册默认值时取到什么」有定义。
    private T Read<T>(DependencyProperty property, T fallback) where T : struct
        => GetValue(property) is T value ? value : fallback;

    /// <summary>Assigned by the composing control for drag-to-pan, or resolved from <see cref="ScrollViewerName"/>.</summary>
    public ScrollViewer? ScrollViewer { get; set; }

    /// <summary>The minimap's desired width; the draw rect is computed from the available surface corner.</summary>
    public static readonly DependencyProperty MinimapWidthProperty = DependencyProperty.Register(
        nameof(MinimapWidth), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(200d, OnVisualChanged));

    /// <summary>The minimap's desired height; the draw rect is computed from the available surface corner.</summary>
    public static readonly DependencyProperty MinimapHeightProperty = DependencyProperty.Register(
        nameof(MinimapHeight), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(140d, OnVisualChanged));

    /// <summary>The smallest side a node thumbnail is drawn at, in minimap pixels.</summary>
    public static readonly DependencyProperty MinimapMinSizeProperty = DependencyProperty.Register(
        nameof(MinimapMinSize), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(1d, OnVisualChanged));

    /// <summary>The inset between the minimap's edge and the content it fits.</summary>
    public static readonly DependencyProperty ContentPaddingProperty = DependencyProperty.Register(
        nameof(ContentPadding), typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(8d, OnVisualChanged));

    /// <summary>The minimap's fill.</summary>
    public static readonly DependencyProperty MinimapBackgroundProperty = DependencyProperty.Register(
        nameof(MinimapBackground), typeof(Brush), typeof(WorkflowMinimapOverlay),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x1E, 0x1E)), OnVisualChanged));

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

    /// <summary>The minimap's desired width.</summary>
    public double MinimapWidth { get => Read(MinimapWidthProperty, 200d); set => SetValue(MinimapWidthProperty, value); }

    /// <summary>The minimap's desired height.</summary>
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
    private ITreeComponent? _tree;
    private bool _dragging;
    private Rect _minimapRect = Rect.Empty;

    public WorkflowMinimapOverlay()
    {
        ClipToBounds = true;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMiniMouseDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMiniMouseMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMiniMouseUp));
        Loaded += (_, _) => ResolveScrollViewer();
    }

    // 全表面浮层：模板让它铺满表面，小地图自己画在 context 给出的「可用右上角」。
    // 命中测试只在小地图矩形内返回结果，其余区域的指针穿到下面的画布。
    protected override HitTestResult? HitTestCore(Point hitTestPoint)
    {
        if (!IsMinimapVisible || Visibility != Jalium.UI.Visibility.Visible || !IsHitTestVisible)
        {
            return null;
        }

        if (_minimapRect.IsEmpty)
        {
            _minimapRect = ComputeMinimapRect();
        }

        return _minimapRect.Contains(hitTestPoint) ? base.HitTestCore(hitTestPoint) : null;
    }

    // 可用区来自 surface：右侧让开竖滚动条，顶部让开水平刻度，底部让开横滚动条。
    // 没拿到可用区时退回浮层自己的整块尺寸，至少仍落在右上角。
    private Rect ComputeMinimapRect()
    {
        var width = Math.Max(0, ActualWidth > 0 ? ActualWidth : MinimapWidth);
        var height = Math.Max(0, ActualHeight > 0 ? ActualHeight : MinimapHeight);
        var right = AvailableRight > 0 ? AvailableRight : width;
        var top = Math.Max(0, AvailableTop);
        var bottom = AvailableBottom > top ? AvailableBottom : height;

        var placement = new MinimapDrawContext
        {
            MinimapWidth = MinimapWidth,
            MinimapHeight = MinimapHeight,
            MinimapMargin = MinimapMargin,
            AvailableRight = right,
            AvailableTop = top,
            AvailableBottom = bottom,
        };
        var bounds = placement.GetMinimapRect(MinimapMinSize);
        return new Rect(bounds.Left, bounds.Top, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            // 可用区或尺寸变了，缓存的小地图矩形就不再成立；下一次命中或绘制重新算。
            if (d is WorkflowMinimapOverlay overlay)
            {
                overlay._minimapRect = Rect.Empty;
            }

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
        overlay._tree = (ITreeComponent?)e.NewValue;
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

    private void SubscribeNode(INodeComponent node)
    {
        if (node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += OnNodeChanged;
        }
    }

    private void UnsubscribeNode(INodeComponent node)
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
                if (item is INodeComponent node)
                {
                    UnsubscribeNode(node);
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INodeComponent node)
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
        if (e.PropertyName is nameof(INodeComponent.Anchor) or nameof(INodeComponent.Size))
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

    private (double Ox, double Oy, double Scale) ComputeTransform(Rect bounds, double minimapWidth, double minimapHeight)
    {
        var padding = ContentPadding;
        double drawW = Math.Max(1, minimapWidth - padding * 2);
        double drawH = Math.Max(1, minimapHeight - padding * 2);
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
        var rect = _minimapRect.IsEmpty ? ComputeMinimapRect() : _minimapRect;

        // 指针是浮层坐标，映射函数吃小地图自己的局部坐标：先减掉绘制矩形的原点。
        var (ox, oy, scale) = ComputeTransform(bounds, rect.Width, rect.Height);
        if (scale <= 0)
        {
            return;
        }

        var (wx, wy) = WorkflowSurfaceMath.MinimapToCanvasLocal(
            mini.X - rect.Left, mini.Y - rect.Top, ox, oy, scale, bounds.X, bounds.Y);
        NavigateToCanvasLocal(wx, wy);
    }

    /// <summary>Centers the view on a world point, growing the canvas if the target scroll runs past an edge.</summary>
    public void NavigateToCanvasLocal(double wx, double wy)
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
        if (e.ChangedButton != PlatformInput.MouseButton.Left || !IsMinimapVisible)
        {
            return;
        }

        // 浮层铺满表面，只有落在小地图矩形里的按下才归它；其余区域的指针不该被它吃掉。
        var rect = ComputeMinimapRect();
        var point = e.GetPosition(this);
        if (!rect.Contains(point))
        {
            return;
        }

        _minimapRect = rect;
        _dragging = true;
        CaptureMouse();
        PanToMini(point);
        e.Handled = true;
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

        if (!IsMinimapVisible)
        {
            return;
        }

        EnsureContent();
        var context = CreateDrawContext();
        if (!context.HasContent || context.Scale <= 0)
        {
            return;
        }

        DrawMinimap(dc, context);
    }

    /// <summary>
    /// Draws the minimap from <paramref name="context"/>. Override and call <c>base</c> to add to the default drawing.
    /// </summary>
    protected virtual void DrawMinimap(DrawingContext dc, MinimapDrawContext context)
    {
        // 浮层铺满整个表面，小地图自己按可用右上角落位：背景、边框、节点、视口都按矩形原点平移。
        var bounds = context.GetMinimapRect(MinimapMinSize);
        var rect = new Rect(bounds.Left, bounds.Top, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        var offsetX = rect.Left;
        var offsetY = rect.Top;
        var radius = Math.Max(0, context.MinimapCornerRadius);

        dc.DrawRoundedRectangle(
            MinimapBackground,
            MinimapBorderThickness > 0 ? new Pen(MinimapBorderBrush, MinimapBorderThickness) : null,
            rect,
            radius, radius);

        foreach (var node in context.MappedNodes)
        {
            dc.DrawRoundedRectangle(NodeBrush, null,
                new Rect(offsetX + node.Left, offsetY + node.Top, node.Width, node.Height),
                context.NodeCornerRadius, context.NodeCornerRadius);
        }

        if (context.HasViewport && !context.MappedViewport.IsEmpty)
        {
            var vp = context.MappedViewport;
            dc.DrawRectangle(
                ViewportFill,
                ViewportStrokeThickness > 0 ? new Pen(ViewportStroke, ViewportStrokeThickness) : null,
                new Rect(offsetX + vp.Left, offsetY + vp.Top, vp.Width, vp.Height));
        }
    }

    /// <summary>Builds the geometry for one draw pass from the cached content and viewport bounds.</summary>
    private MinimapDrawContext CreateDrawContext()
    {
        var bounds = _contentBounds;
        var overlayWidth = ActualWidth > 0 ? ActualWidth : Math.Max(0, MinimapWidth);
        var overlayHeight = ActualHeight > 0 ? ActualHeight : Math.Max(0, MinimapHeight);

        // 先按可用右上角算出绘制矩形，映射与命中都用这个矩形自己的尺寸，而不是浮层的整块尺寸。
        var top = Math.Max(0, AvailableTop);
        var placement = new MinimapDrawContext
        {
            MinimapWidth = MinimapWidth,
            MinimapHeight = MinimapHeight,
            MinimapMargin = MinimapMargin,
            AvailableRight = AvailableRight > 0 ? AvailableRight : overlayWidth,
            AvailableTop = top,
            AvailableBottom = AvailableBottom > top ? AvailableBottom : overlayHeight,
        };
        var boundsRect = placement.GetMinimapRect(MinimapMinSize);
        var width = Math.Max(1, boundsRect.Width);
        var height = Math.Max(1, boundsRect.Height);
        _minimapRect = new Rect(boundsRect.Left, boundsRect.Top, width, height);

        var padding = ContentPadding;
        var (ox, oy, scale) = ComputeTransform(bounds, width, height);

        var nodes = new List<WorkflowBounds>(_nodeRects.Count);
        var mapped = new List<WorkflowBounds>(_nodeRects.Count);
        foreach (var (nx, ny, nw, nh) in _nodeRects)
        {
            nodes.Add(WorkflowBounds.FromNode(nx, ny, nw, nh));
            var (lx, ly) = WorkflowSurfaceMath.MinimapLocal(nx, ny, bounds.X, bounds.Y, ox, oy, scale);
            mapped.Add(WorkflowBounds.FromNode(lx, ly,
                WorkflowSurfaceMath.MinThumbSize(nw, scale, MinimapMinSize),
                WorkflowSurfaceMath.MinThumbSize(nh, scale, MinimapMinSize)));
        }

        var viewport = WorkflowBounds.FromNode(
            WorkflowSurfaceMath.ToCanvasLocal(ScrollOffsetX, ContentOffsetX),
            WorkflowSurfaceMath.ToCanvasLocal(ScrollOffsetY, ContentOffsetY),
            ViewportWidth, ViewportHeight);

        var mappedViewport = default(WorkflowBounds);
        if (!viewport.IsEmpty)
        {
            var (vx, vy, vw, vh) = WorkflowSurfaceMath.MinimapViewportRect(
                ox, oy, scale, viewport.Left, viewport.Top, viewport.Width, viewport.Height,
                bounds.X, bounds.Y, width, height, minRectSize: 2);
            mappedViewport = WorkflowBounds.FromNode(vx, vy, vw, vh);
        }

        return new MinimapDrawContext
        {
            Tree = WorkflowTree,
            ContentBounds = WorkflowBounds.FromNode(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            ViewportBounds = viewport,
            Nodes = nodes,
            MappedNodes = mapped,
            MappedViewport = mappedViewport,
            MinimapWidth = width,
            MinimapHeight = height,
            Padding = padding,
            OriginX = ox,
            OriginY = oy,
            Scale = scale,
            NodeCornerRadius = Math.Max(0, NodeCornerRadius),
            MinimapCornerRadius = Math.Max(0, MinimapCornerRadius),
            AvailableRight = placement.AvailableRight,
            AvailableTop = placement.AvailableTop,
            AvailableBottom = placement.AvailableBottom,
            MinimapMargin = placement.MinimapMargin,
        };
    }
}
