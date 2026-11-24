using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PlatformInput = Microsoft.Maui.Controls;
using Wf = VeloxDev.WorkflowSystem;
using System;
using VeloxDev.WorkflowSystem.ViewHosting;
#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
#endif

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A full-surface minimap layer for MAUI. The layer fills its host and draws the minimap in the content
/// viewport's top-right corner, below the horizontal ruler band and clear of the scrollbars (MAUI's
/// overlay scrollbars reserve no space).
/// Only the drawn minimap rectangle receives input: an outer, non-interactive drawing surface covers the
/// whole host, while a second surface sized to the minimap rectangle carries the interactions, so canvas
/// gestures pass through everywhere else.
/// </summary>
/// <remarks>
/// Tree subscription, content/viewport mapping, placement and drag math live in the Core
/// <see cref="MinimapPipeline"/>. This class keeps the bindable properties, the platform drawing, the
/// two-surface layout, and the MAUI/WinUI input plumbing that feeds the input layer.
/// </remarks>
public class WorkflowMinimapOverlay : Grid, IDrawable, IWorkflowMinimapOverlay
{
    // ── 可绑定属性 ──────────────────────────────────────────────────────────

    private static void OnVisualProp(BindableObject b, object o, object n)
        => ((WorkflowMinimapOverlay)b).OnSettingChanged();

    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(nameof(ScrollOffsetX), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(nameof(ScrollOffsetY), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(nameof(ContentOffsetX), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(nameof(ContentOffsetY), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportWidthProperty = BindableProperty.Create(nameof(ViewportWidth), typeof(double), typeof(WorkflowMinimapOverlay), 1d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportHeightProperty = BindableProperty.Create(nameof(ViewportHeight), typeof(double), typeof(WorkflowMinimapOverlay), 1d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty IsMinimapVisibleProperty = BindableProperty.Create(nameof(IsMinimapVisible), typeof(bool), typeof(WorkflowMinimapOverlay), true, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapWidthProperty = BindableProperty.Create(nameof(MinimapWidth), typeof(double), typeof(WorkflowMinimapOverlay), 200d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapHeightProperty = BindableProperty.Create(nameof(MinimapHeight), typeof(double), typeof(WorkflowMinimapOverlay), 140d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapMarginProperty = BindableProperty.Create(nameof(MinimapMargin), typeof(double), typeof(WorkflowMinimapOverlay), 12d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty AvailableRightProperty = BindableProperty.Create(nameof(AvailableRight), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty AvailableTopProperty = BindableProperty.Create(nameof(AvailableTop), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty AvailableBottomProperty = BindableProperty.Create(nameof(AvailableBottom), typeof(double), typeof(WorkflowMinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty RulerThicknessProperty = BindableProperty.Create(nameof(RulerThickness), typeof(double), typeof(WorkflowMinimapOverlay), 28d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty LinkStrokeThicknessProperty = BindableProperty.Create(nameof(LinkStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay), 2.0, propertyChanged: OnVisualProp);

    public static readonly BindableProperty MinimapBackgroundColorProperty = BindableProperty.Create(nameof(MinimapBackgroundColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(30, 30, 30, 51), propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapBorderColorProperty = BindableProperty.Create(nameof(MinimapBorderColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(148, 163, 184, 220), propertyChanged: OnVisualProp);
    public static readonly BindableProperty NodeFillColorProperty = BindableProperty.Create(nameof(NodeFillColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(56, 189, 248, 220), propertyChanged: OnVisualProp);
    public static readonly BindableProperty LinkStrokeColorProperty = BindableProperty.Create(nameof(LinkStrokeColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(180, 200, 220, 180), propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportStrokeColorProperty = BindableProperty.Create(nameof(ViewportStrokeColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(255, 255, 255, 240), propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportFillColorProperty = BindableProperty.Create(nameof(ViewportFillColor), typeof(Color), typeof(WorkflowMinimapOverlay), Color.FromRgba(255, 255, 255, 40), propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportStrokeThicknessProperty = BindableProperty.Create(nameof(ViewportStrokeThickness), typeof(double), typeof(WorkflowMinimapOverlay), 1.5, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapCornerRadiusProperty = BindableProperty.Create(nameof(MinimapCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay), 4.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapBorderThicknessProperty = BindableProperty.Create(nameof(MinimapBorderThickness), typeof(double), typeof(WorkflowMinimapOverlay), 1.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty NodeCornerRadiusProperty = BindableProperty.Create(nameof(NodeCornerRadius), typeof(double), typeof(WorkflowMinimapOverlay), 1.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentPaddingProperty = BindableProperty.Create(nameof(ContentPadding), typeof(double), typeof(WorkflowMinimapOverlay), 2.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty MinimapMinSizeProperty = BindableProperty.Create(nameof(MinimapMinSize), typeof(double), typeof(WorkflowMinimapOverlay), 20.0, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ScrollViewerNameProperty = BindableProperty.Create(nameof(ScrollViewerName), typeof(string), typeof(WorkflowMinimapOverlay));

    public static readonly BindableProperty WorkflowTreeProperty = BindableProperty.Create(nameof(WorkflowTree), typeof(ITreeComponent), typeof(WorkflowMinimapOverlay), null,
        propertyChanged: (b, o, n) => ((WorkflowMinimapOverlay)b).OnTreeChanged((ITreeComponent?)n));

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
    public Color? MinimapBackgroundColor { get => (Color?)GetValue(MinimapBackgroundColorProperty); set => SetValue(MinimapBackgroundColorProperty, value); }
    public Color? MinimapBorderColor { get => (Color?)GetValue(MinimapBorderColorProperty); set => SetValue(MinimapBorderColorProperty, value); }
    public Color? NodeFillColor { get => (Color?)GetValue(NodeFillColorProperty); set => SetValue(NodeFillColorProperty, value); }
    public Color? LinkStrokeColor { get => (Color?)GetValue(LinkStrokeColorProperty); set => SetValue(LinkStrokeColorProperty, value); }
    public Color? ViewportStrokeColor { get => (Color?)GetValue(ViewportStrokeColorProperty); set => SetValue(ViewportStrokeColorProperty, value); }
    public Color? ViewportFillColor { get => (Color?)GetValue(ViewportFillColorProperty); set => SetValue(ViewportFillColorProperty, value); }
    public double ViewportStrokeThickness { get => (double)GetValue(ViewportStrokeThicknessProperty); set => SetValue(ViewportStrokeThicknessProperty, value); }
    public double MinimapCornerRadius { get => (double)GetValue(MinimapCornerRadiusProperty); set => SetValue(MinimapCornerRadiusProperty, value); }
    public double MinimapBorderThickness { get => (double)GetValue(MinimapBorderThicknessProperty); set => SetValue(MinimapBorderThicknessProperty, value); }
    public double NodeCornerRadius { get => (double)GetValue(NodeCornerRadiusProperty); set => SetValue(NodeCornerRadiusProperty, value); }
    public double ContentPadding { get => (double)GetValue(ContentPaddingProperty); set => SetValue(ContentPaddingProperty, value); }
    public double MinimapMinSize { get => (double)GetValue(MinimapMinSizeProperty); set => SetValue(MinimapMinSizeProperty, value); }
    public string? ScrollViewerName { get => (string?)GetValue(ScrollViewerNameProperty); set => SetValue(ScrollViewerNameProperty, value); }

    // ── 内部类型 ────────────────────────────────────────────────────────────

    // 输入层只负责命中与手势，不画任何东西。
    private sealed class NoopDrawable : IDrawable
    {
        public static readonly NoopDrawable Instance = new();

        public void Draw(ICanvas canvas, RectF dirtyRect) { }
    }

    // ── 状态 ────────────────────────────────────────────────────────────────

    // 全表面绘制层（从不吃输入）与只覆盖小地图矩形的输入层。
    private readonly GraphicsView _drawView;
    private readonly GraphicsView _inputView;

    private readonly MinimapPipeline _pipeline;
    private readonly MinimapInputHost _inputHost;
    private bool _invalidatePending;
    // 最近一次放置的矩形（浮层局部坐标）；输入层的子坐标从这里换到浮层坐标。
    private RectF _minimapRect;
    private ContentView? _parentView;
    private ScrollView? _scrollView;
    // 目标没有 2px 以上变化就跳过 ScrollToAsync；点击/新手势前重置。
    private double _lastScrollX = double.MinValue;
    private double _lastScrollY = double.MinValue;
    // 越界导航可能让 Core 改写画布模型的 Negative/PositiveOffset；画布元素只经表面的 ApplyLayout 长大，
    // 所以尺寸一变就让表面刷新一趟（与旧的 layoutChanged 分支同一件事）。
    private double _lastCanvasWidth = double.NaN;
    private double _lastCanvasHeight = double.NaN;
    // Windows 上平移手势与原生指针探针会同时活着。只让其中一个驱动管线，否则绝对指针坐标与
    // 手势累计增量交替成为目标，X/Y 会以各自的节奏抖。非 Windows 目标两条标记恒为 false。
    private bool _panActive = false;
    private bool _nativePointerActive = false;
#if WINDOWS
    // 原生平移手势借用输入层 GraphicsView 已捕获的 manipulation 管线（ManipulationMode=All），指针移出缩略图后增量照常到达；
    // 路由的 PointerMoved 监听不行 —— 一旦开始 manipulation，只有捕获者收得到指针事件，它会停在边界。
    private PanGestureRecognizer? _panGesture;
    private float _dragStartX;
    private float _dragStartY;
    private PointerEventHandler? _nativePressedHandler;
    private PointerEventHandler? _nativeMovedHandler;
    private PointerEventHandler? _nativeReleasedHandler;
    // 每个 handler 世代允许一次延后重试：HandlerChanged 早于平台视图创建，所以第一次挂接可以到 UI 线程重试。
    private bool _platformAttachPending;
#endif
    private PointerGestureRecognizer? _parentPointerRecognizer;

    public WorkflowMinimapOverlay()
    {
        // 全表面浮层：本元素自身不吃输入（CascadeInputTransparent=false 让子元素仍可命中），
        // 绘制层始终 InputTransparent；只有小地图矩形上的输入层接收指针。
        InputTransparent = true;
        CascadeInputTransparent = false;

        _inputHost = new MinimapInputHost();
        _pipeline = new MinimapPipeline(new MinimapRenderer(this), new MinimapViewportHost(this), _inputHost);
        _pipeline.RenderRequested += OnPipelineRenderRequested;

        _drawView = new GraphicsView
        {
            Drawable = this,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
        _inputView = new GraphicsView
        {
            Drawable = NoopDrawable.Instance,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
        };
        Children.Add(_drawView);
        Children.Add(_inputView);

        _inputView.StartInteraction += OnStartInteraction;
        _inputView.DragInteraction += OnDragInteraction;
        _inputView.EndInteraction += OnEndInteraction;
        _inputView.HandlerChanged += OnInputHandlerChanged;
        _inputView.HandlerChanging += OnInputHandlerChanging;
        Loaded += OnLoaded;

#if WINDOWS
        // 静态挂接（而非拖拽开始时才挂），这样即使手势压掉了 MAUI 在输入层上的触摸交互，按下也总被捕获；
        // 拖拽结束由本手势的 Completed/Canceled 负责。
        _panGesture = new PanGestureRecognizer();
        _panGesture.PanUpdated += OnPanUpdated;
        _inputView.GestureRecognizers.Add(_panGesture);
#endif
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateInputPlacement(width, height);
        MarkDirty();
    }

    private void OnLoaded(object? s, EventArgs e)
    {
        // 沿父链上溯找到根 ContentView（WorkflowView）；用于查 ScrollView 与拖出边界后的指针跟踪。
        Element? el = this;
        while (el is not null)
        {
            if (el is ContentView cv)
            {
                _parentView ??= cv;
                if (!string.IsNullOrWhiteSpace(ScrollViewerName) && _scrollView is null)
                    _scrollView = cv.FindByName<ScrollView>(ScrollViewerName);
                break;
            }
            el = el.Parent;
        }

        UpdateInputPlacement();

#if WINDOWS
        // 元素已进入活动树、平台视图已存在，在这里挂原生指针处理器。
        AttachPlatformPressedHandler();
#endif
    }

    private void OnInputHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        // 新的 handler 世代：若此刻平台视图还没建，允许再安排一次延后重试。
        _platformAttachPending = false;
        AttachPlatformPressedHandler();
#endif
    }

    private void OnInputHandlerChanging(object? sender, HandlerChangingEventArgs args)
    {
#if WINDOWS
        // 从要离开的平台视图上摘掉处理器，避免 handler 重建（如页面导航）时泄漏。
        if (args.OldHandler?.PlatformView is UIElement oldEl)
        {
            DetachPlatformPressedHandler(oldEl);
        }
#endif
    }

#if WINDOWS
    /// <summary>
    /// Hooks the minimap input surface's own platform element. The press always originates there, so
    /// these handlers fire even when the pan gesture claims the manipulation and suppresses MAUI's
    /// touch interaction (StartInteraction). With the GraphicsView's manipulation holding pointer
    /// capture, PointerMoved on this same element keeps arriving outside the bounds — a second,
    /// independent out-of-bounds input path on top of the pan gesture's manipulation deltas.
    /// </summary>
    private void AttachPlatformPressedHandler()
    {
        if (_nativePressedHandler is not null) return;
        if (_inputView.Handler?.PlatformView is not UIElement mmEl)
        {
            // HandlerChanged 早于 MAUI 创建平台视图（创建发生在 handler 的 Setup 里）；延后一拍，UI 线程处理时原生元素已存在。
            // 由 _platformAttachPending 限界，永不出现的视图不会空转。
            if (_platformAttachPending) return;
            _platformAttachPending = true;
            Dispatcher.Dispatch(() => AttachPlatformPressedHandler());
            return;
        }
        _nativePressedHandler = (s, e) =>
        {
            if (_inputView.Handler?.PlatformView is not UIElement mm) return;

            RefreshPipelineSettingsForCurrentSize();
            _minimapRect = ToRectF(_pipeline.MinimapRect);

            var pt = e.GetCurrentPoint(mm).Position;
            if (!_pipeline.HitTest(pt.X + _minimapRect.Left, pt.Y + _minimapRect.Top)) return;

            // 新的一次按下：清掉上一次可能没收到 Completed 的手势标记；本次手势若随后 Started 会重新置位。
            _panActive = false;
            _nativePointerActive = true;
            // 尽力显式捕获：捕获期间只有此元素发指针事件，下面的 PointerMoved 移出边界也继续流动，直到释放（只能在 PointerPressed 期间取得）。
            mm.CapturePointer(e.Pointer);
            // MAUI 的 StartInteraction 先经类 handler 再到本实例 handler，所以交互跑时管线已在拖拽，
            // 这里不能再用探针的坐标覆盖它已经记下的起点（两套坐标来源一旦混用，X/Y 会各走各的）。
            if (_pipeline.IsDragging) return;
            _dragStartX = (float)pt.X;
            _dragStartY = (float)pt.Y;
            PressAt(pt.X + _minimapRect.Left, pt.Y + _minimapRect.Top);
        };
        _nativeMovedHandler = (s, e) =>
        {
            if (!_pipeline.IsDragging) return;
            // 原生探针是 Windows 上的首选：绝对坐标与缩略图同一套 DIP。PanGesture 的 TotalX/Y
            // 在 WinUI 上会带屏幕/元素偏移，拿它加到局部起点上会把目标一次推出上千像素。
            if (_inputView.Handler?.PlatformView is not UIElement mm) return;
            var pt = e.GetCurrentPoint(mm).Position;
            MoveTo(pt.X + _minimapRect.Left, pt.Y + _minimapRect.Top);
        };
        _nativeReleasedHandler = (s, e) =>
        {
            _nativePointerActive = false;
            ReleaseDrag();
        };
        mmEl.AddHandler(UIElement.PointerPressedEvent, _nativePressedHandler, true);
        mmEl.AddHandler(UIElement.PointerMovedEvent, _nativeMovedHandler, true);
        mmEl.AddHandler(UIElement.PointerReleasedEvent, _nativeReleasedHandler, true);
        mmEl.AddHandler(UIElement.PointerCanceledEvent, _nativeReleasedHandler, true);
        mmEl.AddHandler(UIElement.PointerCaptureLostEvent, _nativeReleasedHandler, true);
    }

    private void DetachPlatformPressedHandler(UIElement mmEl)
    {
        if (_nativePressedHandler is not null)
        {
            mmEl.RemoveHandler(UIElement.PointerPressedEvent, _nativePressedHandler);
            _nativePressedHandler = null;
        }
        if (_nativeMovedHandler is not null)
        {
            mmEl.RemoveHandler(UIElement.PointerMovedEvent, _nativeMovedHandler);
            _nativeMovedHandler = null;
        }
        if (_nativeReleasedHandler is not null)
        {
            mmEl.RemoveHandler(UIElement.PointerReleasedEvent, _nativeReleasedHandler);
            mmEl.RemoveHandler(UIElement.PointerCanceledEvent, _nativeReleasedHandler);
            mmEl.RemoveHandler(UIElement.PointerCaptureLostEvent, _nativeReleasedHandler);
            _nativeReleasedHandler = null;
        }
    }

    /// <summary>
    /// Drives the drag from native manipulation deltas. The manipulation holds pointer
    /// capture, so Running keeps arriving after the cursor leaves the minimap and only
    /// ends at the real release (Completed/Canceled).
    /// </summary>
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panActive = true;
                break;
            case GestureStatus.Running:
                // 原生探针已经按下时由它按绝对坐标驱动；只在探针没挂上时用累计增量兜底。
                if (_pipeline.IsDragging && _panActive && !_nativePointerActive)
                {
                    MoveTo(_dragStartX + e.TotalX + _minimapRect.Left,
                           _dragStartY + e.TotalY + _minimapRect.Top);
                }
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _panActive = false;
                // 原生探针还在驱动时由它的释放/取消处理器收尾。
                if (!_nativePointerActive) ReleaseDrag();
                break;
        }
    }
#endif

    // ── 数据刷新 ─────────────────────────────────────────────────────────────

    private void OnTreeChanged(ITreeComponent? newTree)
    {
        _pipeline.Tree = newTree;
        MarkDirty();
    }

    // 没有节点就没有可画的小地图：输入层此时必须让开，否则右上角会凭空吞掉画布手势。
    private bool HasMinimapContent => WorkflowTree?.Nodes is { Count: > 0 };

    private void OnPipelineRenderRequested(object? s, EventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        // 一帧内会连续写多个视觉 DP（ApplyVisibleRegion 一起写 ScrollOffsetX/Y、ContentOffsetX/Y、ViewportWidth/Height），
        // 拖缩略图时每个滚动增量又写一遍；每次 DP 写都是一次完整重绘，所以改为在下次主线程派发时统一冲刷，一帧只重绘一次。
        if (_invalidatePending)
        {
            return;
        }

        _invalidatePending = true;
        // Invalidate 必须在主线程；BeginInvokeOnMainThread 既调度后台来源，又给出合并窗口，同帧内连续写入因此合并成一次冲刷。
        MainThread.BeginInvokeOnMainThread(FlushInvalidate);
    }

    private void FlushInvalidate()
    {
        _invalidatePending = false;
        // 输入层随可用区/树内容一起在同一帧更新：MarkDirty 的来者可能来自后台线程，
        // 而 Margin / WidthRequest / IsVisible 只能在主线程写。
        UpdateInputPlacement();
        _drawView.Invalidate();
    }

    private void OnSettingChanged()
    {
        RefreshPipelineSettingsForCurrentSize();
        // 视口偏移不在 MinimapMetrics 里，Metrics 的相等比较看不出它变了；显式标脏让下一趟重算映射。
        // Invalidate 会经 RenderRequested 走到 MarkDirty。
        _pipeline.Invalidate();
    }

    // ── 布局与绘制中间量 ────────────────────────────────────────────────────

    private (double W, double H) CurrentSurfaceSize()
    {
        var w = SafeDim(Width, SafeDim(_drawView.Width, 0));
        var h = SafeDim(Height, SafeDim(_drawView.Height, 0));
        return (w, h);
    }

    private void RefreshPipelineSettingsForCurrentSize()
    {
        var (w, h) = CurrentSurfaceSize();
        RefreshPipelineSettings(w, h);
    }

    // 可用区来自 surface：右侧让开竖滚动条，顶部让开水平刻度，底部让开横滚动条。
    // MAUI 的滚动条是覆盖式的（不占宽高），surface 因此把整幅宽高作为可用区传下来。
    private void RefreshPipelineSettings(double availableWidth, double availableHeight)
    {
        var width = SafeDim(availableWidth, 0);
        var height = SafeDim(availableHeight, 0);
        var top = double.IsNaN(AvailableTop) ? 0 : Math.Max(0, AvailableTop);
        var right = double.IsNaN(AvailableRight) ? 0 : AvailableRight;
        var bottom = double.IsNaN(AvailableBottom) ? 0 : AvailableBottom;

        _pipeline.Metrics = new MinimapMetrics
        {
            Width = SafeNonNegative(MinimapWidth),
            Height = SafeNonNegative(MinimapHeight),
            MinSize = SafeNonNegative(MinimapMinSize),
            Padding = SafeNonNegative(ContentPadding),
            NodeCornerRadius = SafeNonNegative(NodeCornerRadius),
            MinimapCornerRadius = SafeNonNegative(MinimapCornerRadius),
            MinimumThumbSize = 2d,
            Available = new SurfaceAvailableArea
            {
                Right = right > 0 ? right : width,
                Top = top,
                Bottom = bottom > top ? bottom : height,
                Margin = SafeNonNegative(MinimapMargin),
            },
        };
        _pipeline.IsVisible = IsMinimapVisible;
    }

    private void UpdateInputPlacement()
    {
        var (w, h) = CurrentSurfaceSize();
        UpdateInputPlacement(w, h);
    }

    // 把输入层贴到小地图矩形上：矩形外的一切命中因此落回画布。
    private void UpdateInputPlacement(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            _inputView.IsVisible = false;
            return;
        }

        RefreshPipelineSettings(width, height);
        var bounds = _pipeline.MinimapRect;
        _minimapRect = ToRectF(bounds);

        _inputView.Margin = new Microsoft.Maui.Thickness(bounds.Left, bounds.Top, 0, 0);
        if (Math.Abs(_inputView.WidthRequest - bounds.Width) > 0.5) _inputView.WidthRequest = bounds.Width;
        if (Math.Abs(_inputView.HeightRequest - bounds.Height) > 0.5) _inputView.HeightRequest = bounds.Height;
        _inputView.IsVisible = IsMinimapVisible && HasMinimapContent && bounds.Width > 0 && bounds.Height > 0;
    }

    private static RectF ToRectF(WorkflowBounds bounds)
        => new((float)bounds.Left, (float)bounds.Top, (float)bounds.Width, (float)bounds.Height);

    // ── 触摸输入 ────────────────────────────────────────────────────────────

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
            if (_pipeline.IsDragging) return;
            RefreshPipelineSettingsForCurrentSize();
            _minimapRect = ToRectF(_pipeline.MinimapRect);

            if (e.Touches is null || e.Touches.Length == 0) return;
            var pt = e.Touches[0];
            if (!_pipeline.HitTest(pt.X + _minimapRect.Left, pt.Y + _minimapRect.Top)) return;

            // 与 Jalium 家一致：点击点一律成为视口中心 —— 指示块上没有抓取锚点，按在哪里都重新居中。
#if WINDOWS
            _dragStartX = pt.X;
            _dragStartY = pt.Y;
#endif
            PressAt(pt.X + _minimapRect.Left, pt.Y + _minimapRect.Top);
            if (_pipeline.IsDragging) SubscribeDragCapture();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // WinUI 上 GraphicsView 触摸事件可能带陈旧/空的 Touches 触发（dotnet/maui #13452）；无害。
            System.Diagnostics.Debug.WriteLine($"[Minimap] StartInteraction error: {ex.Message}");
        }
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
            if (!_pipeline.IsDragging) return;
            // Windows 上手势/原生探针已经在驱动；MAUI 触摸路径不要再补一份。
            if (_panActive || _nativePointerActive) return;
            if (e.Touches is null || e.Touches.Length == 0) return;
            var pt = e.Touches[0];
            MoveTo(pt.X + _minimapRect.Left, pt.Y + _minimapRect.Top);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"[Minimap] DragInteraction error: {ex.Message}");
        }
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
#if WINDOWS
            // MAUI 在指针一离开缩略图时就发 EndInteraction，释放时又发一次；Windows 上拖拽结束归原生处理器
            // （mmEl PointerReleased / pan Completed），它们能持续投递越界移动、在真正释放处才结束，所以这里不能因 EndInteraction 拆掉拖拽。
            return;
#else
            ReleaseDrag();
            UnsubscribeDragCapture();
#endif
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"[Minimap] EndInteraction error: {ex.Message}");
        }
    }

    // 按下：重置 2px 节流窗口，让输入层把这次点击交给管线。
    private void PressAt(double overlayX, double overlayY)
    {
        _lastScrollX = double.MinValue;
        _lastScrollY = double.MinValue;
        _inputHost.PointerPosition = (overlayX, overlayY);
        _inputHost.IsPressed = true;
        _pipeline.PointerPressed();
    }

    private void MoveTo(double overlayX, double overlayY)
    {
        _inputHost.PointerPosition = (overlayX, overlayY);
        _pipeline.PointerMoved();
    }

    private void ReleaseDrag()
    {
        _inputHost.IsPressed = false;
        _pipeline.PointerReleased();
    }

    private void SubscribeDragCapture()
    {
#if WINDOWS
        // 空操作：原生平移手势与平台 PointerPressed 探针都是静态挂接（构造/HandlerChanged），没有可懒订阅的东西；
        // 拖拽结束由 pan 手势的 Completed/Canceled 负责。
#else
        SubscribeParentGestureCapture();
#endif
    }

    private void UnsubscribeDragCapture()
    {
#if WINDOWS
        // 空操作：静态处理器在视图生命周期内一直挂着，在 HandlerChanging 里摘除。
#else
        UnsubscribeParentGestureCapture();
#endif
    }

    private void SubscribeParentGestureCapture()
    {
        if (_parentView is null || _parentPointerRecognizer is not null) return;
        _parentPointerRecognizer = new PointerGestureRecognizer();
        _parentPointerRecognizer.PointerMoved += OnParentPointerMoved;
        _parentPointerRecognizer.PointerReleased += OnParentPointerReleased;
        _parentView.GestureRecognizers.Add(_parentPointerRecognizer);
    }

    private void UnsubscribeParentGestureCapture()
    {
        if (_parentPointerRecognizer is null || _parentView is null) return;
        _parentView.GestureRecognizers.Remove(_parentPointerRecognizer);
        _parentPointerRecognizer.PointerMoved -= OnParentPointerMoved;
        _parentPointerRecognizer.PointerReleased -= OnParentPointerReleased;
        _parentPointerRecognizer = null;
    }

    private void OnParentPointerMoved(object? sender, PlatformInput.PointerEventArgs e)
    {
        if (!_pipeline.IsDragging) return;
        // 父视图的指针事件已经是浮层局部坐标，与管线一致。
        var pos = e.GetPosition(this);
        if (pos is null) return;
        MoveTo(pos.Value.X, pos.Value.Y);
    }

    private void OnParentPointerReleased(object? sender, PlatformInput.PointerEventArgs e)
    {
        ReleaseDrag();
        UnsubscribeDragCapture();
    }

    // ── 滚动写回（由 viewport 宿主调用）────────────────────────────────────

    internal double SafeViewportWidth => SafeDim(ViewportWidth, SafeDim(_scrollView?.Width ?? 0, 1));

    internal double SafeViewportHeight => SafeDim(ViewportHeight, SafeDim(_scrollView?.Height ?? 0, 1));

    // 逻辑最大滚动取自画布模型（ActualSize − 视口），不用 ScrollView.ContentSize —— 后者滞后于异步 MAUI 布局。
    internal double ModelScrollWidth => ModelScrollRange(_scrollView?.Width, WorkflowTree is { } tree ? tree.Layout.ActualSize.Width : 0d);

    internal double ModelScrollHeight => ModelScrollRange(_scrollView?.Height, WorkflowTree is { } tree ? tree.Layout.ActualSize.Height : 0d);

    internal void ApplyScrollOffsets(double x, double y)
    {
        // 节流：目标没有实质变化就跳过 ScrollToAsync。
        if (Math.Abs(x - _lastScrollX) < 2d && Math.Abs(y - _lastScrollY) < 2d)
        {
            return;
        }

        _lastScrollX = x;
        _lastScrollY = y;
        SafeScrollTo(_scrollView, x, y);

        // 越界导航改变了画布模型尺寸：让表面把新几何推到画布元素上。
        if (_parentView is not null && WorkflowTree is { } tree)
        {
            var size = tree.Layout.ActualSize;
            if (size.Width != _lastCanvasWidth || size.Height != _lastCanvasHeight)
            {
                _lastCanvasWidth = size.Width;
                _lastCanvasHeight = size.Height;
                WorkflowSurfaceBehavior.Refresh(_parentView);
            }
        }
    }

    private static double ModelScrollRange(double? viewport, double content)
    {
        var v = viewport ?? 0d;
        if (!double.IsFinite(v) || v <= 0d || !double.IsFinite(content))
        {
            return 0d;
        }

        return Math.Max(0d, content - v);
    }

    /// <summary>
    /// Call ScrollToAsync with exception protection.  NaN/Infinity values
    /// cause ArgumentException crashes in the native scroll viewer.
    /// </summary>
    private static void SafeScrollTo(ScrollView? sv, double x, double y)
    {
        if (sv is null) return;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        try
        {
            _ = sv.ScrollToAsync(x, y, false);
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"[Minimap] Scroll error: {ex.Message}");
        }
    }

    // ── IDrawable 实现 ─────────────────────────────────────────────────────

    void IDrawable.Draw(ICanvas canvas, RectF dirtyRect)
    {
        try
        {
            // WinUI 上 MAUI GraphicsView 的 Draw 回调没有异常保护（dotnet/maui #14567）；
            // 渲染调用抛异常（如 NaN 坐标）会直接漏进 WinUI 的 UnhandledException。
            if (!IsMinimapVisible) return;

            // dirtyRect.Width/Height 可能为 NaN（已知 MAUI WinUI 问题）；必须用 IsNaN 判断 —— NaN > 0 为假，NaN <= 0 也为假。
            var dw = dirtyRect.Width;
            var dh = dirtyRect.Height;
            var w = (!float.IsNaN(dw) && dw > 0) ? dw : (float)SafeDim(Width, 0);
            var h = (!float.IsNaN(dh) && dh > 0) ? dh : (float)SafeDim(Height, 0);
            if (w <= 0 || h <= 0) return;

            RefreshPipelineSettings(w, h);
            _pipeline.Render(canvas);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Minimap] Draw error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Draws the minimap from <paramref name="context"/>. Override and call <c>base</c> to add to the default drawing.
    /// </summary>
    protected virtual void DrawMinimap(ICanvas canvas, MinimapDrawContext context)
    {
        // 节点坐标可能处于布局过渡期（NaN）；拟合比例或矩形一旦非有限，整趟绘制都没有意义。
        if (!double.IsFinite(context.Scale) || context.Scale <= 0d) return;

        var bounds = context.GetMinimapRect(MinimapMinSize);
        if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top)
            || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
        {
            return;
        }

        // 默认绘制把整幅缩略图平移到矩形左上角：context 里的映射仍是缩略图局部坐标（原点 0,0）。
        var offsetX = (float)bounds.Left;
        var offsetY = (float)bounds.Top;
        var mmW = (float)context.MinimapWidth;
        var mmH = (float)context.MinimapHeight;
        if (!float.IsFinite(mmW) || !float.IsFinite(mmH) || mmW <= 0f || mmH <= 0f) return;
        var cr = Math.Max(0, (float)context.MinimapCornerRadius);

        if (MinimapBackgroundColor is not null)
        {
            canvas.FillColor = MinimapBackgroundColor;
            canvas.FillRoundedRectangle(offsetX, offsetY, mmW, mmH, cr);
        }
        if (MinimapBorderColor is not null)
        {
            canvas.StrokeColor = MinimapBorderColor;
            canvas.StrokeSize = (float)MinimapBorderThickness;
            canvas.DrawRoundedRectangle(offsetX, offsetY, mmW, mmH, cr);
        }

        canvas.SaveState();
        canvas.ClipRectangle(offsetX, offsetY, mmW, mmH);
        try
        {
            if (NodeFillColor is not null)
            {
                canvas.FillColor = NodeFillColor;
                var ncr = Math.Max(0, (float)context.NodeCornerRadius);
                foreach (var node in context.MappedNodes)
                {
                    if (!IsFinite(node)) continue;
                    canvas.FillRoundedRectangle(
                        offsetX + (float)node.Left, offsetY + (float)node.Top,
                        (float)node.Width, (float)node.Height, ncr);
                }
            }

            if (context.HasViewport && !context.MappedViewport.IsEmpty && IsFinite(context.MappedViewport))
            {
                var vp = context.MappedViewport;
                var ncr = Math.Max(0, (float)context.NodeCornerRadius);

                if (ViewportFillColor is not null)
                {
                    canvas.FillColor = ViewportFillColor;
                    canvas.FillRoundedRectangle(offsetX + (float)vp.Left, offsetY + (float)vp.Top, (float)vp.Width, (float)vp.Height, ncr);
                }
                if (ViewportStrokeColor is not null)
                {
                    canvas.StrokeColor = ViewportStrokeColor;
                    canvas.StrokeSize = (float)ViewportStrokeThickness;
                    canvas.DrawRoundedRectangle(offsetX + (float)vp.Left, offsetY + (float)vp.Top, (float)vp.Width, (float)vp.Height, ncr);
                }
            }
        }
        finally { canvas.RestoreState(); }
    }

    internal void DrawMinimapSurface(ICanvas canvas, MinimapDrawContext context) => DrawMinimap(canvas, context);

    private static bool IsFinite(WorkflowBounds bounds)
        => double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top)
            && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height);

    private static double SafeDim(double value, double fallback)
        => double.IsNaN(value) || value <= 0 ? fallback : value;

    private static double SafeNonNegative(double value)
        => double.IsNaN(value) ? 0d : Math.Max(0d, value);
}
