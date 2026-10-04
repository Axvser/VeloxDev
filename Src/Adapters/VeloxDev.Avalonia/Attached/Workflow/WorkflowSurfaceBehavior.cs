using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Reflection;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSurfaceBehavior : AvaloniaObject
{
    private sealed class SurfaceState
    {
        public bool IsPanning { get; set; }
        public Point PanStart { get; set; }
        public Vector PanStartOffset { get; set; }
        public ScrollViewer? ScrollViewer { get; set; }
        public Canvas? Canvas { get; set; }
        public Control? GridDecorator { get; set; }
        public Control? PointerPressSource { get; set; }
        public Control? MinimapOverlay { get; set; }
        public EventHandler<PointerWheelEventArgs>? ZoomHandler { get; set; }

        // 上一棵被挂上来的树（引用比较）。恢复只因「换了树」触发一次，之后的 Refresh 不再把用户滚回去。
        public IWorkflowTreeViewModel? LastRestoreTree { get; set; }
        public bool HasPendingRestore { get; set; }
        public bool RestoreQueued { get; set; }
        public double PendingRestoreX { get; set; }
        public double PendingRestoreY { get; set; }

        // 悬停时取焦点的那个连线控件（引用比较，避免每次指针移动都重复 Focus）。
        public Control? HoverFocus { get; set; }

        // 宿主本身：悬停焦点在连线可视对象不可聚焦时的落点（模板/Trimmed 的连线视图就是这种）。
        public UserControl? Host { get; set; }

        // 连线右键菜单：菜单由模板声明（条目归用户，见 LinkMenuProperty），订阅、定位、弹出、开合上报都在这里。
        public ContextMenu? LinkMenu { get; set; }
        public IWorkflowLinkViewModel? MenuLink { get; set; }
        public Anchor MenuPosition { get; set; } = new();
        public LinkInteraction? MenuHub { get; set; }
        public EventHandler<ContextMenuRequestedEventArgs>? MenuRequested { get; set; }
        public EventHandler<ContextMenuDismissRequestedEventArgs>? MenuDismissed { get; set; }
        public EventHandler<RoutedEventArgs>? MenuOpened { get; set; }
        public EventHandler<RoutedEventArgs>? MenuClosed { get; set; }
    }

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, bool>("IsEnabled");

    public static readonly AttachedProperty<string?> ScrollViewerNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("ScrollViewerName");

    public static readonly AttachedProperty<string?> CanvasNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("CanvasName");

    public static readonly AttachedProperty<string?> GridDecoratorNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("GridDecoratorName");

    public static readonly AttachedProperty<string?> PointerPressSourceNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("PointerPressSourceName");

    public static readonly AttachedProperty<string?> MinimapOverlayNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("MinimapOverlayName");

    public static readonly AttachedProperty<bool> ZoomEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, bool>("ZoomEnabled");

    private static readonly AttachedProperty<SurfaceState?> StateProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, SurfaceState?>("State");

    static WorkflowSurfaceBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<UserControl>(OnIsEnabledChanged);
        ZoomEnabledProperty.Changed.AddClassHandler<UserControl>(OnZoomEnabledChanged);
        // 菜单键可能在 IsEnabled 之后才写到元素上（XAML 属性顺序），所以它自己也要触发一次重查。
        LinkMenuKeyProperty.Changed.AddClassHandler<UserControl>((control, _) => Refresh(control));
    }

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetZoomEnabled(AvaloniaObject element) => element.GetValue(ZoomEnabledProperty);

    public static void SetZoomEnabled(AvaloniaObject element, bool value) => element.SetValue(ZoomEnabledProperty, value);

    /// <summary>
    /// Resource key of the context menu a right press on a link opens. The entries are the user's — declare a
    /// <see cref="ContextMenu"/> resource under that key, put its items in it, and name the key here; the surface
    /// resolves which link, positions the menu, opens it, and reports open/close to the interaction hub.
    /// <para>
    /// A key rather than the menu itself: this property sits on the surface's own root element, and a
    /// <c>{StaticResource}</c> there would be resolved before the very resource dictionary that defines it.
    /// </para>
    /// </summary>
    public static readonly AttachedProperty<string?> LinkMenuKeyProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("LinkMenuKey");

    public static string? GetScrollViewerName(AvaloniaObject element) => element.GetValue(ScrollViewerNameProperty);

    public static void SetScrollViewerName(AvaloniaObject element, string? value) => element.SetValue(ScrollViewerNameProperty, value);

    public static string? GetCanvasName(AvaloniaObject element) => element.GetValue(CanvasNameProperty);

    public static void SetCanvasName(AvaloniaObject element, string? value) => element.SetValue(CanvasNameProperty, value);

    public static string? GetGridDecoratorName(AvaloniaObject element) => element.GetValue(GridDecoratorNameProperty);

    public static void SetGridDecoratorName(AvaloniaObject element, string? value) => element.SetValue(GridDecoratorNameProperty, value);

    public static string? GetPointerPressSourceName(AvaloniaObject element) => element.GetValue(PointerPressSourceNameProperty);

    public static void SetPointerPressSourceName(AvaloniaObject element, string? value) => element.SetValue(PointerPressSourceNameProperty, value);

    public static string? GetMinimapOverlayName(AvaloniaObject element) => element.GetValue(MinimapOverlayNameProperty);

    public static void SetMinimapOverlayName(AvaloniaObject element, string? value) => element.SetValue(MinimapOverlayNameProperty, value);

    public static string? GetLinkMenuKey(AvaloniaObject element) => element.GetValue(LinkMenuKeyProperty);

    public static void SetLinkMenuKey(AvaloniaObject element, string? value) => element.SetValue(LinkMenuKeyProperty, value);

    public static void Refresh(UserControl host)
    {
        if (!GetIsEnabled(host))
            return;

        var state = host.GetValue(StateProperty) ?? new SurfaceState();
        host.SetValue(StateProperty, state);
        ResolveNamedControls(host, state);
        WireLinkMenu(host, state);
        CaptureViewportRestore(host, state);
        ApplyLayout(host, state);
        UpdateVisibleRegion(host, state);
        QueueViewportRestore(host, state);
    }

    // 连线右键菜单：**条目由模板声明**（挂在 LinkMenuProperty 上），**接线在这里** —— 订中枢、定位、弹出、
    // 把开合报回去，模板因此没有一行交互代码。菜单指着的那条线离树时中枢发 DismissRequested，这里收自己那份。
    private static void WireLinkMenu(UserControl host, SurfaceState state)
    {
        // 资源在 attach 之后才一定就绪（第一次 Refresh 可能早于 Resources 解析完），所以每次 Refresh 都重查一次。
        var key = GetLinkMenuKey(host);
        ContextMenu? menu = null;
        if (key is { Length: > 0 } && host.TryFindResource(key, out var found))
        {
            menu = found as ContextMenu;
        }

        if (!ReferenceEquals(menu, state.LinkMenu))
        {
            if (state.LinkMenu is not null)
            {
                if (state.MenuOpened is not null) state.LinkMenu.Opened -= state.MenuOpened;
                if (state.MenuClosed is not null) state.LinkMenu.Closed -= state.MenuClosed;
            }

            state.LinkMenu = menu;
            state.MenuOpened = null;
            state.MenuClosed = null;
            if (menu is not null)
            {
                state.MenuOpened = (_, _) => state.MenuHub?.Publish(
                    new ContextMenuEvent(ContextMenuPhase.Opened, state.MenuPosition, state.MenuLink));
                state.MenuClosed = (_, _) => state.MenuHub?.Publish(
                    new ContextMenuEvent(ContextMenuPhase.Closed, state.MenuPosition, state.MenuLink));
                menu.Opened += state.MenuOpened;
                menu.Closed += state.MenuClosed;
            }
        }

        var hub = host.DataContext is IWorkflowTreeViewModel tree ? LinkInteraction.For(tree) : null;
        if (ReferenceEquals(hub, state.MenuHub))
        {
            return;
        }

        if (state.MenuHub is not null)
        {
            if (state.MenuRequested is not null) state.MenuHub.ContextMenuRequested -= state.MenuRequested;
            if (state.MenuDismissed is not null) state.MenuHub.ContextMenuDismissRequested -= state.MenuDismissed;
        }

        state.MenuHub = hub;
        state.MenuRequested = null;
        state.MenuDismissed = null;
        if (hub is null)
        {
            return;
        }

        state.MenuRequested = (_, e) => ShowLinkMenu(host, state, e);
        state.MenuDismissed = (_, e) =>
        {
            if (!ReferenceEquals(state.MenuLink, e.Link)) return;
            state.LinkMenu?.Close();
        };
        hub.ContextMenuRequested += state.MenuRequested;
        hub.ContextMenuDismissRequested += state.MenuDismissed;
    }

    private static void UnwireLinkMenu(SurfaceState state)
    {
        if (state.LinkMenu is not null)
        {
            if (state.MenuOpened is not null) state.LinkMenu.Opened -= state.MenuOpened;
            if (state.MenuClosed is not null) state.LinkMenu.Closed -= state.MenuClosed;
            state.LinkMenu = null;
        }

        state.MenuOpened = null;
        state.MenuClosed = null;

        if (state.MenuHub is not null)
        {
            if (state.MenuRequested is not null) state.MenuHub.ContextMenuRequested -= state.MenuRequested;
            if (state.MenuDismissed is not null) state.MenuHub.ContextMenuDismissRequested -= state.MenuDismissed;
            state.MenuHub = null;
        }

        state.MenuRequested = null;
        state.MenuDismissed = null;
        state.MenuLink = null;
    }

    // 右键落在表面上，而弹出要屏幕坐标；只有表面同时知道画布与屏幕两件事，所以菜单由表面弹。
    private static void ShowLinkMenu(UserControl host, SurfaceState state, ContextMenuRequestedEventArgs e)
    {
        // 空白画布没有可操作的对象，不给菜单。
        if (e.Link is null || state.LinkMenu is null || state.Canvas is null) return;
        if (host.DataContext is not IWorkflowTreeViewModel tree) return;

        // 画布坐标 → 宿主局部的一点：先按逆变换回到画布局部，再由画布换到宿主 —— PlacementRect 相对宿主。
        var screen = WorkflowSurfaceMath.ToScreen(e.Position.Horizontal, e.Position.Vertical, tree.Layout);
        var point = state.Canvas.TranslatePoint(new Point(screen.Horizontal, screen.Vertical), host)
                    ?? new Point(screen.Horizontal, screen.Vertical);

        state.MenuLink = e.Link;
        state.MenuPosition = e.Position;

        // 菜单的 DataContext 就是这条连线，条目据此绑定命令。
        state.LinkMenu.DataContext = e.Link;
        state.LinkMenu.Placement = PlacementMode.AnchorAndGravity;
        state.LinkMenu.PlacementAnchor = PopupAnchor.TopLeft;
        state.LinkMenu.PlacementGravity = PopupGravity.BottomRight;
        state.LinkMenu.PlacementRect = new Rect(point.X, point.Y, 0, 0);
        state.LinkMenu.Open(host);
    }

    // 指针位置换算到连线发布曲线的那个坐标系（canvas-local 锚点空间）：指针在 Canvas 的局部坐标里，
    // 减去 ActualOffset 即连线视图自身的几何空间（见 WorkflowSurfaceMath / 各连线视图的 StartLeft 绑定）。
    // 中枢按树取（LinkInteraction.For）：适配器只把平台指针翻成标准事件转发，不再自己持有实例。
    private static void ForwardLinkPointer(UserControl host, SurfaceState state, PointerEventArgs e, PointerPhase phase, PointerButtonKind button = PointerButtonKind.None)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
            return;

        var point = e.GetPosition(state.Canvas);
        var anchor = WorkflowSurfaceMath.ToWorldAnchor(point.X, point.Y, 0, viewModel.Layout);
        var interaction = LinkInteraction.For(viewModel);
        interaction.Publish(new PointerEvent(phase, anchor, button));
        FocusHoveredLink(interaction, state);
    }

    // 悬停把键盘焦点交给能接住它的东西：Delete 才能沿焦点所在子树冒泡到宿主的键路由。
    // 优先交给画出那条线的控件；它不可聚焦时（**模板与 Trimmed 的连线视图默认就是**）退回宿主本身 ——
    // 否则「悬停 + Delete」在生成出来的工程里根本没有路由，而且不报错。
    // 输入框自己处理 Delete 时事件已被标记、冒泡到宿主前被吃掉，所以编辑文本不受影响。
    private static void FocusHoveredLink(LinkInteraction interaction, SurfaceState state)
    {
        var hovered = interaction.HoveredLink?.HitTarget()?.Visual as IInputElement;
        IInputElement? target = hovered is null
            ? null
            : hovered is { Focusable: true } ? hovered : state.Host;

        if (ReferenceEquals(state.HoverFocus, target))
            return;

        state.HoverFocus = target as Control;
        if (target is { Focusable: true })
            target.Focus();
    }

    // 树刚挂上来且不是上一棵：把它存档里的视口位置排进待恢复（世界 → 滚动）。
    // 必须在 UpdateVisibleRegion 之前 —— 那一步会拿控件当前（还没滚过去的）位置覆盖 ViewportOffset。
    private static void CaptureViewportRestore(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel) return;
        if (ReferenceEquals(viewModel, state.LastRestoreTree)) return;

        state.LastRestoreTree = viewModel;

        if (!WorkflowSurfaceMath.HasViewportRestore(viewModel.Layout)) return;

        var scroll = WorkflowSurfaceMath.ViewportRestoreScroll(viewModel.Layout);
        state.PendingRestoreX = scroll.Horizontal;
        state.PendingRestoreY = scroll.Vertical;
        state.HasPendingRestore = true;
    }

    // 布局稳定后再滚：DataContext 变化这一刻控件往往还没排版，Extent 还是 0，直接滚会被夹没。
    // 控件也还没解析出来时就不清标记 —— 下一次 Refresh 会再排一次，这就是重试。
    private static void QueueViewportRestore(UserControl host, SurfaceState state)
    {
        if (!state.HasPendingRestore || state.RestoreQueued) return;

        state.RestoreQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            state.RestoreQueued = false;

            if (!state.HasPendingRestore || !GetIsEnabled(host) || state.ScrollViewer is not { } viewer)
                return;

            state.HasPendingRestore = false;

            var maxH = GetHorizontalScrollMaximum(viewer);
            var maxV = GetVerticalScrollMaximum(viewer);
            viewer.Offset = new Vector(
                WorkflowSurfaceMath.ClampValue(state.PendingRestoreX, 0, maxH),
                WorkflowSurfaceMath.ClampValue(state.PendingRestoreY, 0, maxV));

            // 恢复后的位置立刻写回模型，免得控件与 Layout.ViewportOffset 各说各话。
            UpdateVisibleRegion(host, state);
        }, DispatcherPriority.Loaded);
    }

    private static void OnIsEnabledChanged(UserControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Attach(control);
            return;
        }

        Detach(control);
    }

    private static void Attach(UserControl control)
    {
        Detach(control);

        var state = new SurfaceState { Host = control };
        control.SetValue(StateProperty, state);

        // 宿主必须能拿焦点，悬停焦点才有落点、Delete 才有路由。`UserControl` 的 `Focusable` 默认是
        // **false**，所以这里由适配器自己打开 —— 不能指望模板/宿主去设（生成出来的工程不会）。
        control.Focusable = true;
        control.AttachedToVisualTree += OnAttachedToVisualTree;
        control.DetachedFromVisualTree += OnDetachedFromVisualTree;
        control.DataContextChanged += OnDataContextChanged;
        control.PointerMoved += OnPointerMoved;
        control.PointerExited += OnPointerExited;
        control.PointerReleased += OnPointerReleased;
        control.PointerCaptureLost += OnPointerCaptureLost;
        control.KeyDown += OnKeyDown;
        ResolveNamedControls(control, state);
        Refresh(control);
    }

    private static void Detach(UserControl control)
    {
        control.AttachedToVisualTree -= OnAttachedToVisualTree;
        control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        control.DataContextChanged -= OnDataContextChanged;
        control.PointerMoved -= OnPointerMoved;
        control.PointerExited -= OnPointerExited;
        control.PointerReleased -= OnPointerReleased;
        control.PointerCaptureLost -= OnPointerCaptureLost;
        control.KeyDown -= OnKeyDown;

        if (control.GetValue(StateProperty) is SurfaceState state)
        {
            UnsubscribeResolvedControls(state);
            UnwireLinkMenu(state);
        }

        control.ClearValue(StateProperty);
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control)
            Refresh(control);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is SurfaceState state)
            state.IsPanning = false;
    }

    private static void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is UserControl control)
            Refresh(control);
    }

    private static void ResolveNamedControls(UserControl control, SurfaceState state)
    {
        UnsubscribeResolvedControls(state);

        var scrollViewerName = GetScrollViewerName(control);
        if (!string.IsNullOrWhiteSpace(scrollViewerName))
            state.ScrollViewer = FindNamed<ScrollViewer>(control, scrollViewerName!);

        var canvasName = GetCanvasName(control);
        if (!string.IsNullOrWhiteSpace(canvasName))
            state.Canvas = FindNamed<Canvas>(control, canvasName!);

        var gridDecoratorName = GetGridDecoratorName(control);
        if (!string.IsNullOrWhiteSpace(gridDecoratorName))
            state.GridDecorator = FindNamed<Control>(control, gridDecoratorName!);

        var minimapOverlayName = GetMinimapOverlayName(control);
        if (!string.IsNullOrWhiteSpace(minimapOverlayName))
            state.MinimapOverlay = FindNamed<Control>(control, minimapOverlayName!);

        var pointerPressSourceName = GetPointerPressSourceName(control);
        if (!string.IsNullOrWhiteSpace(pointerPressSourceName))
            state.PointerPressSource = FindNamed<Control>(control, pointerPressSourceName!);

        if (state.PointerPressSource is not null)
            state.PointerPressSource.PointerPressed += OnPointerPressed;

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.ScrollChanged += OnScrollChanged;
            // 移除 ScrollContentPresenter 内置的 ScrollGestureRecognizer：否则在触屏平台（Android/iOS）它会在拖拽中
            // 抢走指针捕获，破坏节点拖拽与插槽连接；本类自带完整的平移逻辑。
            state.ScrollViewer.LayoutUpdated += OnScrollViewerLayoutUpdated;
        }

        if (GetZoomEnabled(control))
        {
            HookZoom(state);
        }
    }

    /// <summary>
    /// Resolves one named control, or <see langword="null"/> when the names cannot be resolved yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Avalonia's <c>FindControl</c> needs a parent name scope and throws <c>Could not find parent name scope</c>
    /// without one. During XAML load there is none: the loader writes the attached properties one at a time, so the
    /// one written last finds every name already set and goes looking for real controls — while the scope is still
    /// missing. Which property that is depends on the order a given axaml writes them in, and the templates and
    /// the demos do not all agree.
    /// </para>
    /// <para>
    /// <see langword="null"/> is the honest answer there ("not yet"), not a swallowed failure:
    /// <see cref="Refresh"/> runs again when the control reaches the visual tree and whenever its data context
    /// changes, and by then the scope exists.
    /// </para>
    /// </remarks>
    private static T? FindNamed<T>(UserControl control, string name) where T : Control
    {
        try
        {
            return control.FindControl<T>(name);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void UnsubscribeResolvedControls(SurfaceState state)
    {
        if (state.PointerPressSource is not null)
            state.PointerPressSource.PointerPressed -= OnPointerPressed;

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.ScrollChanged -= OnScrollChanged;
            state.ScrollViewer.LayoutUpdated -= OnScrollViewerLayoutUpdated;
        }

        UnhookZoom(state);
        state.PointerPressSource = null;
        state.ScrollViewer = null;
        state.Canvas = null;
        state.GridDecorator = null;
        state.MinimapOverlay = null;
    }

    private static void OnZoomEnabledChanged(UserControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (control.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (Equals(e.NewValue, true))
        {
            HookZoom(state);
        }
        else
        {
            UnhookZoom(state);
        }
    }

    // Avalonia 没有 PreviewMouseWheel，所以滚轮在 ScrollViewer 上做隧道：它在控件滚动前触发并标记已处理，
    // 普通滚轮滚动因此不受影响。
    private static void HookZoom(SurfaceState state)
    {
        if (state.ScrollViewer is not null && state.ZoomHandler is null)
        {
            state.ZoomHandler = OnZoomPointerWheelChanged;
            state.ScrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, state.ZoomHandler, RoutingStrategies.Tunnel);
        }
    }

    private static void UnhookZoom(SurfaceState state)
    {
        if (state.ScrollViewer is not null && state.ZoomHandler is not null)
        {
            state.ScrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, state.ZoomHandler);
            state.ZoomHandler = null;
        }
    }

    private static void OnZoomPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not Control source)
        {
            return;
        }

        var host = source.GetVisualAncestors().OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        // 滚轮向上（Delta.Y 为正）放大：Scale 是折叠因子，放大要除以 1/1.1。
        var factor = e.Delta.Y > 0 ? 1 / 1.1 : 1.1;
        var next = Math.Max(0.1, Math.Min(10, viewModel.Layout.Scale.Horizontal * factor));
        var layout = viewModel.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter
            && host.GetValue(StateProperty) is SurfaceState state
            && state.ScrollViewer is { } sv)
        {
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                sv.Offset.X, sv.Offset.Y, sv.Viewport.Width, sv.Viewport.Height, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);
            // 深度放大时负向内容会越过固定的画布平移；必须在 ApplyLayout 采纳新偏移前扩大覆盖，
            // 下面的 PivotCenterScroll 才会读到长大的偏移、枢轴保持居中。只有正向内容时无事发生。
            WorkflowSurfaceMath.EnsureNegativeCover(viewModel);

            // 先重排，让 ScrollViewer 在读取最大值前采纳新范围；否则夹取落在陈旧范围上、枢轴偏离中心，
            // 下一次滚轮又从这个误差重捕 —— 累积漂移表现为缩放抖动。
            ApplyLayout(host, state);
            sv.UpdateLayout();

            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, sv.Viewport.Width, sv.Viewport.Height);
            var maxH = GetHorizontalScrollMaximum(sv);
            var maxV = GetVerticalScrollMaximum(sv);

            // 越界扩展画布（与拖过边界同一机制），让枢轴总能到达；单纯夹取会把枢轴推离中心并逐格漂移。
            // 缩放不动画布几何（ActualOffset == NegativeOffset），只动滚动。
            var newX = WorkflowSurfaceMath.ClampScrollOffset(tx, maxH, layout, horizontal: true);
            var newY = WorkflowSurfaceMath.ClampScrollOffset(ty, maxV, layout, horizontal: false);
            if (Math.Abs(newX - tx) > double.Epsilon || Math.Abs(newY - ty) > double.Epsilon)
            {
                ApplyLayout(host, state);
                sv.UpdateLayout();
                maxH = GetHorizontalScrollMaximum(sv);
                maxV = GetVerticalScrollMaximum(sv);
            }

            sv.Offset = new Vector(
                WorkflowSurfaceMath.ClampValue(tx, 0, maxH),
                WorkflowSurfaceMath.ClampValue(ty, 0, maxV));
        }
        else
        {
            layout.Scale = new Scale(next, next);
            // 世界原点缩放（视口居中示例中未启用）：覆盖长大后重排，让画布平移/范围跟上新的 ActualOffset。
            if (WorkflowSurfaceMath.EnsureNegativeCover(viewModel)
                && host.GetValue(StateProperty) is SurfaceState fallbackState
                && fallbackState.ScrollViewer is { } fallbackViewer)
            {
                ApplyLayout(host, fallbackState);
                fallbackViewer.UpdateLayout();
            }
        }
        e.Handled = true;
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control source)
            return;

        var host = source.GetVisualAncestors().OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state || state.ScrollViewer is null)
            return;

        // 按下先过 Core：按在哪条连线上由它裁。左键落在连线上时下面照样会起一次平移（连线算空白），
        // 两者互不冲突 —— Core 只报「按到了哪条」，平移是这层的另一件事。
        ForwardLinkPointer(host, state, e, PointerPhase.Pressed, ButtonOf(e, state));

        if (!ShouldStartPan(e, state))
            return;

        state.IsPanning = true;
        state.PanStart = e.GetPosition(host);
        state.PanStartOffset = state.ScrollViewer.Offset;
        e.Pointer.Capture(source);
        e.Handled = true;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
            return;

        OnCanvasPanMoved(host, state, e);
        if (state.IsPanning)
            return;

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
            return;

        var point = e.GetPosition(state.Canvas);
        viewModel.SetPointerCommand.Execute(
            WorkflowSurfaceMath.ToWorldAnchor(point.X, point.Y, 0, viewModel.Layout));

        // 连线建立过程中橡皮筋就在指针底下，逐帧判悬停只会把沿途那些实连线点亮（VirtualLink 仅在拖时可见）。
        if (viewModel.VirtualLink is not { IsVisible: true })
            ForwardLinkPointer(host, state, e, PointerPhase.Moved);
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
            return;

        ForwardLinkPointer(host, state, e, PointerPhase.Exited);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
            return;

        if (e.Key != Key.Delete || host.DataContext is not IWorkflowTreeViewModel viewModel)
            return;

        // 键也过 Core：现在按 Delete 删哪条与其它六家是同一个答案（hub 的 AutoDelete 自己执行命令）。
        var interaction = LinkInteraction.For(viewModel);
        if (interaction.HoveredLink is null)
            return;

        interaction.Publish(new KeyEvent(InputKey.Delete));
        e.Handled = true;
    }

    private static PointerButtonKind ButtonOf(PointerPressedEventArgs e, SurfaceState state)
    {
        var properties = e.GetCurrentPoint(state.ScrollViewer!).Properties;
        if (properties.IsRightButtonPressed) return PointerButtonKind.Right;
        if (properties.IsLeftButtonPressed) return PointerButtonKind.Left;
        if (properties.IsMiddleButtonPressed) return PointerButtonKind.Middle;
        return PointerButtonKind.None;
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
            return;

        if (state.IsPanning)
        {
            state.IsPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel)
            return;

        viewModel.VirtualLink.Sender.State &= ~SlotState.PreviewSender;
        viewModel.ResetVirtualLinkCommand.Execute(null);
    }

    private static void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
            return;

        state.IsPanning = false;
    }

    private static void OnScrollViewerLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not ScrollViewer viewer)
            return;

        // GestureRecognizerCollection 只暴露 Add（IReadOnlyCollection）；用反射取背后的 _recognizers 字段
        // 来移除 ScrollGestureRecognizer，否则它在触屏平台会在拖拽中抢走指针捕获。
        var presenter = viewer.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        if (presenter is null)
            return;

        var hasScrollRecognizer = presenter.GestureRecognizers.OfType<ScrollGestureRecognizer>().Any();
        if (!hasScrollRecognizer)
            return;

        var field = presenter.GestureRecognizers.GetType()
            .GetField("_recognizers", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(presenter.GestureRecognizers) is System.Collections.Generic.List<GestureRecognizer> list)
        {
            list.RemoveAll(static r => r is ScrollGestureRecognizer);
            viewer.LayoutUpdated -= OnScrollViewerLayoutUpdated;
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
            return;

        var host = viewer.GetVisualAncestors().OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is not null)
            Refresh(host);
    }

    private static void OnCanvasPanMoved(UserControl host, SurfaceState state, PointerEventArgs e)
    {
        if (!state.IsPanning || state.ScrollViewer is null || host.DataContext is not IWorkflowTreeViewModel viewModel)
            return;

        if (!IsPanStillActive(host, e))
        {
            state.IsPanning = false;
            e.Pointer.Capture(null);
            return;
        }

        var current = e.GetPosition(host);
        var desiredX = state.PanStartOffset.X + (state.PanStart.X - current.X);
        var desiredY = state.PanStartOffset.Y + (state.PanStart.Y - current.Y);
        var maxH = GetHorizontalScrollMaximum(state.ScrollViewer);
        var maxV = GetVerticalScrollMaximum(state.ScrollViewer);

        var newOffsetX = WorkflowSurfaceMath.ClampScrollOffset(
            desiredX, maxH, viewModel.Layout, horizontal: true, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
        var newOffsetY = WorkflowSurfaceMath.ClampScrollOffset(
            desiredY, maxV, viewModel.Layout, horizontal: false, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
        var layoutChanged = newOffsetX != desiredX || newOffsetY != desiredY;

        if (layoutChanged)
        {
            ApplyLayout(host, state);
            maxH = GetHorizontalScrollMaximum(state.ScrollViewer);
            maxV = GetVerticalScrollMaximum(state.ScrollViewer);
            newOffsetX = Math.Min(newOffsetX, maxH);
            newOffsetY = Math.Min(newOffsetY, maxV);
            state.PanStart = current;
            state.PanStartOffset = new Vector(
                WorkflowSurfaceMath.ClampValue(newOffsetX, 0, maxH),
                WorkflowSurfaceMath.ClampValue(newOffsetY, 0, maxV));
        }

        state.ScrollViewer.Offset = new Vector(
            WorkflowSurfaceMath.ClampValue(newOffsetX, 0, maxH),
            WorkflowSurfaceMath.ClampValue(newOffsetY, 0, maxV));

        UpdateVisibleRegion(host, state);
        e.Handled = true;
    }

    private static void ApplyLayout(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
            return;

        state.Canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        var transform = new TransformGroup
        {
            Children = [
                new TranslateTransform(
                    viewModel.Layout.ActualOffset.Horizontal,
                    viewModel.Layout.ActualOffset.Vertical)
            ]
        };

        WorkflowCanvasTransformBehavior.Apply(host, transform);

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
    }

    private static void UpdateVisibleRegion(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.ScrollViewer is null)
            return;

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
        var viewportX = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.Offset.X, viewModel.Layout.ActualOffset.Horizontal);
        var viewportY = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.Offset.Y, viewModel.Layout.ActualOffset.Vertical);
        viewModel.GetHelper().Viewport = new Viewport(
            viewportX, viewportY,
            state.ScrollViewer.Viewport.Width,
            state.ScrollViewer.Viewport.Height);

        // 持久化视口位置，使其能熬过序列化往返。
        viewModel.Layout.ViewportOffset = new Offset(viewportX, viewportY);
    }

    private static void UpdateGridDecorator(IWorkflowTreeViewModel viewModel, SurfaceState state)
    {
        if (state.GridDecorator is not IWorkflowGridDecorator decorator || state.ScrollViewer is null)
            return;

        decorator.ScrollOffsetX = state.ScrollViewer.Offset.X;
        decorator.ScrollOffsetY = state.ScrollViewer.Offset.Y;
        decorator.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        decorator.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;

        // 让虚拟化可见区修正与装饰器的浮动标尺带保持一致，标尺下方的节点才不会提前一个标尺厚度被剔除。
        viewModel.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand);
    }

    private static void UpdateMinimapOverlay(IWorkflowTreeViewModel viewModel, SurfaceState state)
    {
        if (state.MinimapOverlay is not IWorkflowMinimapOverlay minimap || state.ScrollViewer is null)
            return;

        minimap.ScrollOffsetX = state.ScrollViewer.Offset.X;
        minimap.ScrollOffsetY = state.ScrollViewer.Offset.Y;
        minimap.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        minimap.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
        minimap.ViewportWidth = state.ScrollViewer.Viewport.Width;
        minimap.ViewportHeight = state.ScrollViewer.Viewport.Height;
        minimap.WorkflowTree = viewModel;
    }

    private static double GetHorizontalScrollMaximum(ScrollViewer scrollViewer)
        => WorkflowSurfaceMath.ScrollMax(scrollViewer.Extent.Width, scrollViewer.Viewport.Width);

    private static double GetVerticalScrollMaximum(ScrollViewer scrollViewer)
        => WorkflowSurfaceMath.ScrollMax(scrollViewer.Extent.Height, scrollViewer.Viewport.Height);

    private static bool ShouldStartPan(PointerPressedEventArgs e, SurfaceState state)
    {
        var properties = e.GetCurrentPoint(state.ScrollViewer!).Properties;
        return (properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed)
            && IsSurfaceBlankInteraction(e.Source, state);
    }

    private static bool IsPanStillActive(UserControl host, PointerEventArgs e)
    {
        var properties = e.GetCurrentPoint(host).Properties;
        return properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed;
    }

    private static bool IsSurfaceBlankInteraction(object? source, SurfaceState state)
    {
        if (source is not Visual visual)
            return false;

        if (IsWorkflowNodeOrSlotVisual(visual))
            return false;

        var ancestors = visual.GetVisualAncestors().OfType<Visual>().ToArray();
        if (ancestors.Any(IsWorkflowNodeOrSlotVisual))
            return false;

        if (IsWorkflowLinkVisual(visual) || ancestors.Any(IsWorkflowLinkVisual))
            return true;

        return ReferenceEquals(visual, state.Canvas)
            || ReferenceEquals(visual, state.ScrollViewer)
            || ReferenceEquals(visual, state.PointerPressSource)
            || ReferenceEquals(visual, state.GridDecorator)
            || ancestors.Any(x => ReferenceEquals(x, state.Canvas)
                || ReferenceEquals(x, state.ScrollViewer)
                || ReferenceEquals(x, state.PointerPressSource)
                || ReferenceEquals(x, state.GridDecorator)
                || string.Equals(x.GetType().Name, "ScrollContentPresenter", StringComparison.Ordinal));
    }

    private static bool IsWorkflowNodeOrSlotVisual(Visual source)
        => source is StyledElement { DataContext: IWorkflowNodeViewModel or IWorkflowSlotViewModel };

    private static bool IsWorkflowLinkVisual(Visual source)
        => source is StyledElement element
            && (element.DataContext is IWorkflowLinkViewModel
                || string.Equals(element.GetType().Name, "BezierCurveView", StringComparison.Ordinal)
                || string.Equals(element.GetType().Name, "PolylineCurveView", StringComparison.Ordinal));


    }
