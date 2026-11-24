using System;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.ViewHosting;
using PlatformInput = Jalium.UI.Input;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Self-contained minimap. Content-fit: maps the bounding box of all node cards into
/// the minimap (nodes only, 2× padding, centered), draws a translucent white viewport rect
/// clamped inside the minimap, and drag-to-pans by reusing the surface's edge-aware navigation
/// (grows Layout.Positive/NegativeOffset when dragged past an edge).</summary>
/// <remarks>
/// Tree subscription, content/viewport mapping, placement and drag math live in the Core
/// <see cref="MinimapPipeline"/>. This class keeps the dependency properties, the platform drawing,
/// and the mouse handlers that feed the input layer.
/// </remarks>
public class WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay
{

    /// <summary>The surface's horizontal scroll offset, pushed by the surface behavior.</summary>
    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        "ScrollOffsetX", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    /// <summary>The surface's vertical scroll offset, pushed by the surface behavior.</summary>
    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        "ScrollOffsetY", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    /// <summary>The canvas content offset on the horizontal axis, pushed by the surface behavior.</summary>
    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        "ContentOffsetX", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    /// <summary>The canvas content offset on the vertical axis, pushed by the surface behavior.</summary>
    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        "ContentOffsetY", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    /// <summary>The visible viewport width, pushed by the surface behavior.</summary>
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        "ViewportWidth", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    /// <summary>The visible viewport height, pushed by the surface behavior.</summary>
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        "ViewportHeight", typeof(double), typeof(WorkflowMinimapOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    /// <summary>Whether the minimap draws at all.</summary>
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
    /// <summary>The workflow tree the minimap renders; assigning it subscribes the pipeline to its nodes and links.</summary>
    public static readonly DependencyProperty WorkflowTreeProperty = DependencyProperty.Register(
        "WorkflowTree", typeof(ITreeComponent), typeof(WorkflowMinimapOverlay), new PropertyMetadata(null, OnTreeChanged));

    /// <summary>The surface's horizontal scroll offset.</summary>
    public double ScrollOffsetX { get => Read(ScrollOffsetXProperty, 0.0); set => SetValue(ScrollOffsetXProperty, value); }
    /// <summary>The surface's vertical scroll offset.</summary>
    public double ScrollOffsetY { get => Read(ScrollOffsetYProperty, 0.0); set => SetValue(ScrollOffsetYProperty, value); }
    /// <summary>The canvas content offset on the horizontal axis.</summary>
    public double ContentOffsetX { get => Read(ContentOffsetXProperty, 0.0); set => SetValue(ContentOffsetXProperty, value); }
    /// <summary>The canvas content offset on the vertical axis.</summary>
    public double ContentOffsetY { get => Read(ContentOffsetYProperty, 0.0); set => SetValue(ContentOffsetYProperty, value); }
    /// <inheritdoc />
    public double RulerBand => 0;
    /// <summary>The visible viewport width.</summary>
    public double ViewportWidth { get => Read(ViewportWidthProperty, 0.0); set => SetValue(ViewportWidthProperty, value); }
    /// <summary>The visible viewport height.</summary>
    public double ViewportHeight { get => Read(ViewportHeightProperty, 0.0); set => SetValue(ViewportHeightProperty, value); }
    /// <summary>Whether the minimap draws at all.</summary>
    public bool IsMinimapVisible { get => Read(IsMinimapVisibleProperty, true); set => SetValue(IsMinimapVisibleProperty, value); }
    /// <inheritdoc />
    public double MinimapMargin { get => Read(MinimapMarginProperty, 12d); set => SetValue(MinimapMarginProperty, value); }
    /// <inheritdoc />
    public double AvailableRight { get => Read(AvailableRightProperty, 0d); set => SetValue(AvailableRightProperty, value); }
    /// <inheritdoc />
    public double AvailableTop { get => Read(AvailableTopProperty, 0d); set => SetValue(AvailableTopProperty, value); }
    /// <inheritdoc />
    public double AvailableBottom { get => Read(AvailableBottomProperty, 0d); set => SetValue(AvailableBottomProperty, value); }
    /// <summary>The workflow tree the minimap renders.</summary>
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

    private readonly MinimapPipeline pipeline;
    private readonly MinimapInputHost inputHost;

    /// <summary>Creates the overlay and composes its minimap pipeline from the Jalium host layers.</summary>
    public WorkflowMinimapOverlay()
    {
        ClipToBounds = true;

        inputHost = new MinimapInputHost(this);
        pipeline = new MinimapPipeline(new MinimapRenderer(this), new MinimapViewportHost(this), inputHost);
        pipeline.RenderRequested += OnPipelineRenderRequested;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMiniMouseDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMiniMouseMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMiniMouseUp));
        AddHandler(LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture));

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ResolveScrollViewer();

        // 卸载时退过订；重新挂回可视树要把同一棵树重新订上。
        pipeline.Tree = WorkflowTree;
        RefreshPipelineSettings();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        // 防内存泄漏：覆盖层从可视树移除时（如关闭标签页），退订所有树事件、清掉对 ViewModel 对象的强引用。
        pipeline.Tree = null;
        pipeline.PointerCaptureLost();
        inputHost.IsPressed = false;
    }

    /// <inheritdoc />
    protected override HitTestResult? HitTestCore(Point hitTestPoint)
    {
        // 全表面浮层：模板让它铺满表面，小地图自己画在 context 给出的「可用右上角」。
        // 命中测试只在小地图矩形内返回结果，其余区域的指针穿到下面的画布。
        if (!IsMinimapVisible || Visibility != Jalium.UI.Visibility.Visible || !IsHitTestVisible)
        {
            return null;
        }

        RefreshPipelineSettings();
        var bounds = pipeline.MinimapRect;
        var rect = new Rect(bounds.Left, bounds.Top, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        return rect.Contains(hitTestPoint) ? base.HitTestCore(hitTestPoint) : null;
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WorkflowMinimapOverlay overlay)
        {
            return;
        }

        overlay.RefreshPipelineSettings();
        overlay.pipeline.Invalidate();
    }

    private static void OnTreeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkflowMinimapOverlay overlay)
        {
            overlay.pipeline.Tree = (ITreeComponent?)e.NewValue;
        }
    }

    private void OnPipelineRenderRequested(object? sender, EventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (!IsVisible)
        {
            return;
        }

        // OnNodePropertyChanged / OnSlotPropertyChanged 可能从 TickManager 循环线程触发（经 BroadcastVisibleItemLayout）；InvalidateVisual 必须在 UI 线程，需要时派发。
        if (Dispatcher.CheckAccess())
        {
            InvalidateVisual();
        }
        else
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(InvalidateVisual));
        }
    }

    // 可用区来自 surface：右侧让开竖滚动条，顶部让开水平刻度，底部让开横滚动条。
    // 没拿到可用区时退回浮层自己的整块尺寸，至少仍落在右上角。
    private void RefreshPipelineSettings()
    {
        var width = Math.Max(0, ActualWidth > 0 ? ActualWidth : MinimapWidth);
        var height = Math.Max(0, ActualHeight > 0 ? ActualHeight : MinimapHeight);
        var right = AvailableRight > 0 ? AvailableRight : width;
        var top = Math.Max(0, AvailableTop);
        var bottom = AvailableBottom > top ? AvailableBottom : height;

        pipeline.Metrics = new MinimapMetrics
        {
            Width = MinimapWidth,
            Height = MinimapHeight,
            MinSize = MinimapMinSize,
            Padding = ContentPadding,
            NodeCornerRadius = NodeCornerRadius,
            MinimapCornerRadius = MinimapCornerRadius,
            // 这家把最小缩略尺寸与最小落位尺寸写在同一个属性上，保持既有观感。
            MinimumThumbSize = MinimapMinSize,
            Available = new SurfaceAvailableArea
            {
                Right = right,
                Top = top,
                Bottom = bottom,
                Margin = MinimapMargin,
            },
        };
        pipeline.IsVisible = IsMinimapVisible;
    }

    internal void ApplyScrollOffsets(double x, double y)
    {
        ScrollViewer?.ScrollToHorizontalOffset(x);
        ScrollViewer?.ScrollToVerticalOffset(y);
    }

    // ── 拖拽导航 ────────────────────────────────────────────────────────────

    private void OnMiniMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != PlatformInput.MouseButton.Left || !IsMinimapVisible)
        {
            return;
        }

        // 浮层铺满表面，只有落在小地图矩形里的按下才归它；其余区域的指针不该被它吃掉。
        RefreshPipelineSettings();
        var point = e.GetPosition(this);
        inputHost.PointerPosition = (point.X, point.Y);
        inputHost.IsPressed = true;
        if (pipeline.PointerPressed())
        {
            e.Handled = true;
        }
    }

    private void OnMiniMouseMove(object? sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        inputHost.PointerPosition = (point.X, point.Y);
        if (!pipeline.IsDragging)
        {
            return;
        }

        pipeline.PointerMoved();
        e.Handled = true;
    }

    private void OnMiniMouseUp(object? sender, MouseButtonEventArgs e)
    {
        inputHost.IsPressed = false;
        if (!pipeline.IsDragging)
        {
            return;
        }

        pipeline.PointerReleased();
        e.Handled = true;
    }

    private void OnLostMouseCapture(object? sender, MouseEventArgs e)
    {
        inputHost.IsPressed = false;
        pipeline.PointerCaptureLost();
    }

    // ── 导航 ────────────────────────────────────────────────────────────────

    /// <summary>Centers the view on a world point, growing the canvas if the target scroll runs past an edge.</summary>
    public void NavigateToCanvasLocal(double wx, double wy)
    {
        if (pipeline.Tree is not { } tree)
        {
            return;
        }

        var viewport = pipeline.Viewport;
        var layout = tree.Layout;
        var maxH = Math.Max(0, viewport.ScrollableWidth);
        var maxV = Math.Max(0, viewport.ScrollableHeight);
        var (scrollX, scrollY) = WorkflowSurfaceMath.MinimapToScroll(
            wx, wy, viewport.ViewportWidth, viewport.ViewportHeight, viewport.ContentOffsetX, viewport.ContentOffsetY);
        scrollX = WorkflowSurfaceMath.ClampScrollOffset(scrollX, maxH, layout, horizontal: true);
        scrollY = WorkflowSurfaceMath.ClampScrollOffset(scrollY, maxV, layout, horizontal: false);
        viewport.ApplyScroll(
            WorkflowSurfaceMath.ClampValue(scrollX, 0, maxH),
            WorkflowSurfaceMath.ClampValue(scrollY, 0, maxV));
    }

    // ── 渲染 ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (!IsMinimapVisible)
        {
            return;
        }

        RefreshPipelineSettings();
        pipeline.Render(dc);
    }

    internal void DrawMinimapSurface(DrawingContext dc, MinimapDrawContext context) => DrawMinimap(dc, context);

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
            MinimapBorderThickness > 0 ? GetPen(ref _borderPen, MinimapBorderBrush, MinimapBorderThickness) : null,
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
                ViewportStrokeThickness > 0 ? GetPen(ref _viewportPen, ViewportStroke, ViewportStrokeThickness) : null,
                new Rect(offsetX + vp.Left, offsetY + vp.Top, vp.Width, vp.Height));
        }
    }

    // 描边画笔按画刷引用与线宽缓存：小地图每帧重画，但配色只在属性变化时换，不应每帧新建两支 Pen。
    private Pen? _borderPen;
    private Pen? _viewportPen;

    private static Pen GetPen(ref Pen? cache, Brush brush, double thickness)
    {
        if (cache is null || !ReferenceEquals(cache.Brush, brush) || cache.Thickness != thickness)
        {
            cache = new Pen(brush, thickness);
        }

        return cache;
    }
}
