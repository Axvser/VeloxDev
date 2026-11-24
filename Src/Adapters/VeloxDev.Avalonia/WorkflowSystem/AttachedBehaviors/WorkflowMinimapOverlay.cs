using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using VeloxDev.WorkflowSystem.ViewHosting;
using PlatformInput = Avalonia.Input;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A full-surface minimap overlay for Avalonia. It fills its host and draws a thumbnail overview of all
/// nodes and the visible viewport inside the content viewport's top-right corner, below the ruler band and
/// left of the vertical scrollbar. Only the drawn minimap rectangle receives pointer input.
///
/// The surface behavior pushes <see cref="AvailableRight"/> / <see cref="AvailableTop"/> /
/// <see cref="AvailableBottom"/> so the corner follows the scrollbars and ruler; dragging inside the minimap
/// navigates the bound ScrollViewer.
/// </summary>
/// <remarks>
/// Tree subscription, content/viewport mapping, placement and drag math live in the Core
/// <see cref="MinimapPipeline"/>. This class keeps the styled properties, the platform drawing, and the
/// pointer handlers that feed the input layer.
/// </remarks>
public class WorkflowMinimapOverlay : Control, IWorkflowMinimapOverlay, ICustomHitTest
{
    // ── 样式属性 ─────────────────────────────────────────────────────────────

    public static readonly StyledProperty<double> ScrollOffsetXProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ScrollOffsetX));

    public static readonly StyledProperty<double> ScrollOffsetYProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ScrollOffsetY));

    public static readonly StyledProperty<double> ContentOffsetXProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ContentOffsetX));

    public static readonly StyledProperty<double> ContentOffsetYProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(ContentOffsetY));

    /// <summary>The workflow tree view model to visualize.</summary>
    public static readonly StyledProperty<ITreeComponent?> WorkflowTreeProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, ITreeComponent?>(nameof(WorkflowTree));

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
    /// Default 28 matches <c>WorkflowGridDecorator.RulerThickness</c>.
    /// </summary>
    public static readonly StyledProperty<double> RulerThicknessProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(RulerThickness), 28d);

    public static readonly StyledProperty<double> MinimapWidthProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(MinimapWidth), 200d);

    public static readonly StyledProperty<double> MinimapHeightProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(MinimapHeight), 140d);

    public static readonly StyledProperty<double> MinimapMarginProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(MinimapMargin), 12d);

    /// <summary>
    /// The x of the content viewport's right edge in this overlay's local pixels, excluding the vertical
    /// scrollbar. Zero means the surface has not supplied an available corner yet.
    /// </summary>
    public static readonly StyledProperty<double> AvailableRightProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(AvailableRight));

    /// <summary>The y of the content viewport's top edge, below the ruler band.</summary>
    public static readonly StyledProperty<double> AvailableTopProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(AvailableTop));

    /// <summary>
    /// The y of the content viewport's bottom edge in this overlay's local pixels, excluding the horizontal
    /// scrollbar. Zero means the surface has not supplied an available corner yet.
    /// </summary>
    public static readonly StyledProperty<double> AvailableBottomProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(AvailableBottom));

    public static readonly StyledProperty<double> LinkStrokeThicknessProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, double>(nameof(LinkStrokeThickness), 2.0);

    // ── 画刷 ────────────────────────────────────────────────────────────────

    public static readonly StyledProperty<IBrush?> MinimapBackgroundProperty =
        AvaloniaProperty.Register<WorkflowMinimapOverlay, IBrush?>(nameof(MinimapBackground),
            new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x1E, 0x1E)));

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

    // 这些属性一变，映射或放置就过期：Metrics 只覆盖尺寸与可用角，滚动/内容偏移和视口尺寸不在其中。
    private static readonly AvaloniaProperty[] MappingProperties =
    [
        WorkflowTreeProperty,
        ScrollOffsetXProperty, ScrollOffsetYProperty,
        ContentOffsetXProperty, ContentOffsetYProperty,
        ViewportWidthProperty, ViewportHeightProperty,
        IsMinimapVisibleProperty,
        MinimapWidthProperty, MinimapHeightProperty, MinimapMarginProperty, MinimapMinSizeProperty,
        AvailableRightProperty, AvailableTopProperty, AvailableBottomProperty,
        NodeCornerRadiusProperty, MinimapCornerRadiusProperty, ContentPaddingProperty,
    ];

    // ── CLR 访问器 ───────────────────────────────────────────────────────────

    public double ScrollOffsetX { get => GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    /// <inheritdoc />
    public double RulerBand => 0;
    public ITreeComponent? WorkflowTree { get => GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public double ViewportWidth { get => GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public bool IsMinimapVisible { get => GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }
    public double MinimapWidth { get => GetValue(MinimapWidthProperty); set => SetValue(MinimapWidthProperty, value); }
    public double MinimapHeight { get => GetValue(MinimapHeightProperty); set => SetValue(MinimapHeightProperty, value); }
    public double MinimapMargin { get => GetValue(MinimapMarginProperty); set => SetValue(MinimapMarginProperty, value); }
    /// <inheritdoc />
    public double AvailableRight { get => GetValue(AvailableRightProperty); set => SetValue(AvailableRightProperty, value); }
    /// <inheritdoc />
    public double AvailableTop { get => GetValue(AvailableTopProperty); set => SetValue(AvailableTopProperty, value); }
    /// <inheritdoc />
    public double AvailableBottom { get => GetValue(AvailableBottomProperty); set => SetValue(AvailableBottomProperty, value); }
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

    // ── 状态 ────────────────────────────────────────────────────────────────

    private readonly MinimapPipeline pipeline;
    private readonly MinimapInputHost inputHost;
    private ScrollViewer? scrollViewer;

    static WorkflowMinimapOverlay()
    {
        AffectsRender<WorkflowMinimapOverlay>(
            ScrollOffsetXProperty, ScrollOffsetYProperty,
            ContentOffsetXProperty, ContentOffsetYProperty,
            WorkflowTreeProperty,
            ViewportWidthProperty, ViewportHeightProperty,
            IsMinimapVisibleProperty,
            MinimapWidthProperty, MinimapHeightProperty, MinimapMarginProperty, MinimapMinSizeProperty,
            AvailableRightProperty, AvailableTopProperty, AvailableBottomProperty,
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
        inputHost = new MinimapInputHost(this);
        pipeline = new MinimapPipeline(new MinimapRenderer(this), new MinimapViewportHost(this), inputHost);
        pipeline.RenderRequested += OnPipelineRenderRequested;

        // 全表面浮层：模板让它铺满表面，小地图自己画在 pipeline 给出的“可用右上角”。
        Focusable = true;
        AttachedToVisualTree += OnOverlayAttachedToVisualTree;
        DetachedFromVisualTree += OnOverlayDetachedFromVisualTree;
    }

    // 拖拽导航要把滚动写回宿主表面，宿主表面按名字解析。
    internal ScrollViewer? MinimapScrollViewer => scrollViewer;

    // ── 命中测试 ─────────────────────────────────────────────────────────────

    // 全表面浮层只在算出来的小地图矩形里接收指针，其余位置放行给下面的画布。
    // point 是元素局部坐标（Avalonia 的 ICustomHitTest 契约），与 pipeline 的放置同一坐标系。
    bool ICustomHitTest.HitTest(Point point)
    {
        RefreshPipelineSettings();
        return pipeline.HitTest(point.X, point.Y);
    }

    // ── 生命周期 ─────────────────────────────────────────────────────────────

    private void OnOverlayAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ResolveScrollViewer();

        // 卸载时退过订；重新挂回可视树要把同一棵树重新订上。
        pipeline.Tree = WorkflowTree;
        RefreshPipelineSettings();
        pipeline.Invalidate();
        InvalidateVisual();
    }

    private void OnOverlayDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // 防内存泄漏：覆盖层从可视树移除时（如关闭标签页），退订所有树事件、清掉对 ViewModel 对象的强引用。
        pipeline.Tree = null;
        pipeline.PointerCaptureLost();
        inputHost.IsPressed = false;
        inputHost.TrackPointer(null);
        scrollViewer = null;
    }

    private void ResolveScrollViewer()
    {
        scrollViewer = null;
        if (string.IsNullOrWhiteSpace(ScrollViewerName))
        {
            return;
        }

        // 从可视根递归查找 ScrollViewer：FindControl 只搜后代，所以沿完整可视树向上走。
        // Avalonia 12 移除了 GetVisualRoot()、e.Root 的返回类型也变了（11 是 IRenderRoot，12 是 Visual），
        // 因此用两版签名一致的 GetVisualParent() 向上走。
        Visual root = this;
        while (root.GetVisualParent() is { } parent)
            root = parent;
        scrollViewer = FindByName<ScrollViewer>(root, ScrollViewerName);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WorkflowTreeProperty)
            pipeline.Tree = change.NewValue as ITreeComponent;

        if (Array.IndexOf(MappingProperties, change.Property) >= 0)
        {
            RefreshPipelineSettings();
            pipeline.Invalidate();
        }
    }

    // 可用区来自 surface：右侧让开竖滚动条，顶部让开水平刻度，底部让开横滚动条。
    private void RefreshPipelineSettings()
    {
        var width = Math.Max(0, Bounds.Width);
        var height = Math.Max(0, Bounds.Height);
        var top = Math.Max(0, AvailableTop);

        pipeline.Metrics = new MinimapMetrics
        {
            Width = MinimapWidth,
            Height = MinimapHeight,
            MinSize = MinimapMinSize,
            Padding = ContentPadding,
            NodeCornerRadius = NodeCornerRadius,
            MinimapCornerRadius = MinimapCornerRadius,
            MinimumThumbSize = 2d,
            Available = new SurfaceAvailableArea
            {
                Right = AvailableRight > 0 ? AvailableRight : width,
                Top = top,
                Bottom = AvailableBottom > top ? AvailableBottom : height,
                Margin = MinimapMargin,
            },
        };
        pipeline.IsVisible = IsMinimapVisible;
    }

    internal void ApplyScrollOffsets(double x, double y)
    {
        if (scrollViewer is not null)
        {
            scrollViewer.Offset = new Vector(x, y);
        }
    }

    private void OnPipelineRenderRequested(object? sender, EventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (!IsVisible)
        {
            return;
        }

        // 树的通知可能从 TickManager 循环线程触发（经 BroadcastVisibleItemLayout）；InvalidateVisual 必须在 UI 线程。
        if (Dispatcher.UIThread.CheckAccess())
            InvalidateVisual();
        else
            Dispatcher.UIThread.Post(InvalidateVisual);
    }

    // ── 指针/拖拽：只作用于视口指示块 ────────────────────────────────────────

    protected override void OnPointerPressed(PlatformInput.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var pt = e.GetPosition(this);
        inputHost.PointerPosition = (pt.X, pt.Y);
        inputHost.IsPressed = true;
        inputHost.TrackPointer(e.Pointer);

        RefreshPipelineSettings();
        if (pipeline.PointerPressed())
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PlatformInput.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!pipeline.IsDragging)
            return;

        var pt = e.GetPosition(this);
        inputHost.PointerPosition = (pt.X, pt.Y);
        pipeline.PointerMoved();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PlatformInput.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        inputHost.IsPressed = false;
        if (!pipeline.IsDragging)
            return;

        pipeline.PointerReleased();
        inputHost.TrackPointer(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        inputHost.IsPressed = false;
        pipeline.PointerCaptureLost();
        inputHost.TrackPointer(null);
    }

    // ── 渲染 ────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        RefreshPipelineSettings();
        pipeline.Render(context);
    }

    internal void DrawMinimapSurface(DrawingContext drawingContext, Wf.MinimapDrawContext context)
        => DrawMinimap(drawingContext, context);

    /// <summary>
    /// Draws the minimap from <paramref name="context"/>. Override and call <c>base</c> to add to the default drawing.
    /// </summary>
    protected virtual void DrawMinimap(DrawingContext drawingContext, Wf.MinimapDrawContext context)
    {
        var bounds = context.GetMinimapRect(MinimapMinSize);
        var mmRect = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        var offsetX = mmRect.Left;
        var offsetY = mmRect.Top;
        var cr = (float)Math.Max(0, context.MinimapCornerRadius);

        // 圆角背景
        if (MinimapBackground is not null)
            drawingContext.FillRectangle(MinimapBackground, mmRect, cr);
        if (MinimapBorderBrush is not null)
            drawingContext.DrawRectangle(null, GetPen(ref _borderPen, MinimapBorderBrush, MinimapBorderThickness), mmRect, cr);

        using (drawingContext.PushClip(mmRect))
        {
            // 节点
            if (NodeBrush is not null)
            {
                var ncr = (float)Math.Max(0, context.NodeCornerRadius);
                foreach (var node in context.MappedNodes)
                {
                    drawingContext.FillRectangle(NodeBrush,
                        new Rect(offsetX + node.Left, offsetY + node.Top, node.Width, node.Height), ncr);
                }
            }

            // 视口指示块
            if (context.HasViewport && !context.MappedViewport.IsEmpty)
            {
                var vp = context.MappedViewport;
                var vr = new Rect(offsetX + vp.Left, offsetY + vp.Top, vp.Width, vp.Height);
                var ncr = (float)Math.Max(0, context.NodeCornerRadius);
                if (ViewportFill is not null)
                    drawingContext.FillRectangle(ViewportFill, vr, ncr);
                if (ViewportStroke is not null)
                    drawingContext.DrawRectangle(null, GetPen(ref _viewportPen, ViewportStroke, ViewportStrokeThickness), vr, ncr);
            }
        }
    }

    // 描边画笔按画刷引用与线宽缓存，避免每帧 Render 分配新 Pen。
    private Pen? _borderPen;
    private Pen? _viewportPen;

    private static Pen GetPen(ref Pen? cache, IBrush brush, double thickness)
    {
        if (cache is null || !ReferenceEquals(cache.Brush, brush) || cache.Thickness != thickness)
        {
            cache = new Pen(brush, thickness);
        }

        return cache;
    }
}
