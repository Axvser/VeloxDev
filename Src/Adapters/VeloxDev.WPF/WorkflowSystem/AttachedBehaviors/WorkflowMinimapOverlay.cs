using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A minimap overlay for WPF that renders a thumbnail overview of all nodes,
/// links, and the visible viewport in the top-right corner.
/// Only the viewport indicator rectangle can be dragged to navigate.
/// </summary>
/// <remarks>
/// Tree subscription, content/viewport mapping, placement and drag math live in the Core
/// <see cref="MinimapPipeline"/>. This class keeps the dependency properties, the platform drawing,
/// and the mouse handlers that feed the input layer.
/// </remarks>
public class WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay
{
    // ── 依赖属性 ────────────────────────────────────────────────────────────

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
        DependencyProperty.Register(nameof(WorkflowTree), typeof(ITreeComponent), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(null, (d, e) => ((WorkflowMinimapOverlay)d).OnTreeValueChanged((ITreeComponent?)e.NewValue)));

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

    public static readonly DependencyProperty MinimapMarginProperty =
        DependencyProperty.Register(nameof(MinimapMargin), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(12d, OnPropChanged));

    public static readonly DependencyProperty AvailableRightProperty =
        DependencyProperty.Register(nameof(AvailableRight), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty AvailableTopProperty =
        DependencyProperty.Register(nameof(AvailableTop), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty AvailableBottomProperty =
        DependencyProperty.Register(nameof(AvailableBottom), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty RulerThicknessProperty =
        DependencyProperty.Register(nameof(RulerThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(28d, OnPropChanged));

    public static readonly DependencyProperty LinkStrokeThicknessProperty =
        DependencyProperty.Register(nameof(LinkStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(2.0, OnPropChanged));

    // ── 画刷/样式属性 ───────────────────────────────────────────────────────

    public static readonly DependencyProperty MinimapBackgroundProperty =
        DependencyProperty.Register(nameof(MinimapBackground), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x1E, 0x1E)), OnPropChanged));

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

    // ── CLR 访问器 ───────────────────────────────────────────────────────────

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    /// <inheritdoc />
    public double RulerBand => 0;
    public ITreeComponent? WorkflowTree { get => (ITreeComponent?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public bool IsMinimapVisible { get => (bool)GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }
    public double MinimapWidth { get => (double)GetValue(MinimapWidthProperty); set => SetValue(MinimapWidthProperty, value); }
    public double MinimapHeight { get => (double)GetValue(MinimapHeightProperty); set => SetValue(MinimapHeightProperty, value); }
    /// <inheritdoc />
    public double MinimapMargin { get => (double)GetValue(MinimapMarginProperty); set => SetValue(MinimapMarginProperty, value); }
    /// <inheritdoc />
    public double AvailableRight { get => (double)GetValue(AvailableRightProperty); set => SetValue(AvailableRightProperty, value); }
    /// <inheritdoc />
    public double AvailableTop { get => (double)GetValue(AvailableTopProperty); set => SetValue(AvailableTopProperty, value); }
    /// <inheritdoc />
    public double AvailableBottom { get => (double)GetValue(AvailableBottomProperty); set => SetValue(AvailableBottomProperty, value); }
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

    // ── 状态 ────────────────────────────────────────────────────────────────

    private readonly MinimapPipeline pipeline;
    private readonly MinimapInputHost inputHost;
    private ScrollViewer? scrollViewer;

    public WorkflowMinimapOverlay()
    {
        inputHost = new MinimapInputHost(this);
        pipeline = new MinimapPipeline(new MinimapRenderer(this), new MinimapViewportHost(this), inputHost);
        pipeline.RenderRequested += OnPipelineRenderRequested;

        // 全表面浮层：模板让它铺满表面，小地图自己画在 context 给出的“可用右上角”。
        IsHitTestVisible = true;
        Focusable = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // 拖拽导航要把滚动写回宿主表面，宿主表面按名字解析。
    internal ScrollViewer? MinimapScrollViewer => scrollViewer;

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        RefreshPipelineSettings();
        var pt = hitTestParameters.HitPoint;
        var bounds = pipeline.MinimapRect;
        var rect = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        return rect.Contains(pt) ? new PointHitTestResult(this, pt) : null;
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        ResolveScrollViewer();

        // 卸载时退过订；重新挂回可视树要把同一棵树重新订上。
        pipeline.Tree = WorkflowTree;
        RefreshPipelineSettings();
    }

    private void OnUnloaded(object? s, RoutedEventArgs e)
    {
        // 防内存泄漏：覆盖层从可视树移除时（如关闭标签页），退订所有树事件、清掉对 ViewModel 对象的强引用。
        pipeline.Tree = null;
        pipeline.PointerCaptureLost();
        inputHost.IsPressed = false;
        scrollViewer = null;
    }

    private void ResolveScrollViewer()
    {
        if (string.IsNullOrWhiteSpace(ScrollViewerName))
        {
            return;
        }

        // 沿可视树上溯找 UserControl（名称作用域属主）。UserControl 的 FindName 能找到其 XAML 里所有命名元素，而 Window.FindName 看不进 UserControl 名称作用域。
        DependencyObject? el = this;
        while (el is not null)
        {
            if (el is UserControl uc)
            {
                var found = uc.FindName(ScrollViewerName);
                if (found is ScrollViewer sv)
                {
                    scrollViewer = sv;
                }

                break;
            }

            el = VisualTreeHelper.GetParent(el);
        }
    }

    // ── 设置同步 ────────────────────────────────────────────────────────────

    private void OnTreeValueChanged(ITreeComponent? tree) => pipeline.Tree = tree;

    private static void OnPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WorkflowMinimapOverlay)d).OnSettingChanged();

    private void OnSettingChanged()
    {
        RefreshPipelineSettings();
        pipeline.Invalidate();
    }

    private void OnPipelineRenderRequested(object? s, EventArgs e) => MarkDirty();

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
            Dispatcher.BeginInvoke(InvalidateVisual);
        }
    }

    // 可用区来自 surface：右侧让开竖滚动条，顶部让开水平刻度，底部让开横滚动条。
    private void RefreshPipelineSettings()
    {
        var width = Math.Max(0, RenderSize.Width);
        var height = Math.Max(0, RenderSize.Height);
        var right = AvailableRight > 0 ? AvailableRight : width;
        var top = Math.Max(0, AvailableTop);
        var bottom = AvailableBottom > top ? AvailableBottom : height;

        pipeline.Metrics = new MinimapMetrics
        {
            Width = MinimapWidth,
            Height = MinimapHeight,
            Margin = MinimapMargin,
            MinSize = MinimapMinSize,
            Padding = ContentPadding,
            NodeCornerRadius = NodeCornerRadius,
            MinimapCornerRadius = MinimapCornerRadius,
            MinimumThumbSize = 2d,
            AvailableRight = right,
            AvailableTop = top,
            AvailableBottom = bottom,
        };
        pipeline.IsVisible = IsMinimapVisible;
    }

    internal void ApplyScrollOffsets(double x, double y)
    {
        scrollViewer?.ScrollToHorizontalOffset(x);
        scrollViewer?.ScrollToVerticalOffset(y);
    }

    // ── 命中测试/拖拽 ──────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(this);
        inputHost.PointerPosition = (pt.X, pt.Y);
        inputHost.IsPressed = true;
        if (pipeline.PointerPressed())
        {
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pt = e.GetPosition(this);
        inputHost.PointerPosition = (pt.X, pt.Y);
        if (!pipeline.IsDragging)
        {
            return;
        }

        pipeline.PointerMoved();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        inputHost.IsPressed = false;
        if (!pipeline.IsDragging)
        {
            return;
        }

        pipeline.PointerReleased();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        inputHost.IsPressed = false;
        pipeline.PointerCaptureLost();
        base.OnLostMouseCapture(e);
    }

    // ── 渲染 ────────────────────────────────────────────────────────────────

    // 画笔与裁剪几何按「画刷引用 + 粗细 / 矩形」缓存：每帧比较即可，不必另挂属性失效回调。
    // Pen 只持有画刷引用，画刷自身变色仍会生效。
    private Pen? _borderPen;
    private Pen? _viewportPen;
    private RectangleGeometry? _clipGeometry;

    private static Pen GetPen(ref Pen? cache, Brush brush, double thickness)
    {
        if (cache is null || !ReferenceEquals(cache.Brush, brush) || cache.Thickness != thickness)
            cache = new Pen(brush, thickness);
        return cache;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var sz = RenderSize;
        if (sz.Width <= 0 || sz.Height <= 0) return;
        RefreshPipelineSettings();
        pipeline.Render(dc);
    }

    internal void DrawMinimapSurface(DrawingContext dc, MinimapDrawContext context) => DrawMinimap(dc, context);

    /// <summary>
    /// Draws the minimap from <paramref name="context"/>. Override and call <c>base</c> to add to the default drawing.
    /// </summary>
    protected virtual void DrawMinimap(DrawingContext dc, MinimapDrawContext context)
    {
        var bounds = context.GetMinimapRect(MinimapMinSize);
        var mmRect = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        var offsetX = mmRect.Left;
        var offsetY = mmRect.Top;
        var cr = Math.Max(0, context.MinimapCornerRadius);

        // 缩略图背景
        if (MinimapBackground is not null)
            dc.DrawRoundedRectangle(MinimapBackground, null, mmRect, cr, cr);
        if (MinimapBorderBrush is not null)
            dc.DrawRoundedRectangle(null, GetPen(ref _borderPen, MinimapBorderBrush, MinimapBorderThickness), mmRect, cr, cr);

        if (_clipGeometry is null || _clipGeometry.Rect != mmRect)
            _clipGeometry = new RectangleGeometry(mmRect);
        dc.PushClip(_clipGeometry);
        try
        {
            // 节点
            if (NodeBrush is not null)
            {
                var ncr = Math.Max(0, context.NodeCornerRadius);
                foreach (var node in context.MappedNodes)
                {
                    dc.DrawRoundedRectangle(NodeBrush, null,
                        new Rect(offsetX + node.Left, offsetY + node.Top, node.Width, node.Height), ncr, ncr);
                }
            }

            // 视口指示块
            if (context.HasViewport && !context.MappedViewport.IsEmpty)
            {
                var vp = context.MappedViewport;
                var vr = new Rect(offsetX + vp.Left, offsetY + vp.Top, vp.Width, vp.Height);
                var ncr = Math.Max(0, context.NodeCornerRadius);
                if (ViewportFill is not null)
                    dc.DrawRoundedRectangle(ViewportFill, null, vr, ncr, ncr);
                if (ViewportStroke is not null)
                    dc.DrawRoundedRectangle(null, GetPen(ref _viewportPen, ViewportStroke, ViewportStrokeThickness), vr, ncr, ncr);
            }
        }
        finally
        {
            dc.Pop();
        }
    }
}
