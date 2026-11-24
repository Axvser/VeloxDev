using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A minimap overlay for WinUI that renders a thumbnail overview of all nodes,
/// links, and the visible viewport in the top-right corner.
/// Uses XAML shapes (Rectangle, Line) for rendering.
/// </summary>
/// <remarks>
/// Tree subscription, change tracking, content/viewport mapping, placement and drag math live in the Core
/// <see cref="MinimapPipeline"/>. This class keeps the dependency properties, the retained shape pool and
/// the pointer handlers that feed the input layer.
/// </remarks>
public class WorkflowMinimapOverlay : Canvas, IWorkflowMinimapOverlay
{
    // ── 依赖属性 ────────────────────────────────────────────────────────────

    public static readonly DependencyProperty ScrollOffsetXProperty =
        DependencyProperty.Register(nameof(ScrollOffsetX), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ScrollOffsetYProperty =
        DependencyProperty.Register(nameof(ScrollOffsetY), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ContentOffsetXProperty =
        DependencyProperty.Register(nameof(ContentOffsetX), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty ContentOffsetYProperty =
        DependencyProperty.Register(nameof(ContentOffsetY), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty WorkflowTreeProperty =
        DependencyProperty.Register(nameof(WorkflowTree), typeof(ITreeComponent), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(null, (d, e) => ((WorkflowMinimapOverlay)d).OnTreeChanged((ITreeComponent?)e.NewValue)));

    public static readonly DependencyProperty ViewportWidthProperty =
        DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1d, OnPropChanged));

    public static readonly DependencyProperty ViewportHeightProperty =
        DependencyProperty.Register(nameof(ViewportHeight), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1d, OnPropChanged));

    public static readonly DependencyProperty IsMinimapVisibleProperty =
        DependencyProperty.Register(nameof(IsMinimapVisible), typeof(bool), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(true, OnPropChanged));

    public static readonly DependencyProperty MinimapWidthProperty =
        DependencyProperty.Register(nameof(MinimapWidth), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(200d, OnPropChanged));

    public static readonly DependencyProperty MinimapHeightProperty =
        DependencyProperty.Register(nameof(MinimapHeight), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(140d, OnPropChanged));

    public static readonly DependencyProperty MinimapMarginProperty =
        DependencyProperty.Register(nameof(MinimapMargin), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(12d, OnPropChanged));

    public static readonly DependencyProperty AvailableRightProperty =
        DependencyProperty.Register(nameof(AvailableRight), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty AvailableTopProperty =
        DependencyProperty.Register(nameof(AvailableTop), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty AvailableBottomProperty =
        DependencyProperty.Register(nameof(AvailableBottom), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(0d, OnPropChanged));

    public static readonly DependencyProperty RulerThicknessProperty =
        DependencyProperty.Register(nameof(RulerThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(28d, OnPropChanged));

    public static readonly DependencyProperty LinkStrokeThicknessProperty =
        DependencyProperty.Register(nameof(LinkStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(2.0, OnPropChanged));

    public static readonly DependencyProperty MinimapBackgroundProperty =
        DependencyProperty.Register(nameof(MinimapBackground), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x1E, 0x1E)), OnPropChanged));

    public static readonly DependencyProperty MinimapBorderBrushProperty =
        DependencyProperty.Register(nameof(MinimapBorderBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(220, 148, 163, 184)), OnPropChanged));

    public static readonly DependencyProperty NodeBrushProperty =
        DependencyProperty.Register(nameof(NodeBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(220, 56, 189, 248)), OnPropChanged));

    public static readonly DependencyProperty LinkBrushProperty =
        DependencyProperty.Register(nameof(LinkBrush), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(180, 180, 200, 220)), OnPropChanged));

    public static readonly DependencyProperty ViewportStrokeProperty =
        DependencyProperty.Register(nameof(ViewportStroke), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), OnPropChanged));

    public static readonly DependencyProperty ViewportFillProperty =
        DependencyProperty.Register(nameof(ViewportFill), typeof(Brush), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), OnPropChanged));

    public static readonly DependencyProperty ViewportStrokeThicknessProperty =
        DependencyProperty.Register(nameof(ViewportStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1.5, OnPropChanged));

    public static readonly DependencyProperty MinimapCornerRadiusProperty =
        DependencyProperty.Register(nameof(MinimapCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(4.0, OnPropChanged));

    public static readonly DependencyProperty MinimapBorderThicknessProperty =
        DependencyProperty.Register(nameof(MinimapBorderThickness), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1.0, OnPropChanged));

    public static readonly DependencyProperty NodeCornerRadiusProperty =
        DependencyProperty.Register(nameof(NodeCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(1.0, OnPropChanged));

    public static readonly DependencyProperty ContentPaddingProperty =
        DependencyProperty.Register(nameof(ContentPadding), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(2.0, OnPropChanged));

    public static readonly DependencyProperty MinimapMinSizeProperty =
        DependencyProperty.Register(nameof(MinimapMinSize), typeof(double), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(20.0, OnPropChanged));

    public static readonly DependencyProperty ScrollViewerNameProperty =
        DependencyProperty.Register(nameof(ScrollViewerName), typeof(string), typeof(WorkflowMinimapOverlay),
            new PropertyMetadata(null));    // ── CLR accessors ────────────────────────────────────────────────────────

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
    private bool _isUnloaded;
    private ScrollViewer? _scrollViewer;

    // 形状池
    private readonly List<Rectangle> _nodeRects = [];
    private Rectangle? _viewportRect;
    private Rectangle? _bgRect;
    private Rectangle? _borderRect;

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _refreshTimer;

    public WorkflowMinimapOverlay()
    {
        // 全表面浮层：模板让它铺满表面，小地图自己画在 context 给出的“可用右上角”。
        // 不设 Background：Canvas 空处不可命中，指针才能透过它落到下面的画布。
        Background = null;

        inputHost = new MinimapInputHost(this);
        pipeline = new MinimapPipeline(new MinimapRenderer(this), new MinimapViewportHost(this), inputHost);
        pipeline.RenderRequested += (_, _) => MarkDirty();

        // 只在 UI 线程才订阅定时器
        try
        {
            _refreshTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.CreateTimer();
            if (_refreshTimer is not null)
            {
                _refreshTimer.Interval = TimeSpan.FromMilliseconds(16);
                _refreshTimer.Tick += (s, e) =>
                {
                    if (_isUnloaded) return;
                    try { RebuildShapes(); }
                    catch (System.Runtime.InteropServices.COMException) { /* app teardown: the DependencyObject may already be destroyed */ }
                };
            }
        }
        catch { }

        PointerPressed += OnPointerPressedHandler;
        PointerMoved += OnPointerMovedHandler;
        PointerReleased += OnPointerReleasedHandler;
        PointerCaptureLost += OnPointerCaptureLostHandler;
        Loaded += (s, e) =>
        {
            _isUnloaded = false;
            ResolveScrollViewer();
            // 卸载时退过订；重新挂回可视树要把同一棵树重新订上。
            pipeline.Tree = WorkflowTree;
        };
        Unloaded += (_, _) =>
        {
            // 防内存泄漏：覆盖层离树时退订所有树事件，清掉对 ViewModel 对象的强引用。
            // 先退订再停表：Tree 置空会触发一次 MarkDirty（可能重启定时器），停表放在它后面。
            pipeline.Tree = null;
            _isUnloaded = true;
            _refreshTimer?.Stop();
            pipeline.PointerCaptureLost();
            inputHost.IsPressed = false;
            inputHost.CurrentPointer = null;
        };
    }

    // 拖拽导航要把滚动写回宿主表面，宿主表面按名字解析。
    internal ScrollViewer? MinimapScrollViewer => _scrollViewer;

    private void ResolveScrollViewer()
    {
        if (string.IsNullOrWhiteSpace(ScrollViewerName)) return;
        // 上溯到 UserControl（名称作用域根）按名查找
        FrameworkElement? el = this;
        while (el is not null)
        {
            if (el is UserControl uc)
            {
                var found = uc.FindName(ScrollViewerName);
                if (found is ScrollViewer sv)
                {
                    if (_scrollViewer is not null) _scrollViewer.SizeChanged -= OnScrollViewerResized;
                    _scrollViewer = sv;
                    // 行为在 ViewChanged（滚动）时推 ViewportWidth，但视口尺寸变化不会触发 ViewChanged —— 且布局中途 ScrollViewer.ViewportWidth 会滞后。
                    // 在 ScrollViewer 自身 resize 时读它落定的视口，窗口缩小时可拖块才跟得上真实可见区。
                    _scrollViewer.SizeChanged += OnScrollViewerResized;
                }
                return;
            }
            el = VisualTreeHelper.GetParent(el) as FrameworkElement;
        }
    }

    private void OnScrollViewerResized(object? sender, SizeChangedEventArgs e)
    {
        // 延后到当前布局趟之后让尺寸落定，再推实际可见区并重渲染。ActualWidth/Height = 元素渲染尺寸（真实可见区），而 ScrollViewer.ViewportWidth 可能报有效/更大的值；窗口缩小时块必须缩到实际区域。
        // 纯 resize 不改 ScrollOffsetX/Y（块的左上角不动）。
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_scrollViewer is null) return;
            ViewportWidth = Math.Max(0, _scrollViewer.ActualWidth);
            ViewportHeight = Math.Max(0, _scrollViewer.ActualHeight);
            MarkDirty();
        });
    }

    // ── 设置同步 ────────────────────────────────────────────────────────────

    private void OnTreeChanged(ITreeComponent? tree) => pipeline.Tree = tree;

    private static void OnPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WorkflowMinimapOverlay)d).OnSettingChanged();

    private void OnSettingChanged()
    {
        RefreshPipelineSettings();
        pipeline.Invalidate();
    }

    private void MarkDirty()
    {
        // 树的通知可能来自任意线程；定时器与形状都只在 UI 线程上动。
        if (DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            queue.TryEnqueue(() => MarkDirty());
            return;
        }

        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        if (_refreshTimer is not null)
        {
            // 节流而非去抖：每次 MarkDirty 重启运行中的定时器会让连续平移期间它永不触发 —— 缩略图只在移动停止后重绘一次。
            // 只在空闲时启动，更新持续到来时它才按固定 16ms 节拍触发。
            if (!_refreshTimer.IsRunning)
            {
                _refreshTimer.Start();
            }
        }
        else
        {
            RebuildShapes();
        }
    }

    // 可用区来自 surface：右侧让开竖滚动条，顶部让开水平刻度，底部让开横滚动条。
    private void RefreshPipelineSettings()
    {
        var width = Math.Max(0, ActualWidth);
        var height = Math.Max(0, ActualHeight);
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
            MinimumThumbSize = 2d,
            Available = new SurfaceAvailableArea
            {
                Right = right,
                Top = top,
                Bottom = bottom,
                Margin = MinimapMargin,
            },
        };
        pipeline.IsVisible = IsMinimapVisible;

        // 输入层按放置矩形守卫命中：即便首帧还没画出来，按下的落点也必须与随后的绘制矩形一致。
        inputHost.ClampRect = pipeline.MinimapRect;
    }

    internal void ApplyScrollOffsets(double x, double y)
    {
        // 没解析到滚动容器时旧实现不导航：Core 会把上限当 0，直接写就会把画布拽回原点。
        if (_scrollViewer is null) return;

        // 走表面的统一写入口：它会记下在途目标并补偿画布平移；绕过它会让表面上一笔尚未落地的滚动
        // 把画面钉在旧位置。找不到表面时退回直接 ChangeView。
        if (!WorkflowSurfaceBehavior.TryRequestScroll(this, x, y))
        {
            _scrollViewer.ChangeView(x, y, null, true);
        }
    }

    // ── 指针 ────────────────────────────────────────────────────────────────

    private void OnPointerPressedHandler(object? sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        // 命中前按当前尺寸/可用区重算放置矩形，输入层的守卫才与这一帧画出来的缩略图一致。
        RefreshPipelineSettings();
        inputHost.PointerPosition = (point.Position.X, point.Position.Y);
        inputHost.IsPressed = true;
        inputHost.CurrentPointer = e.Pointer;
        if (pipeline.PointerPressed())
        {
            e.Handled = true;
        }
    }

    private void OnPointerMovedHandler(object? sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this).Position;
        inputHost.PointerPosition = (pt.X, pt.Y);
        if (!pipeline.IsDragging) return;

        pipeline.PointerMoved();
        e.Handled = true;
    }

    private void OnPointerReleasedHandler(object? sender, PointerRoutedEventArgs e)
    {
        inputHost.IsPressed = false;
        if (!pipeline.IsDragging)
        {
            inputHost.CurrentPointer = null;
            return;
        }

        pipeline.PointerReleased();
        inputHost.CurrentPointer = null;
        e.Handled = true;
    }

    private void OnPointerCaptureLostHandler(object? sender, PointerRoutedEventArgs e)
    {
        inputHost.IsPressed = false;
        inputHost.CurrentPointer = null;
        pipeline.PointerCaptureLost();
    }

    // ── 形状渲染 ────────────────────────────────────────────────────────────

    private void RebuildShapes()
    {
        _refreshTimer?.Stop();
        if (_isUnloaded) return;

        if (!IsMinimapVisible)
        {
            ClearShapes();
            return;
        }

        RefreshPipelineSettings();
        pipeline.Refresh();

        if (pipeline.DrawContext is null)
        {
            // 没有树或没有节点：Core 不调 renderer，但保留式画布必须自己收回旧形状并留下空框。
            DrawEmptyFrame();
            return;
        }

        pipeline.Render(this);
    }

    private void ClearShapes()
    {
        Children.Clear();
        _nodeRects.Clear();
        _viewportRect = _bgRect = _borderRect = null;
        Clip = null;
    }

    private void DrawEmptyFrame()
    {
        var metrics = pipeline.Metrics;
        DrawMinimap(new MinimapDrawContext
        {
            MinimapWidth = metrics.Width,
            MinimapHeight = metrics.Height,
            MinimapMargin = metrics.Available.Margin,
            AvailableRight = metrics.Available.Right,
            AvailableTop = metrics.Available.Top,
            AvailableBottom = metrics.Available.Bottom,
            NodeCornerRadius = metrics.NodeCornerRadius,
            MinimapCornerRadius = metrics.MinimapCornerRadius,
        });
    }

    internal void DrawMinimapSurface(MinimapDrawContext context) => DrawMinimap(context);

    /// <summary>
    /// Draws the minimap from <paramref name="context"/>. Override and call <c>base</c> to add to the default drawing.
    /// </summary>
    protected virtual void DrawMinimap(MinimapDrawContext context)
    {
        // 默认绘制把整块缩略图放在 context 给出的“可用右上角”，所有子形状都带这个原点偏移。
        var bounds = context.GetMinimapRect(MinimapMinSize);
        var mmRect = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        var offsetX = mmRect.Left;
        var offsetY = mmRect.Top;

        // 裁到缩略图矩形：缩略图之外的绘制与命中都不落在浮层上。
        var clip = Clip as RectangleGeometry;
        if (clip is null || !clip.Rect.Equals(mmRect))
            Clip = new RectangleGeometry { Rect = mmRect };

        // 确保背景/边框
        if (_bgRect is null)
        {
            _bgRect = new Rectangle();
            Children.Add(_bgRect);
        }
        _bgRect.Width = context.MinimapWidth;
        _bgRect.Height = context.MinimapHeight;
        SetLeft(_bgRect, offsetX);
        SetTop(_bgRect, offsetY);
        _bgRect.Fill = MinimapBackground;
        _bgRect.RadiusX = _bgRect.RadiusY = Math.Max(0, context.MinimapCornerRadius);

        if (MinimapBorderBrush is not null)
        {
            if (_borderRect is null)
            {
                _borderRect = new Rectangle();
                Children.Add(_borderRect);
            }
            _borderRect.Width = context.MinimapWidth;
            _borderRect.Height = context.MinimapHeight;
            SetLeft(_borderRect, offsetX);
            SetTop(_borderRect, offsetY);
            _borderRect.Stroke = MinimapBorderBrush;
            _borderRect.StrokeThickness = MinimapBorderThickness;
            _borderRect.RadiusX = _borderRect.RadiusY = Math.Max(0, context.MinimapCornerRadius);
        }

        if (!context.HasContent)
        {
            foreach (var r in _nodeRects) r.Visibility = Visibility.Collapsed;
            _viewportRect?.Visibility = Visibility.Collapsed;
            return;
        }

        // 节点
        while (_nodeRects.Count < context.MappedNodes.Count)
        {
            var rect = new Rectangle();
            _nodeRects.Add(rect);
            Children.Add(rect);
        }
        for (int i = 0; i < _nodeRects.Count; i++)
        {
            var rect = _nodeRects[i];
            if (i < context.MappedNodes.Count)
            {
                var node = context.MappedNodes[i];
                SetLeft(rect, offsetX + node.Left);
                SetTop(rect, offsetY + node.Top);
                rect.Width = node.Width;
                rect.Height = node.Height;
                rect.Fill = NodeBrush;
                rect.RadiusX = rect.RadiusY = context.NodeCornerRadius;
                rect.Visibility = Visibility.Visible;
            }
            else
            {
                rect.Visibility = Visibility.Collapsed;
            }
        }

        // 视口指示块
        if (context.HasViewport && !context.MappedViewport.IsEmpty)
        {
            if (_viewportRect is null)
            {
                _viewportRect = new Rectangle();
                Children.Add(_viewportRect);
            }
            var vp = context.MappedViewport;
            SetLeft(_viewportRect, offsetX + vp.Left);
            SetTop(_viewportRect, offsetY + vp.Top);
            _viewportRect.Width = vp.Width;
            _viewportRect.Height = vp.Height;
            _viewportRect.Fill = ViewportFill;
            _viewportRect.Stroke = ViewportStroke;
            _viewportRect.StrokeThickness = ViewportStrokeThickness;
            _viewportRect.RadiusX = _viewportRect.RadiusY = context.NodeCornerRadius;
            _viewportRect.Visibility = Visibility.Visible;
        }
        else
        {
            _viewportRect?.Visibility = Visibility.Collapsed;
        }
    }
}
