using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PlatformInput = Microsoft.Maui.Controls;
using Wf = VeloxDev.WorkflowSystem;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
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
public class WorkflowMinimapOverlay : Grid, IDrawable, IWorkflowMinimapOverlay
{
    // ── 可绑定属性 ──────────────────────────────────────────────────────────

    private static void OnVisualProp(BindableObject b, object o, object n)
        => ((WorkflowMinimapOverlay)b).MarkDirty();

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

    private WorkflowBounds _lastGlobalBounds;
    private readonly List<(double X, double Y, double W, double H)> _lastNodeRects = [];
    private WorkflowBounds _lastViewport;
    private bool _pendingRefresh = true;
    private bool _isDragging;
    // Windows 上平移手势与原生指针探针会同时活着。只让其中一个驱动 NavigateToCanvasLocal；
    // 两个都写时，绝对指针坐标与手势累计增量交替成为目标，X/Y 会以各自的节奏抖。
    // 非 Windows 目标没有手势/原生探针路径，两个标记保持 false；Windows 在 #if 分支内改写。
    private bool _panActive = false;
    private bool _nativePointerActive = false;
    private ContentView? _parentView;
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
    private readonly HashSet<INodeComponent> _subscribedNodes = [];
    private readonly HashSet<ILinkComponent> _subscribedLinks = [];
    private ITreeComponent? _subscribedTree;
    private ScrollView? _scrollView;

    // 绘制的中间量（MAUI ICanvas 用 float）
    private float _mmW, _mmH, _ox, _oy, _sc;
    private WorkflowBounds _drawGb;
    // 最近一次布局算出的可用区与放置矩形。矩形是浮层局部坐标；输入层的子坐标从矩形左上角起算。
    private double _placementRight, _placementTop, _placementBottom, _placementMargin;
    private RectF _minimapRect;

    public WorkflowMinimapOverlay()
    {
        // 全表面浮层：本元素自身不吃输入（CascadeInputTransparent=false 让子元素仍可命中），
        // 绘制层始终 InputTransparent；只有小地图矩形上的输入层接收指针。
        InputTransparent = true;
        CascadeInputTransparent = false;

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

    // 可用区来自 surface：右侧让开竖滚动条，顶部让开水平刻度，底部让开横滚动条。
    // MAUI 的滚动条是覆盖式的（不占宽高），surface 因此把整幅宽高作为可用区传下来。
    private MinimapDrawContext CreatePlacement(double availableWidth, double availableHeight)
    {
        var width = SafeDim(availableWidth, 0);
        var height = SafeDim(availableHeight, 0);
        var top = double.IsNaN(AvailableTop) ? 0 : Math.Max(0, AvailableTop);
        var right = double.IsNaN(AvailableRight) ? 0 : AvailableRight;
        var bottom = double.IsNaN(AvailableBottom) ? 0 : AvailableBottom;

        return new MinimapDrawContext
        {
            MinimapWidth = double.IsNaN(MinimapWidth) ? 0 : Math.Max(0, MinimapWidth),
            MinimapHeight = double.IsNaN(MinimapHeight) ? 0 : Math.Max(0, MinimapHeight),
            MinimapMargin = double.IsNaN(MinimapMargin) ? 0 : Math.Max(0, MinimapMargin),
            AvailableRight = right > 0 ? right : width,
            AvailableTop = top,
            AvailableBottom = bottom > top ? bottom : height,
        };
    }

    private void OnLoaded(object? s, EventArgs e)
    {
        // 沿父链上溯找到根 ContentView（WorkflowView）；用于查 ScrollView 与拖出边界后的指针跟踪。
        Element? el = this;
        while (el is not null)
        {
            if (el is ContentView cv)
            {
                if (_parentView is null) _parentView = cv;
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

            if (_pendingRefresh) RefreshMinimapData();
            var (sw, sh) = CurrentSurfaceSize();
            if (sw <= 0 || sh <= 0) return;
            ComputeDrawing(sw, sh);

            var pt = e.GetCurrentPoint(mm).Position;
            // 输入层本就只覆盖小地图矩形；这里再挡一道，陈旧布局下也不会在矩形外起拖。
            if (!IsInsideMinimapRect((float)pt.X, (float)pt.Y)) return;

            // 新的一次按下：清掉上一次可能没收到 Completed 的手势标记；本次手势若随后 Started 会重新置位。
            _panActive = false;
            _nativePointerActive = true;
            // 尽力显式捕获：捕获期间只有此元素发指针事件，下面的 PointerMoved 移出边界也继续流动，直到释放（只能在 PointerPressed 期间取得）。
            mm.CapturePointer(e.Pointer);
            // MAUI 的 StartInteraction 先经类 handler 再到本实例 handler，所以交互跑时 _isDragging 已为真，
            // 这里不能再用探针的坐标覆盖它已经记下的起点（两套坐标来源一旦混用，X/Y 会各走各的）。
            if (_isDragging) return;
            _dragStartX = (float)pt.X;
            _dragStartY = (float)pt.Y;
            _lastScrollX = float.MinValue;
            _lastScrollY = float.MinValue;
            NavigateToCanvasLocal(_dragStartX, _dragStartY);
            _isDragging = true;
        };
        _nativeMovedHandler = (s, e) =>
        {
            if (!_isDragging) return;
            // 原生探针是 Windows 上的首选：绝对坐标与缩略图同一套 DIP。PanGesture 的 TotalX/Y
            // 在 WinUI 上会带屏幕/元素偏移，拿它加到局部起点上会把目标一次推出上千像素。
            if (_inputView.Handler?.PlatformView is not UIElement mm) return;
            var pt = e.GetCurrentPoint(mm).Position;
            NavigateToCanvasLocal((float)pt.X, (float)pt.Y);
        };
        _nativeReleasedHandler = (s, e) =>
        {
            _nativePointerActive = false;
            _isDragging = false;
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
    /// ends at the real release (Completed/Canceled) — matching the other frameworks.
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
                if (_isDragging && _panActive && !_nativePointerActive)
                {
                    var x = _dragStartX + (float)e.TotalX;
                    var y = _dragStartY + (float)e.TotalY;
                    NavigateToCanvasLocal(x, y);
                }
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _panActive = false;
                _isDragging = false;
                break;
        }
    }
#endif

    // ── 树管理 ──────────────────────────────────────────────────────────────

    private void OnTreeChanged(ITreeComponent? newTree)
    {
        UnsubscribeFromTree();
        _subscribedTree = newTree;
        if (newTree is null) { MarkDirty(); return; }

        if (newTree.Nodes is INotifyCollectionChanged nc)
        {
            nc.CollectionChanged += OnNodesChanged;
            foreach (var n in newTree.Nodes) SubscribeNode(n);
        }
        if (newTree.Links is INotifyCollectionChanged lc)
        {
            lc.CollectionChanged += OnLinksChanged;
            foreach (var l in newTree.Links) SubscribeLink(l);
        }
        MarkDirty();
    }

    // 没有节点就没有可画的小地图：输入层此时必须让开，否则右上角会凭空吞掉画布手势。
    private bool HasMinimapContent => WorkflowTree?.Nodes is { Count: > 0 };

    private void UnsubscribeFromTree()
    {
        if (_subscribedTree is null) return;
        if (_subscribedTree.Nodes is INotifyCollectionChanged nc) nc.CollectionChanged -= OnNodesChanged;
        foreach (var n in _subscribedNodes) if (n is INotifyPropertyChanged npc) npc.PropertyChanged -= OnNodePropChanged;
        _subscribedNodes.Clear();
        if (_subscribedTree.Links is INotifyCollectionChanged lc) lc.CollectionChanged -= OnLinksChanged;
        foreach (var l in _subscribedLinks)
        {
            if (l.Sender is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotPropChanged;
            if (l.Receiver is INotifyPropertyChanged rp) rp.PropertyChanged -= OnSlotPropChanged;
        }
        _subscribedLinks.Clear();
        _subscribedTree = null;
    }

    private void SubscribeNode(INodeComponent n)
    {
        if (_subscribedNodes.Add(n) && n is INotifyPropertyChanged npc) npc.PropertyChanged += OnNodePropChanged;
    }

    private void SubscribeLink(ILinkComponent l)
    {
        if (!_subscribedLinks.Add(l)) return;
        if (l.Sender is INotifyPropertyChanged sp) sp.PropertyChanged += OnSlotPropChanged;
        if (l.Receiver is INotifyPropertyChanged rp) rp.PropertyChanged += OnSlotPropChanged;
    }

    private void OnNodesChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (var i in e.NewItems) if (i is INodeComponent n) SubscribeNode(n);
        if (e.OldItems is not null) foreach (var i in e.OldItems) if (i is INodeComponent n && _subscribedNodes.Remove(n) && n is INotifyPropertyChanged npc) npc.PropertyChanged -= OnNodePropChanged;
        MarkDirty();
    }

    private void OnLinksChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (var i in e.NewItems) if (i is ILinkComponent l) SubscribeLink(l);
        if (e.OldItems is not null) foreach (var i in e.OldItems) if (i is ILinkComponent l && _subscribedLinks.Remove(l))
            {
                if (l.Sender is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotPropChanged;
                if (l.Receiver is INotifyPropertyChanged rp) rp.PropertyChanged -= OnSlotPropChanged;
            }
        MarkDirty();
    }

    private void OnNodePropChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(INodeComponent.Anchor) or nameof(INodeComponent.Size)) MarkDirty();
    }

    private void OnSlotPropChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISlotComponent.Anchor)) MarkDirty();
    }

    private bool _invalidatePending;

    private void MarkDirty()
    {
        _pendingRefresh = true;
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

    // ── 数据刷新 ─────────────────────────────────────────────────────────────

    private void RefreshMinimapData()
    {
        _pendingRefresh = false;
        var tree = WorkflowTree;
        if (tree is null) { ClearCache(); return; }

        _lastNodeRects.Clear();

        if (tree.Nodes is not null)
            foreach (var node in tree.Nodes)
            {
                var (nx, ny, nw, nh) = (
                    double.IsNaN(node.Anchor.Horizontal) ? 0 : node.Anchor.Horizontal,
                    double.IsNaN(node.Anchor.Vertical) ? 0 : node.Anchor.Vertical,
                    Math.Max(1, node.Size.Width),
                    Math.Max(1, node.Size.Height));
                _lastNodeRects.Add((nx, ny, nw, nh));
            }
        _lastGlobalBounds = WorkflowBounds.FromNodes(_lastNodeRects);

        var rawVpX = WorkflowSurfaceMath.ToCanvasLocal(ScrollOffsetX, ContentOffsetX);
        var rawVpY = WorkflowSurfaceMath.ToCanvasLocal(ScrollOffsetY, ContentOffsetY);
        var vpX = double.IsNaN(rawVpX) ? 0 : rawVpX;
        var vpY = double.IsNaN(rawVpY) ? 0 : rawVpY;
        var vpW = double.IsNaN(ViewportWidth) ? 1 : Math.Max(1, ViewportWidth);
        var vpH = double.IsNaN(ViewportHeight) ? 1 : Math.Max(1, ViewportHeight);
        _lastViewport = WorkflowBounds.FromNode(vpX, vpY, vpW, vpH);
    }

    private void ClearCache()
    {
        _lastNodeRects.Clear();
        _lastGlobalBounds = default;
        _lastViewport = default;
    }

    // ── 布局与绘制中间量 ────────────────────────────────────────────────────

    private (double W, double H) CurrentSurfaceSize()
    {
        var w = SafeDim(Width, SafeDim(_drawView.Width, 0));
        var h = SafeDim(Height, SafeDim(_drawView.Height, 0));
        return (w, h);
    }

    private void UpdateInputPlacement()
        => UpdateInputPlacement(SafeDim(Width, 0), SafeDim(Height, 0));

    // 把输入层贴到小地图矩形上：矩形外的一切命中因此落回画布。
    private void UpdateInputPlacement(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            _inputView.IsVisible = false;
            return;
        }

        var placement = CreatePlacement(width, height);
        var bounds = placement.GetMinimapRect(MinimapMinSize);
        _minimapRect = new RectF((float)bounds.Left, (float)bounds.Top, (float)bounds.Width, (float)bounds.Height);

        _inputView.Margin = new Microsoft.Maui.Thickness(bounds.Left, bounds.Top, 0, 0);
        if (Math.Abs(_inputView.WidthRequest - bounds.Width) > 0.5) _inputView.WidthRequest = bounds.Width;
        if (Math.Abs(_inputView.HeightRequest - bounds.Height) > 0.5) _inputView.HeightRequest = bounds.Height;
        _inputView.IsVisible = IsMinimapVisible && HasMinimapContent && bounds.Width > 0 && bounds.Height > 0;
    }

    private bool IsInsideMinimapRect(float x, float y)
        => _minimapRect.Width > 0 && _minimapRect.Height > 0
            && x >= -1f && y >= -1f
            && x <= _minimapRect.Width + 1f && y <= _minimapRect.Height + 1f;

    private void ComputeDrawing(double availWidth, double availHeight)
    {
        var gb = _lastGlobalBounds;
        _drawGb = gb;

        var placement = CreatePlacement(availWidth, availHeight);
        var bounds = placement.GetMinimapRect(MinimapMinSize);
        _placementRight = placement.AvailableRight;
        _placementTop = placement.AvailableTop;
        _placementBottom = placement.AvailableBottom;
        _placementMargin = placement.MinimapMargin;

        // 布局过渡期尺寸可能为 NaN；退回最小尺寸，绘制与命中都不至于拿到 NaN。
        if (double.IsNaN(bounds.Width) || double.IsNaN(bounds.Height)
            || double.IsInfinity(bounds.Width) || double.IsInfinity(bounds.Height)
            || bounds.Width <= 0 || bounds.Height <= 0)
        {
            var fallback = (float)Math.Max(1, MinimapMinSize);
            _minimapRect = new RectF(0f, 0f, fallback, fallback);
            _mmW = fallback;
            _mmH = fallback;
            _ox = 0f;
            _oy = 0f;
            _sc = 1f;
            return;
        }

        _minimapRect = new RectF((float)bounds.Left, (float)bounds.Top, (float)bounds.Width, (float)bounds.Height);
        _mmW = (float)Math.Max(1, bounds.Width);
        _mmH = (float)Math.Max(1, bounds.Height);

        var pad = (float)Math.Max(0, ContentPadding);
        var drawW = _mmW - pad * 2;
        var drawH = _mmH - pad * 2;

        // MinimapFit：scale = min(drawW/max(1,cw), drawH/max(1,ch))，origin = pad + (draw − content·scale)/2；
        // 保留 MAUI 对拟合比例的 NaN/Infinity 防护（节点 Size 为 NaN 时 gb 边界也会是 NaN）。
        var (fitOx, fitOy, fitSc) = WorkflowSurfaceMath.MinimapFit(
            (float)gb.Width, (float)gb.Height, drawW, drawH, pad);
        _sc = float.IsNaN((float)fitSc) || float.IsInfinity((float)fitSc) ? 1f : (float)fitSc;
        _ox = (float)fitOx;
        _oy = (float)fitOy;

        // _ox/_oy 的最终 NaN 防护 —— 它们为 NaN 时全部命中测试都会失效。
        if (float.IsNaN(_ox)) _ox = 0f;
        if (float.IsNaN(_oy)) _oy = 0f;
    }

    // ── 触摸输入 ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the viewport rectangle's render position in minimap coordinates,
    /// clamped so it always stays within the minimap's visible area.
    /// This is the position used for drawing AND hit testing.
    /// All return values are guaranteed non-NaN.
    /// </summary>
    private (float X, float Y, float W, float H) GetClampedViewportRect()
    {
        var vp = _lastViewport;
        var gb = _lastGlobalBounds;
        if (vp.IsEmpty || gb.IsEmpty || _sc <= 0 || float.IsNaN(_sc))
            return (0, 0, 0, 0);

        var w = Math.Max(2f, SafeFloatMul(vp.Width, _sc));
        var h = Math.Max(2f, SafeFloatMul(vp.Height, _sc));
        if (float.IsNaN(w) || float.IsNaN(h))
            return (0, 0, 0, 0);

        // MinimapViewportRect 用与内容相同的拟合变换把世界视口映射到缩略图，并把指示块夹在缩略图边界内（指示块最小 2px，与旧内联算法一致）。
        var (x, y, rw, rh) = WorkflowSurfaceMath.MinimapViewportRect(
            _ox, _oy, _sc,
            vp.Left, vp.Top, vp.Width, vp.Height,
            gb.Left, gb.Top,
            _mmW, _mmH, minRectSize: 2.0);

        // 保留 MAUI 对该 helper 原始输出的最终 NaN 防护。
        return (float.IsNaN((float)x) ? 0f : (float)x,
                float.IsNaN((float)y) ? 0f : (float)y,
                float.IsNaN((float)rw) ? 0f : (float)rw,
                float.IsNaN((float)rh) ? 0f : (float)rh);
    }

    /// <summary>Safe float multiplication guarding against NaN.</summary>
    private static float SafeFloatMul(double a, double b)
        => double.IsNaN(a) || double.IsNaN(b) ? 0f : (float)(a * b);

    /// <summary>Returns a safe dimension value, guarding against NaN and <=0.</summary>
    private static double SafeDim(double value, double fallback)
        => double.IsNaN(value) || value <= 0 ? fallback : value;

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
            if (_isDragging) return;
            if (_pendingRefresh) RefreshMinimapData();
            var (sw, sh) = CurrentSurfaceSize();
            if (sw <= 0 || sh <= 0) return;
            ComputeDrawing(sw, sh);

            if (e.Touches is null || e.Touches.Length == 0) return;
            var pt = e.Touches[0];
            // 输入层只覆盖小地图矩形；这里再挡一道，陈旧布局下也不会在矩形外起拖。
            if (!IsInsideMinimapRect(pt.X, pt.Y)) return;

            // 新手势从当前指针重新起算；沿用上一轮的节流目标会把第一次滚动吞掉。
            _lastScrollX = float.MinValue;
            _lastScrollY = float.MinValue;

            // 与 Jalium 家一致：点击点一律成为视口中心 —— 指示块上没有抓取锚点，按在哪里都重新居中。
#if WINDOWS
            _dragStartX = pt.X;
            _dragStartY = pt.Y;
#endif
            NavigateToCanvasLocal(pt.X, pt.Y);
            _isDragging = true;

            SubscribeDragCapture();
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
            if (!_isDragging) return;
            // Windows 上手势/原生探针已经在驱动；MAUI 触摸路径不要再补一份。
            if (_panActive || _nativePointerActive) return;
            if (e.Touches is null || e.Touches.Length == 0) return;
            var pt = e.Touches[0];
            NavigateToCanvasLocal(pt.X, pt.Y);
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
            _isDragging = false;
            UnsubscribeDragCapture();
#endif
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"[Minimap] EndInteraction error: {ex.Message}");
        }
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
        if (!_isDragging) return;
        // 父视图的指针事件是浮层局部坐标；输入层里的一切都从矩形左上角起算，这里换回去。
        var pos = e.GetPosition(this);
        if (pos is null) return;
        NavigateToCanvasLocal(
            (float)(pos.Value.X - _minimapRect.Left),
            (float)(pos.Value.Y - _minimapRect.Top));
    }

    private void OnParentPointerReleased(object? sender, PlatformInput.PointerEventArgs e)
    {
        _isDragging = false;
        UnsubscribeDragCapture();
    }

    // ── 输入/拖拽 ──────────────────────────────────────────────────────────

    private float _lastScrollX = float.MinValue;
    private float _lastScrollY = float.MinValue;

    /// <summary>
    /// Navigate the main workflow viewport to a point in minimap-local coordinates (the minimap
    /// rectangle's top-left is the origin).
    /// When the target scroll position exceeds the current canvas bounds, the
    /// canvas model expands (PositiveOffset/NegativeOffset) and ApplyLayout
    /// translates the content accordingly.  The IsRefreshing guard in
    /// WorkflowSurfaceBehavior prevents the cascade that caused the original
    /// crash — expansion is safe now.
    /// </summary>
    private void NavigateToCanvasLocal(float adjX, float adjY)
    {
        if (_drawGb.IsEmpty || _sc <= 0) return;

        // MinimapToCanvasLocal：world = (mm − origin)/scale + contentLeft；MinimapToScroll 再把世界目标换算成能把它居中到视口的滚动偏移。
        var (wcx, wcy) = WorkflowSurfaceMath.MinimapToCanvasLocal(
            adjX, adjY, _ox, _oy, _sc, _drawGb.Left, _drawGb.Top);
        var (scrollX, scrollY) = WorkflowSurfaceMath.MinimapToScroll(
            wcx, wcy, ViewportWidth, ViewportHeight, ContentOffsetX, ContentOffsetY);
        if (_scrollView is null || WorkflowTree?.Layout is not { } layout) return;

        // 最大滚动范围取自布局模型（ActualSize = OriginSize + offsets），下面的 ClampScrollOffset 一写越界量它就同步长大；
        // _scrollView.ContentSize 滞后于异步 MAUI 布局，拿它夹取会把目标钉在当前边缘、2px 节流又跳过这次滚动，画布再也长不大 —— 在边界上自锁。
        // 画布平移（WorkflowSurfaceBehavior）正是为此用模型。
        var svW = _scrollView.Width;
        var svH = _scrollView.Height;

        var maxH = ComputeMaxScroll(layout.ActualSize.Width, svW);
        var maxV = ComputeMaxScroll(layout.ActualSize.Height, svH);

        // 拖到边缘时扩展画布模型：ClampScrollOffset 仅在越界量超过 0.5f 死区（亚像素抖动）时把它写进 NegativeOffset（原点前）
        // 或 PositiveOffset（边缘外），并返回夹取后的滚动偏移。画布只经 ApplyLayout（由 WorkflowSurfaceBehavior.Refresh 驱动）长大，
        // 所以扩展时就地应用布局（同 ApplyPanAsync），再从模型重算最大值（模型已反映增长）。
        bool layoutChanged = (scrollX < 0 && -scrollX > 0.5f)
            || (scrollX > maxH && scrollX - maxH > 0.5f)
            || (scrollY < 0 && -scrollY > 0.5f)
            || (scrollY > maxV && scrollY - maxV > 0.5f);

        scrollX = WorkflowSurfaceMath.ClampScrollOffset(scrollX, maxH, layout, horizontal: true, threshold: 0.5f);
        scrollY = WorkflowSurfaceMath.ClampScrollOffset(scrollY, maxV, layout, horizontal: false, threshold: 0.5f);

        // 扩展后重算最大值（模型刚同步长大）。
        if (layoutChanged)
        {
            if (_parentView is not null)
            {
                WorkflowSurfaceBehavior.Refresh(_parentView);
            }
            maxH = ComputeMaxScroll(layout.ActualSize.Width, _scrollView.Width);
            maxV = ComputeMaxScroll(layout.ActualSize.Height, _scrollView.Height);
        }

        var clampedX = SafeClamp(scrollX, maxH);
        var clampedY = SafeClamp(scrollY, maxV);

        // 节流：目标没有实质变化就跳过 ScrollToAsync。
        if (Math.Abs(clampedX - _lastScrollX) < 2f &&
            Math.Abs(clampedY - _lastScrollY) < 2f)
        {
            return;
        }

        _lastScrollX = clampedX;
        _lastScrollY = clampedY;

        SafeScrollTo(_scrollView, clampedX, clampedY);
    }

    /// <summary>
    /// Compute max scroll offset from content and viewport dimensions,
    /// fully guarded against NaN/zero/infinity from async MAUI layout.
    /// </summary>
    private static float ComputeMaxScroll(double content, double viewport)
    {
        if (double.IsNaN(content) || content <= 0 ||
            double.IsNaN(viewport) || viewport <= 0)
            return 0f;
        return (float)Math.Max(0, content - viewport);
    }

    /// <summary>
    /// Clamp scroll offset within valid range, guarding against NaN/Infinity
    /// that may propagate from intermediate layout state.
    /// </summary>
    private static float SafeClamp(double value, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0f;
        if (double.IsNaN(max) || double.IsInfinity(max)) return 0f;
        return (float)WorkflowSurfaceMath.ClampValue(value, 0, max);
    }

    /// <summary>
    /// Call ScrollToAsync with exception protection.  NaN/Infinity values
    /// cause ArgumentException crashes in the native scroll viewer.
    /// </summary>
    private static void SafeScrollTo(ScrollView? sv, float x, float y)
    {
        if (sv is null) return;
        if (float.IsNaN(x) || float.IsInfinity(x) ||
            float.IsNaN(y) || float.IsInfinity(y))
            return;
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
            if (_pendingRefresh) RefreshMinimapData();

            // dirtyRect.Width/Height 可能为 NaN（已知 MAUI WinUI 问题）；必须用 IsNaN 判断 —— NaN > 0 为假，NaN <= 0 也为假。
            var dw = dirtyRect.Width;
            var dh = dirtyRect.Height;
            var w = (!float.IsNaN(dw) && dw > 0) ? dw : (float)SafeDim(Width, 0);
            var h = (!float.IsNaN(dh) && dh > 0) ? dh : (float)SafeDim(Height, 0);
            if (w <= 0 || h <= 0) return;
            if (WorkflowTree?.Nodes is null || WorkflowTree.Nodes.Count == 0) return;

            ComputeDrawing(w, h);
            var context = CreateDrawContext();
            if (!context.HasContent || context.Scale <= 0) return;

            DrawMinimap(canvas, context);
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
        // 默认绘制把整幅缩略图平移到矩形左上角：context 里的映射仍是缩略图局部坐标（原点 0,0）。
        var bounds = context.GetMinimapRect(MinimapMinSize);
        var offsetX = (float)bounds.Left;
        var offsetY = (float)bounds.Top;
        var cr = Math.Max(0, (float)context.MinimapCornerRadius);

        if (MinimapBackgroundColor is not null)
        {
            canvas.FillColor = MinimapBackgroundColor;
            canvas.FillRoundedRectangle(offsetX, offsetY, (float)context.MinimapWidth, (float)context.MinimapHeight, cr);
        }
        if (MinimapBorderColor is not null)
        {
            canvas.StrokeColor = MinimapBorderColor;
            canvas.StrokeSize = (float)MinimapBorderThickness;
            canvas.DrawRoundedRectangle(offsetX, offsetY, (float)context.MinimapWidth, (float)context.MinimapHeight, cr);
        }

        canvas.SaveState();
        canvas.ClipRectangle(offsetX, offsetY, (float)context.MinimapWidth, (float)context.MinimapHeight);
        try
        {
            if (NodeFillColor is not null)
            {
                canvas.FillColor = NodeFillColor;
                var ncr = Math.Max(0, (float)context.NodeCornerRadius);
                foreach (var node in context.MappedNodes)
                {
                    canvas.FillRoundedRectangle(
                        offsetX + (float)node.Left, offsetY + (float)node.Top,
                        (float)node.Width, (float)node.Height, ncr);
                }
            }

            if (context.HasViewport && !context.MappedViewport.IsEmpty)
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

    /// <summary>Builds the geometry for one draw pass from the computed transform and cached node rects.</summary>
    private MinimapDrawContext CreateDrawContext()
    {
        var gb = _drawGb;
        var nodes = new List<WorkflowBounds>(_lastNodeRects.Count);
        var mapped = new List<WorkflowBounds>(_lastNodeRects.Count);
        foreach (var (nx, ny, nw, nh) in _lastNodeRects)
        {
            nodes.Add(WorkflowBounds.FromNode(nx, ny, nw, nh));
            // MinimapLocal：local = origin + (world − contentOrigin)·scale。
            var (lx, ly) = WorkflowSurfaceMath.MinimapLocal(nx, ny, gb.Left, gb.Top, _ox, _oy, _sc);
            mapped.Add(WorkflowBounds.FromNode(lx, ly,
                Math.Max(2d, nw * _sc),
                Math.Max(2d, nh * _sc)));
        }

        var (vpx, vpy, vpw, vph) = GetClampedViewportRect();
        var mappedViewport = vpw > 0 && vph > 0 && !float.IsNaN(vpx) && !float.IsNaN(vpy)
            ? WorkflowBounds.FromNode(vpx, vpy, vpw, vph)
            : default;

        return new MinimapDrawContext
        {
            Tree = WorkflowTree,
            ContentBounds = gb,
            ViewportBounds = _lastViewport,
            Nodes = nodes,
            MappedNodes = mapped,
            MappedViewport = mappedViewport,
            MinimapWidth = _mmW,
            MinimapHeight = _mmH,
            Padding = Math.Max(0, ContentPadding),
            OriginX = _ox,
            OriginY = _oy,
            Scale = _sc,
            NodeCornerRadius = Math.Max(0, NodeCornerRadius),
            MinimapCornerRadius = Math.Max(0, MinimapCornerRadius),
            AvailableRight = _placementRight,
            AvailableTop = _placementTop,
            AvailableBottom = _placementBottom,
            MinimapMargin = _placementMargin,
        };
    }
}
