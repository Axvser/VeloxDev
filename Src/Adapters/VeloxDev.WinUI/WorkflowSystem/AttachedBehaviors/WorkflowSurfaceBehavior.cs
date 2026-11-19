using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;
using Windows.System;
using Windows.UI.Core;
using Wf = VeloxDev.WorkflowSystem;
using PlatformInput = Windows.UI.Core;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSurfaceBehavior : DependencyObject
{
    private sealed class SurfaceState
    {
        public bool IsPanning { get; set; }
        public Windows.Foundation.Point PanStart { get; set; }
        public Windows.Foundation.Point PanStartOffset { get; set; }
        public bool IsVisibleRegionUpdateQueued { get; set; }
        public UserControl? Host { get; set; }
        public ScrollViewer? ScrollViewer { get; set; }
        public Canvas? Canvas { get; set; }
        public FrameworkElement? GridDecorator { get; set; }
        public FrameworkElement? MinimapOverlay { get; set; }
        public FrameworkElement? PointerPressSource { get; set; }
        public PointerEventHandler? WheelHandler { get; set; }

        // 这一笔按下已被路由过（组件自己路由的，或表面在平移那一处路由的）。
        // 不能用 e.Handled 代替：表面的按下处理器是 handledEventsToo:true 挂的，它无视 Handled 照跑，
        // 于是同一笔会被路由第二遍。这个标记是那一笔「已经路由」的唯一凭据。
        public bool PressRouted { get; set; }

        // 连线右键菜单：菜单由模板声明（条目归用户，见 LinkMenuKeyProperty），订阅、定位、弹出、挂起都在这里。
        public MenuFlyout? LinkMenu { get; set; }
        public IWorkflowLinkViewModel? MenuLink { get; set; }

        // 这棵树的输入路由：菜单开着时由它挂起指针跟踪，接线的那两个订阅也从它来。
        public WorkflowInput? Input { get; set; }

        // 挂在输入路由上的画布滚动口。本类拥有它、宿主拿它去滚；换树时连同 Input 一起换新。
        public IWorkflowSurfaceScroller? Scroller { get; set; }

        public EventHandler<Wf.PointerPressedEventArgs>? MenuPressed { get; set; }
        public EventHandler<IWorkflowLinkViewModel>? MenuLinkRemoved { get; set; }
        public EventHandler<object>? MenuOpened { get; set; }
        public EventHandler<object>? MenuClosed { get; set; }

        // 上一棵被挂上来的树（引用比较）。恢复只因「换了树」触发一次，之后的 Refresh 不再把用户滚回去。
        public IWorkflowTreeViewModel? LastRestoreTree { get; set; }
        public bool HasPendingRestore { get; set; }
        public double PendingRestoreX { get; set; }
        public double PendingRestoreY { get; set; }

        // 已经请求、但 ChangeView 还没落地的滚动目标（NaN = 该轴没有在途请求）。WinUI 的 ChangeView 是异步的，
        // 而负向扩画布会同时把 ActualOffset 写进 Canvas.Translation（合成变换当帧生效）；两边不同步就会出现
        // 「内容先被平移、再被滚动拉回」的闪动。挂起期间 Translate = ActualOffset − (目标 − 实际滚动)，
        // 内容当帧就落在目标位置；ViewChanged 落地后清掉目标，Translate 回到 ActualOffset，画面不二次跳。
        public double PendingScrollX { get; set; } = double.NaN;
        public double PendingScrollY { get; set; } = double.NaN;

        // 在途目标的最长寿命：ChangeView 落地会立即清；若它被夹取/忽略、或用户在途中自己拖了滚动条，
        // 这个 150ms 的定时器兜底清掉补偿，避免画面永远挂在到不了的目标上。每次请求都重新计时。
        public Microsoft.UI.Dispatching.DispatcherQueueTimer? PendingScrollTimer { get; set; }
    }

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ScrollViewerNameProperty = DependencyProperty.RegisterAttached(
        "ScrollViewerName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CanvasNameProperty = DependencyProperty.RegisterAttached(
        "CanvasName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty GridDecoratorNameProperty = DependencyProperty.RegisterAttached(
        "GridDecoratorName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PointerPressSourceNameProperty = DependencyProperty.RegisterAttached(
        "PointerPressSourceName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty MinimapOverlayNameProperty = DependencyProperty.RegisterAttached(
        "MinimapOverlayName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ZoomEnabledProperty = DependencyProperty.RegisterAttached(
        "ZoomEnabled",
        typeof(bool),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false, OnZoomEnabledChanged));

    /// <summary>
    /// Resource key of the context menu a right press on a link opens. The entries are the user's — declare a
    /// <see cref="MenuFlyout"/> resource under that key, put its items in it, and name the key here; the surface
    /// resolves the menu by key, feeds the pressed link to each item, positions it, opens it, and suspends the
    /// tree's <see cref="WorkflowInput"/> while it is open.
    /// <para>
    /// A key rather than the menu itself: this property sits on the surface's own root element, and a
    /// <c>{StaticResource}</c> there would be resolved before the very resource dictionary that defines it.
    /// </para>
    /// </summary>
    public static readonly DependencyProperty LinkMenuKeyProperty = DependencyProperty.RegisterAttached(
        "LinkMenuKey",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(SurfaceState),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetZoomEnabled(DependencyObject element) => (bool)element.GetValue(ZoomEnabledProperty);

    public static void SetZoomEnabled(DependencyObject element, bool value) => element.SetValue(ZoomEnabledProperty, value);

    public static string? GetScrollViewerName(DependencyObject element) => element.GetValue(ScrollViewerNameProperty) as string;

    public static void SetScrollViewerName(DependencyObject element, string? value) => element.SetValue(ScrollViewerNameProperty, value);

    public static string? GetCanvasName(DependencyObject element) => element.GetValue(CanvasNameProperty) as string;

    public static void SetCanvasName(DependencyObject element, string? value) => element.SetValue(CanvasNameProperty, value);

    public static string? GetGridDecoratorName(DependencyObject element) => element.GetValue(GridDecoratorNameProperty) as string;

    public static void SetGridDecoratorName(DependencyObject element, string? value) => element.SetValue(GridDecoratorNameProperty, value);

    public static string? GetPointerPressSourceName(DependencyObject element) => element.GetValue(PointerPressSourceNameProperty) as string;

    public static void SetPointerPressSourceName(DependencyObject element, string? value) => element.SetValue(PointerPressSourceNameProperty, value);

    public static string? GetMinimapOverlayName(DependencyObject element) => element.GetValue(MinimapOverlayNameProperty) as string;

    public static void SetMinimapOverlayName(DependencyObject element, string? value) => element.SetValue(MinimapOverlayNameProperty, value);

    public static string? GetLinkMenuKey(DependencyObject element) => element.GetValue(LinkMenuKeyProperty) as string;

    public static void SetLinkMenuKey(DependencyObject element, string? value) => element.SetValue(LinkMenuKeyProperty, value);

    public static void Refresh(UserControl host)
    {
        if (!GetIsEnabled(host))
        {
            return;
        }

        var state = host.GetValue(StateProperty) as SurfaceState ?? new SurfaceState();
        host.SetValue(StateProperty, state);
        state.Host = host;
        ResolveNamedControls(host, state);
        WireLinkMenu(host, state);
        CaptureViewportRestore(host, state);
        ApplyLayout(host, state);
        UpdateVisibleRegion(host, state);
    }

    // 连线右键菜单：**条目由模板声明**（挂在 LinkMenuKey 上），**接线在这里** —— 订输入面、定位、弹出、
    // 挂起指针跟踪，模板因此没有一行交互代码。菜单指着的那条线离树时树会报 LinkRemoved，这里收自己那份。
    private static void WireLinkMenu(UserControl host, SurfaceState state)
    {
        // 资源在 attach 之后才一定就绪（第一次 Refresh 可能早于 Resources 解析完），所以每次 Refresh 都重查一次。
        var key = GetLinkMenuKey(host);
        var menu = key is { Length: > 0 } ? FindResource(host, key) as MenuFlyout : null;

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
                // 开合由表面自己记账：菜单开着的那段时间，输入路由不再改指针目标。
                state.MenuOpened = (_, _) =>
                {
                    if (state.Input is { } open) open.IsSuspended = true;
                };
                state.MenuClosed = (_, _) =>
                {
                    if (state.Input is { } closed) closed.IsSuspended = false;
                    state.MenuLink = null;
                };
                menu.Opened += state.MenuOpened;
                menu.Closed += state.MenuClosed;
            }
        }

        var input = host.DataContext is IWorkflowTreeViewModel tree ? WorkflowInput.For(tree) : null;
        if (ReferenceEquals(input, state.Input))
        {
            return;
        }

        if (state.Input is not null)
        {
            UnsubscribeMenu(state);
        }

        state.Input = input;
        if (input is null || host.DataContext is not IWorkflowTreeViewModel bound)
        {
            return;
        }

        // 画布滚动归适配器（见 OnSurfaceWheel）：订阅方被拦下之后从这里滚，不必知道这家用的是 ScrollViewer。
        var scroller = new SurfaceScroller(state);
        state.Scroller = scroller;
        input.Scroller = scroller;
        input.Navigator = scroller;

        state.MenuPressed = (_, e) => ShowLinkMenu(host, state, e);
        state.MenuLinkRemoved = (_, link) =>
        {
            // 「菜单不能比它指着的那条线活得久」：Delete、Undo、Agent 改树都走这条路。
            if (!ReferenceEquals(state.MenuLink, link)) return;
            state.LinkMenu?.Hide();
        };
        ((Wf.IInputEvents)bound.GetHelper()).Input.PointerPressed += state.MenuPressed;
        bound.GetHelper().LinkRemoved += state.MenuLinkRemoved;
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

        UnsubscribeMenu(state);
    }

    private static void UnsubscribeMenu(SurfaceState state)
    {
        if (state.Input is { } input)
        {
            var helper = input.Tree.GetHelper();
            if (state.MenuPressed is not null && helper is Wf.IInputEvents events)
            {
                events.Input.PointerPressed -= state.MenuPressed;
            }

            if (state.MenuLinkRemoved is not null) helper.LinkRemoved -= state.MenuLinkRemoved;
        }

        // 只在还挂着自己那一个时才摘：同一棵树上两个表面时，摘别人的会把对方的滚动口一起清掉。
        if (state.Input is { } released && ReferenceEquals(released.Scroller, state.Scroller))
        {
            released.Scroller = null;
            released.Navigator = null;
        }

        state.Scroller = null;
        state.Input = null;
        state.MenuPressed = null;
        state.MenuLinkRemoved = null;
        state.MenuLink = null;
    }

    // 右键落在表面上，而弹出要相对某个元素；表面同时知道画布与自己的位置，所以菜单由表面弹。
    private static void ShowLinkMenu(UserControl host, SurfaceState state, Wf.PointerPressedEventArgs e)
    {
        // 只有右键、且落在连线上才弹：空白画布没有可操作的对象。
        if (e.Button != Wf.MouseButton.Right) return;
        if (e.Target is not IWorkflowLinkViewModel link) return;
        if (state.LinkMenu is null || state.Canvas is null) return;
        if (host.DataContext is not IWorkflowTreeViewModel) return;

        // 链上更靠前的一级（连线自己）可以否决这次按下 —— 它说不给菜单，这里就不给。
        if (e.Handle.PreventDefault) return;

        var anchor = state.PointerPressSource ?? (FrameworkElement)host;

        // 画布坐标 → 锚点坐标：画布自带渲染平移（刻度带）与合成平移，TransformToVisual 一并算进去。
        var point = state.Canvas.TransformToVisual(anchor)
            .TransformPoint(new Windows.Foundation.Point(e.Position.Horizontal, e.Position.Vertical));

        state.MenuLink = link;

        state.LinkMenu.XamlRoot = host.XamlRoot;

        // MenuFlyout 继承 FlyoutBase → DependencyObject，不在可视树上也没有 DataContext，逐条把这条连线喂给条目。
        foreach (var item in state.LinkMenu.Items)
        {
            if (item is FrameworkElement element)
            {
                element.DataContext = link;
            }
        }

        state.LinkMenu.ShowAt(anchor, new FlyoutShowOptions { Position = point });
    }

    // WinUI 没有 TryFindResource：从宿主沿父链逐级查 Resources，最后落到 Application 资源。
    // 模板把菜单资源声明在自己的 <UserControl.Resources> 里，所以第一站就是宿主本身。
    private static object? FindResource(DependencyObject host, string key)
    {
        for (var current = host; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element && element.Resources.TryGetValue(key, out var found))
            {
                return found;
            }
        }

        return Application.Current?.Resources.TryGetValue(key, out var appFound) == true ? appFound : null;
    }

    // 树刚挂上来且不是上一棵：把它存档里的视口位置排进待恢复（世界 → 滚动）。
    // 必须在 UpdateVisibleRegion 之前 —— 那次排队的 ApplyVisibleRegion 会用控件当前（还没滚过去的）
    // 位置覆盖 ViewportOffset。
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

    // 有待恢复就把滚动交给宿主，并让这一次回调跳过 ApplyVisibleRegion。
    // ChangeView 是异步的：此刻读 HorizontalOffset 拿到的还是旧偏移，写回去就把存档位置抹了；
    // 滚动落地会触发 ViewChanged → Refresh → 再排一次队列，那一次读到的才是恢复后的位置。
    private static bool ApplyPendingViewportRestore(SurfaceState state)
    {
        if (!state.HasPendingRestore || state.ScrollViewer is not { } viewer) return false;

        state.HasPendingRestore = false;

        RequestScroll(
            state,
            WorkflowSurfaceMath.ClampValue(state.PendingRestoreX, 0, GetHorizontalScrollMaximum(viewer)),
            WorkflowSurfaceMath.ClampValue(state.PendingRestoreY, 0, GetVerticalScrollMaximum(viewer)));
        return true;
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UserControl control)
        {
            return;
        }

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

        var state = new SurfaceState();
        control.SetValue(StateProperty, state);

        // 这一家没有可设的 `Focusable`（WinUI 是 `IsTabStop` + `Focus(FocusState)`）。焦点要落在**滚动容器**上，
        // 不是宿主：宿主里第一个可聚焦的后代常常是节点卡里的 TextBox，`host.Focus()` 会把焦点交给它，
        // 而滚动容器随后会把它 BringIntoView —— 平移或 Locate 之后点一下画布，视口就被拽回那个输入框。
        // 滚动容器本身在视口里、不会因此滚动，键仍从它冒泡到宿主的 KeyDown。悬停到连线时收焦点同理。

        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.DataContextChanged += OnDataContextChanged;
        control.PointerMoved += OnPointerMoved;
        control.PointerEntered += OnPointerEntered;
        control.PointerExited += OnPointerExited;
        // 普通滚轮：这家没有预览/捕获相，`ScrollViewer` 在冒泡相上先吃掉滚轮并标记 handled，普通订阅
        // 在能滚的时候一次都不会执行（实测：两格滚轮零到达）。用 handledEventsToo 挂，代价是位置与目标
        // 取自**滚动之后** —— 与 Ctrl+滚轮那条已经接受的取舍相同。
        control.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        // 连线交互要看到整个 surface 上的按下与按键，包括被其它处理器标记为 Handled 的那些
        control.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLinkPointerPressed), true);
        control.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnLinkKeyDown), false);
        control.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(OnLinkKeyUp), false);
        ResolveNamedControls(control, state);
        Refresh(control);
    }

    private static void Detach(UserControl control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.DataContextChanged -= OnDataContextChanged;
        control.PointerMoved -= OnPointerMoved;
        control.PointerEntered -= OnPointerEntered;
        control.PointerExited -= OnPointerExited;
        control.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased));
        control.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLinkPointerPressed));
        control.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnLinkKeyDown));
        control.RemoveHandler(UIElement.KeyUpEvent, new KeyEventHandler(OnLinkKeyUp));

        if (control.GetValue(StateProperty) is SurfaceState state)
        {
            state.PendingScrollTimer?.Stop();
            state.PendingScrollTimer = null;
            UnsubscribeResolvedControls(state);
            UnwireLinkMenu(state);
        }

        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control)
        {
            Refresh(control);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is SurfaceState state)
        {
            state.IsPanning = false;
        }
    }

    private static void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is UserControl control)
        {
            // 换了树：上一棵树排的滚动目标不再有意义，别让补偿带到新树上。
            if (control.GetValue(StateProperty) is SurfaceState state)
            {
                state.PendingScrollX = double.NaN;
                state.PendingScrollY = double.NaN;
                state.PendingScrollTimer?.Stop();
            }

            Refresh(control);
        }
    }

    private static void ResolveNamedControls(UserControl control, SurfaceState state)
    {
        UnsubscribeResolvedControls(state);

        var scrollViewerName = GetScrollViewerName(control);
        if (!string.IsNullOrWhiteSpace(scrollViewerName))
        {
            var resolvedViewer = control.FindName(scrollViewerName) as ScrollViewer;
            if (!ReferenceEquals(resolvedViewer, state.ScrollViewer))
            {
                // 换了滚动容器：旧容器上的在途目标不能带过来。
                state.PendingScrollX = double.NaN;
                state.PendingScrollY = double.NaN;
                state.PendingScrollTimer?.Stop();
                state.PendingScrollTimer = null;
            }

            state.ScrollViewer = resolvedViewer;

            // 焦点要能落在滚动容器本身上（见 Attach 的注释）。模板里的 ScrollViewer 默认不可 Tab 停留，
            // 不置这一位的话 `Focus()` 会失败，平台转而把焦点交给画布里第一个可聚焦的后代（节点卡的 TextBox），
            // 随后 BringIntoView 就把视口拽回那个输入框。
            if (state.ScrollViewer is { } viewer) viewer.IsTabStop = true;
        }

        var canvasName = GetCanvasName(control);
        if (!string.IsNullOrWhiteSpace(canvasName))
        {
            state.Canvas = control.FindName(canvasName) as Canvas;
        }

        var gridDecoratorName = GetGridDecoratorName(control);
        if (!string.IsNullOrWhiteSpace(gridDecoratorName))
        {
            state.GridDecorator = control.FindName(gridDecoratorName) as FrameworkElement;
        }

        var minimapOverlayName = GetMinimapOverlayName(control);
        if (!string.IsNullOrWhiteSpace(minimapOverlayName))
        {
            state.MinimapOverlay = control.FindName(minimapOverlayName) as FrameworkElement;
        }

        var pointerPressSourceName = GetPointerPressSourceName(control);
        if (!string.IsNullOrWhiteSpace(pointerPressSourceName))
        {
            state.PointerPressSource = control.FindName(pointerPressSourceName) as FrameworkElement;
        }

        if (state.PointerPressSource is not null)
        {
            state.PointerPressSource.PointerPressed += OnPointerPressed;
        }

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.ViewChanged += OnViewChanged;
        }

        // 滚轮的挂接不跟缩放开关走：普通滚轮的汇报与缩放是两件事。
        HookWheel(state);
    }

    private static void UnsubscribeResolvedControls(SurfaceState state)
    {
        if (state.PointerPressSource is not null)
        {
            state.PointerPressSource.PointerPressed -= OnPointerPressed;
        }

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.ViewChanged -= OnViewChanged;
        }

        UnhookWheel(state);
        state.PointerPressSource = null;
        state.ScrollViewer = null;
        state.Canvas = null;
        state.GridDecorator = null;
        state.MinimapOverlay = null;
    }

    private static void OnZoomEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UserControl control || control.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        // 滚轮的挂接与这个开关无关（汇报永远要发，缩放与否在处理器里判），这里只保证它还在：
        // 换滚动容器时解析路径会重新挂，而只改开关时补一次是幂等的。
        _ = e;
        HookWheel(state);
    }

    // 挂在下代（画布）而不是 SCROLLVIEWER 自己：这家没有隧道相，`ScrollPresenter` 在**冒泡路径上比
    // ScrollViewer 更早**处理这一笔并已经滚过一段，等处理器跑到 ScrollViewer 时再置 Handled 已经晚了
    // （实测：挂在 ScrollViewer 上即使置 Handled，普通滚轮照样滚 74）。画布是 ScrollViewer 的内容、
    // 位于 `ScrollPresenter` 之前，在这里置 Handled 才真的拦得住 —— 「平台的滚动容器一次都不许滚画布」。
    // 用 handledEventsToo:true 挂接，节点卡片先处理了滚轮也仍触发（它们不处理这一笔）。
    // 挂接**不跟缩放开关走**：普通滚轮的汇报与缩放是两件事，关掉缩放的宿主一样要收得到。
    private static void HookWheel(SurfaceState state)
    {
        if (state.Canvas is not null && state.WheelHandler is null)
        {
            state.WheelHandler = new PointerEventHandler(OnSurfaceWheel);
            state.Canvas.AddHandler(UIElement.PointerWheelChangedEvent, state.WheelHandler, true);
        }
    }

    private static void UnhookWheel(SurfaceState state)
    {
        if (state.Canvas is not null && state.WheelHandler is not null)
        {
            state.Canvas.RemoveHandler(UIElement.PointerWheelChangedEvent, state.WheelHandler);
            state.WheelHandler = null;
        }
    }

    // 内层闸门：指针底下最近的、画布**之内**的滚动容器，沿这一笔方向还有没有余量。
    // 这家与 WPF/Avalonia 的判据同形，但问题不同：那两家挂在内层**之前**，问的是「这一笔该不该归内层」（前瞻）；
    // 这家挂在画布上、带 handledEventsToo，内层 ScrollViewer 已经先跑过，问的是「它到底消化了没有」。
    // 实测（2026-10-11 探针）：内层能滚时置 Handled（它滚了），**不能滚时也置 Handled**（内层到边后再滚两格，
    // `VerticalOffset` 停在 `ScrollableHeight` 一动不动，入口 `Handled` 仍是 true）。所以「Handled == 被消费」
    // 不成立；唯一的判据是沿方向的绝对余量：向下滚（delta < 0）看 `VerticalOffset < ScrollableHeight`，
    // 向上滚看 `VerticalOffset > 0`，横轴同理。还有余量 ⇒ 这一笔归内层（不路由、不默认滚，它自己那一滚已经落地）；
    // 到边 ⇒ 归画布（本类自己滚，可被订阅者否决）。画布自己那台是**祖先**、卡片里那些是**后代** ——
    // 向上走、先碰到画布就说明底下没有滚动容器，这一笔归画布。
    private static bool InnerScrollViewerHasRoom(DependencyObject? source, SurfaceState state, double deltaX, double deltaY)
    {
        if (source is null || (deltaX == 0d && deltaY == 0d))
        {
            return false;
        }

        foreach (var candidate in SelfThenAncestors(source))
        {
            if (ReferenceEquals(candidate, state.Canvas))
            {
                return false;
            }

            if (candidate is not ScrollViewer inner || ReferenceEquals(inner, state.ScrollViewer))
            {
                continue;
            }

            var verticalRoom = deltaY == 0d
                || (deltaY < 0d ? inner.VerticalOffset < inner.ScrollableHeight : inner.VerticalOffset > 0d);
            var horizontalRoom = deltaX == 0d
                || (deltaX < 0d ? inner.HorizontalOffset < inner.ScrollableWidth : inner.HorizontalOffset > 0d);

            // 这一笔里非零的那几轴都要有余量才算「归它」；单轴到边就该由画布接。
            return verticalRoom && horizontalRoom;
        }

        return false;
    }

    // 自身在前，再沿可视祖先往外走。不用 Enumerable.Prepend：本工程多档 TFM，它不都在。
    private static IEnumerable<DependencyObject> SelfThenAncestors(DependencyObject element)
    {
        yield return element;

        foreach (DependencyObject ancestor in EnumerateVisualAncestors(element))
        {
            yield return ancestor;
        }
    }

    private static void OnSurfaceWheel(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var host = EnumerateVisualAncestors(source).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        // 这一家的 MouseWheelDelta 同时承载竖滚与横滚，方向由 IsHorizontalMouseWheel 分流
        // （横滚才写 DeltaX，否则零增量轴不许动 —— IWorkflowSurfaceScroller 的 remarks）。
        var properties = e.GetCurrentPoint(source as UIElement ?? host).Properties;
        var horizontal = properties.IsHorizontalMouseWheel;
        var deltaX = horizontal ? properties.MouseWheelDelta : 0d;
        var deltaY = horizontal ? 0d : (double)properties.MouseWheelDelta;
        var delta = properties.MouseWheelDelta;

        // 普通滚轮（含横向）**整笔归适配器**：平台的滚动容器一次都不许滚画布，默认滚由这里在路由之后补上。
        // 订阅方置 PreventDefault 就是「这一笔不滚」，由他走 Scroller 决定往哪滚 —— 于是七家宿主是同一段代码。
        // Ctrl+竖滚走缩放（横滚不参与缩放），所以这里多一个「不是缩放那一笔」的条件。
        var isZoom = !horizontal && e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) && GetZoomEnabled(host);
        if (!isZoom)
        {
            // 取不到表面状态也拦：滚轮归适配器这条不因为一次查找失败就把画布交回平台。
            if (host.GetValue(StateProperty) is not SurfaceState reportState)
            {
                e.Handled = true;
                return;
            }

            // 内层闸门：内层 ScrollViewer 已经先跑过。它沿这一笔方向还有余量 ⇒ 这一笔归它，别插手 ——
            // 它自己那一滚已经落地，这里既不路由也不默认滚（到边时返回 false，于是照旧由画布接）。
            if (InnerScrollViewerHasRoom(e.OriginalSource as DependencyObject, reportState, deltaX, deltaY))
            {
                return;
            }

            // 拦下平台那一手：置 Handled 之后 ScrollPresenter 不再滚画布。
            e.Handled = true;

            var handle = RoutePointer(reportState, viewModel, e, host, e.OriginalSource as DependencyObject,
                (position, target, h) => new Wf.PointerWheelEventArgs(
                    position, Modifiers(e.KeyModifiers), host, target, deltaX, deltaY, h));

            if (!handle.PreventDefault)
            {
                reportState.Scroller?.ScrollBy(deltaX, deltaY);
            }

            return;
        }

        // Ctrl+滚轮也进路由，订阅者才有机会说「这一次别缩放」。
        if (host.GetValue(StateProperty) is SurfaceState zoomState)
        {
            var zoomHandle = RoutePointer(zoomState, viewModel, e, host, e.OriginalSource as DependencyObject,
                (position, target, handle) => new Wf.PointerWheelEventArgs(
                    position, Modifiers(e.KeyModifiers), host, target, 0d, delta, handle));

            if (zoomHandle.PreventDefault)
            {
                return;
            }
        }

        // 滚轮向上（增量为正）放大：Scale 是折叠因子，放大要除以 1/1.1。
        var factor = delta > 0 ? 1 / 1.1 : 1.1;
        var next = Math.Max(0.1, Math.Min(10, viewModel.Layout.Scale.Horizontal * factor));
        var layout = viewModel.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter
            && host.GetValue(StateProperty) is SurfaceState state
            && state.ScrollViewer is { } sv)
        {
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                EffectiveScrollX(state), EffectiveScrollY(state), sv.ViewportWidth, sv.ViewportHeight, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);
            // 深度放大把负向内容折叠越过固定画布平移（ActualOffset == NegativeOffset）；必须在 ApplyLayout 采纳新偏移前扩大覆盖。
            // 下面的 PivotCenterScroll 会读到长大的偏移，多出的覆盖被滚动目标吸收、枢轴保持居中 —— 无需手动增量。见 WorkflowSurfaceMath.EnsureNegativeCover。
            WorkflowSurfaceMath.EnsureNegativeCover(viewModel);

            // 强制一次布局趟，让 ScrollViewer 在我们落滚动之前采纳新范围；否则 ChangeView 按陈旧范围夹取、枢轴偏离中心，下一次滚轮又从这个误差重捕 —— 累积漂移表现为缩放抖动。
            ApplyLayout(host, state);
            state.Canvas?.UpdateLayout();
            sv.UpdateLayout();
            host.UpdateLayout();

            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, sv.ViewportWidth, sv.ViewportHeight);
            var maxH = GetHorizontalScrollMaximum(sv);
            var maxV = GetVerticalScrollMaximum(sv);

            // 越界扩展画布让枢轴总能到达；单纯夹取会把枢轴推离中心并逐格漂移。缩放不动画布几何（ActualOffset == NegativeOffset，固定）——只动滚动。
            var newX = WorkflowSurfaceMath.ClampScrollOffset(tx, maxH, layout, horizontal: true);
            var newY = WorkflowSurfaceMath.ClampScrollOffset(ty, maxV, layout, horizontal: false);
            if (Math.Abs(newX - tx) > double.Epsilon || Math.Abs(newY - ty) > double.Epsilon)
            {
                ApplyLayout(host, state);
                state.Canvas?.UpdateLayout();
                sv.UpdateLayout();
                host.UpdateLayout();
                maxH = GetHorizontalScrollMaximum(sv);
                maxV = GetVerticalScrollMaximum(sv);
            }

            var committedX = WorkflowSurfaceMath.ClampValue(tx, 0, maxH);
            var committedY = WorkflowSurfaceMath.ClampValue(ty, 0, maxV);
            RequestScroll(state, committedX, committedY);

            // 在提交的滚动目标处同步重新虚拟化（与 Jalium TreeView.NotifyZoomCommitted 一致）。否则 helper 在约 10fps 的脏 tick / 低优先级视口队列上重新虚拟化，
            // 缩放连发期间池化的节点/连线视图比刚折叠的锚点慢约 100ms —— 那个窗口里连线消失/弹出。用提交的偏移，而不是 sv.HorizontalOffset（ChangeView 可能还没应用）。
            VirtualizeAtScroll(viewModel, committedX, committedY, sv.ViewportWidth, sv.ViewportHeight);
        }
        else
        {
            layout.Scale = new Scale(next, next);
            // 世界原点缩放：只有重应用布局时画布平移才变，所以覆盖长大后把新偏移走视口居中分支同样的布局趟推出去（trim 示例是视口居中，此路径通常休眠）。
            if (WorkflowSurfaceMath.EnsureNegativeCover(viewModel)
                && host.GetValue(StateProperty) is SurfaceState fallbackState
                && fallbackState.Canvas is { } fallbackCanvas
                && fallbackState.ScrollViewer is { } fallbackViewer)
            {
                ApplyLayout(host, fallbackState);
                fallbackCanvas.UpdateLayout();
                fallbackViewer.UpdateLayout();
                host.UpdateLayout();
            }

            // 覆盖没长大也要重新虚拟化：折叠可能把一个跨越窗口边缘的盒子缩小到窗口外，而池化视图否则只能在约 10fps 的脏 tick / 低优先级队列上追上。这里滚动不变，当前偏移有效。
            if (host.GetValue(StateProperty) is SurfaceState elseState
                && elseState.ScrollViewer is { } elseViewer)
            {
                VirtualizeAtScroll(viewModel, EffectiveScrollX(elseState), EffectiveScrollY(elseState),
                    elseViewer.ViewportWidth, elseViewer.ViewportHeight);
            }
        }
        e.Handled = true;
    }

    // 画布滚动的中立请求。收的是**滚轮增量**（正 = 远离用户），换算成这家自己的步长 ——
    // 与 ScrollViewer 自己那一手一致，否则接管之后手感会变。
    private sealed class SurfaceScroller : IWorkflowSurfaceScroller, IWorkflowSurfaceNavigator
    {
        // 一格滚轮是 WHEEL_DELTA（120）；这家 ScrollViewer 自己一格滚 74（实测：一格 74、四格 296）——
        // 复刻它，接管之后手感才不变（与 WPF 那边 48、Avalonia 那边 50 是同一件事）。
        private const double Notch = 120d;
        private const double PixelsPerNotch = 74d;

        private readonly SurfaceState state;

        public SurfaceScroller(SurfaceState state) => this.state = state;

        public void ScrollBy(double wheelDeltaX, double wheelDeltaY)
        {
            if (state.ScrollViewer is not { } viewer)
            {
                return;
            }

            // 横轴正 = 向右，偏移同向取加；纵轴正 = 远离用户 = 视口上移，偏移取减 —— 两轴的「正」不是同一个物理方向。
            var dx = (wheelDeltaX / Notch) * PixelsPerNotch;
            var dy = -(wheelDeltaY / Notch) * PixelsPerNotch;

            // 零增量不动那一轴（IWorkflowSurfaceScroller.ScrollBy 的 remarks）：只把非零那轴算进目标，另一轴原样沿用。
            // 起点取逻辑位置：上一笔 ChangeView 可能还在途，此时实际偏移是旧的。
            var desiredX = dx != 0d ? EffectiveScrollX(state) + dx : EffectiveScrollX(state);
            var desiredY = dy != 0d ? EffectiveScrollY(state) + dy : EffectiveScrollY(state);

            var maxH = GetHorizontalScrollMaximum(viewer);
            var maxV = GetVerticalScrollMaximum(viewer);

            var host = state.Host;
            if (host is null || state.Input is not { } input)
            {
                // 拿不到布局时退回旧的「到边即停」，至少不越界。
                RequestScroll(
                    state,
                    dx != 0d ? WorkflowSurfaceMath.ClampValue(desiredX, 0d, maxH) : null,
                    dy != 0d ? WorkflowSurfaceMath.ClampValue(desiredY, 0d, maxV) : null);
                return;
            }

            // 与平移走同一条边界语义：越界经 ClampScrollOffset 撑大画布（extendRatio 用平移那个），而不是到边即停。
            // 两处若各留一套语义，同一个画布「拖能撑出去、滚不能」。
            var newX = dx != 0d
                ? WorkflowSurfaceMath.ClampScrollOffset(
                    desiredX, maxH, input.Tree.Layout, horizontal: true, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio)
                : desiredX;
            var newY = dy != 0d
                ? WorkflowSurfaceMath.ClampScrollOffset(
                    desiredY, maxV, input.Tree.Layout, horizontal: false, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio)
                : desiredY;

            if (newX != desiredX || newY != desiredY)
            {
                // 撑大过画布：extent 是懒的，先强制一次布局趟让 ScrollViewer 认下新范围、再重读 max
                //（与缩放那支同一套：ChangeView 会按陈旧范围夹取，见 OnSurfaceWheel 的缩放分支）。
                ApplyLayout(host, state);
                state.Canvas?.UpdateLayout();
                viewer.UpdateLayout();
                host.UpdateLayout();
                maxH = GetHorizontalScrollMaximum(viewer);
                maxV = GetVerticalScrollMaximum(viewer);
                newX = Math.Min(newX, maxH);
                newY = Math.Min(newY, maxV);
            }

            // 只写增量非零的那一轴：另一轴传 null，免得把它夹回范围里（零增量不许动那一轴）。
            var committedX = dx != 0d ? WorkflowSurfaceMath.ClampValue(newX, 0d, maxH) : EffectiveScrollX(state);
            var committedY = dy != 0d ? WorkflowSurfaceMath.ClampValue(newY, 0d, maxV) : EffectiveScrollY(state);
            RequestScroll(
                state,
                dx != 0d ? committedX : null,
                dy != 0d ? committedY : null);

            // 用提交的偏移同步重新虚拟化：ChangeView 是异步的，此刻读到的还是旧偏移。
            VirtualizeAtScroll(input.Tree, committedX, committedY, viewer.ViewportWidth, viewer.ViewportHeight);
        }

        public void CenterOn(double worldX, double worldY)
        {
            if (state.ScrollViewer is not { } viewer) return;
            if (state.Host is not { } host || host.DataContext is not IWorkflowTreeViewModel viewModel) return;

            var layout = viewModel.Layout;
            var (desiredX, desiredY) = WorkflowSurfaceMath.CenterScroll(
                worldX, worldY, viewer.ViewportWidth, viewer.ViewportHeight, layout);
            var maxH = GetHorizontalScrollMaximum(viewer);
            var maxV = GetVerticalScrollMaximum(viewer);

            // 画布不够大时和滚轮/平移一样按差额长出去，而不是把目标夹到边上。
            var clampedX = WorkflowSurfaceMath.ClampScrollOffset(desiredX, maxH, layout, horizontal: true);
            var clampedY = WorkflowSurfaceMath.ClampScrollOffset(desiredY, maxV, layout, horizontal: false);
            if (clampedX != desiredX || clampedY != desiredY)
            {
                // 负向增长会改动 ActualOffset，正向增长会改动 ActualSize；先让布局认下新范围，再按新偏移重算目标。
                ApplyLayout(host, state);
                state.Canvas?.UpdateLayout();
                viewer.UpdateLayout();
                host.UpdateLayout();
                maxH = GetHorizontalScrollMaximum(viewer);
                maxV = GetVerticalScrollMaximum(viewer);
                (desiredX, desiredY) = WorkflowSurfaceMath.CenterScroll(
                    worldX, worldY, viewer.ViewportWidth, viewer.ViewportHeight, layout);
            }

            var committedX = WorkflowSurfaceMath.ClampValue(desiredX, 0d, maxH);
            var committedY = WorkflowSurfaceMath.ClampValue(desiredY, 0d, maxV);

            RequestScroll(state, committedX, committedY);
            VirtualizeAtScroll(viewModel, committedX, committedY, viewer.ViewportWidth, viewer.ViewportHeight);
        }
    }

    private static void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement source)
        {
            return;
        }

        var host = EnumerateVisualAncestors(source).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state || state.ScrollViewer is null)
        {
            return;
        }

        // 节点卡片或插槽（比表面先跑）已经为这一笔路由过：这次不归表面 —— 不平移，也不再路由。
        // 标记留在这里不清，由最后跑的连线处理器消费；清了那一处就会把同一笔再路由一遍。
        if (state.PressRouted)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        // 平台说不出是哪个键就不路由（与连线那一处同一判据），否则会造出一笔无键的按下。
        var button = CurrentButton(e, host);
        if (button == Wf.MouseButton.None)
        {
            return;
        }

        // 表面是自己的身体，这一笔只能自己路由：句柄在手才能让订阅者否决这一次平移。若留给更外层
        // （连线那一处）去路由，本处理器先跑，读到的只会是上一次按下的句柄。
        var handle = RoutePointer(state, viewModel, e, host, e.OriginalSource as DependencyObject,
            (position, target, h) => new Wf.PointerPressedEventArgs(
                position, Modifiers(e.KeyModifiers), host, target, button, 1, h));
        state.PressRouted = true;

        if (handle.PreventDefault || !ShouldStartPan(e, state))
        {
            return;
        }

        var point = e.GetCurrentPoint(host);
        state.IsPanning = true;
        state.PanStart = point.Position;
        // 起点取逻辑位置：上一笔 Locate/滚轮的 ChangeView 可能还没落地。
        state.PanStartOffset = new Windows.Foundation.Point(EffectiveScrollX(state), EffectiveScrollY(state));
        source.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private static void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        OnCanvasPanMoved(host, state, e);
        if (state.IsPanning)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.ScrollViewer is null)
        {
            return;
        }

        var anchor = ToCanvasLocalAnchor(state, e, viewModel.Layout);
        viewModel.SetPointerCommand.Execute(anchor);

        // 拉线期间把指针下的候选接收口报给树：槽的 preview 位和虚拟连线的阶段都靠它；
        // 这一笔输入路由本来就没被压掉，所以不额外改写它。
        if (viewModel.VirtualLink.IsVisible)
        {
            viewModel.StandardSetPreviewReceiver(
                SlotHitTestEx.HitTest(viewModel, anchor.Horizontal, anchor.Vertical));
        }

        // 把同一份 canvas-local 坐标交给输入面：被指到的对象由适配器从来源视觉的 DataContext 判出来。
        RoutePointer(state, viewModel, e, host, e.OriginalSource as DependencyObject,
            (position, target, handle) => new Wf.PointerMovedEventArgs(position, Modifiers(e.KeyModifiers), host, target, handle));
    }

    // 指针进入宿主边界。
    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.ScrollViewer is null)
        {
            return;
        }

        RoutePointer(state, viewModel, e, host, e.OriginalSource as DependencyObject,
            (position, target, handle) => new Wf.PointerEnteredEventArgs(position, Modifiers(e.KeyModifiers), host, target, handle));
    }

    // 指针 → canvas-local（槽锚点所在的坐标系）。画布自带 RenderTransform（WinUI demo 里的刻度带）
    // 与 ActualOffset 平移，所以鼠标进入连线所在的坐标系要逐段减回去；曲线也是以这个坐标系发布的，
    // 少减一段不会报错，只会在平移/缩放后命中另一条线。
    private static Anchor ToCanvasLocalAnchor(SurfaceState state, PointerRoutedEventArgs e, CanvasLayout layout)
    {
        var point = e.GetCurrentPoint(state.ScrollViewer!).Position;
        var canvasTranslateX = 0d;
        var canvasTranslateY = 0d;
        if (state.Canvas?.RenderTransform is TranslateTransform transform)
        {
            canvasTranslateX = transform.X;
            canvasTranslateY = transform.Y;
        }

        return WorkflowSurfaceMath.ToWorldAnchor(
            EffectiveScrollX(state) + point.X - canvasTranslateX,
            EffectiveScrollY(state) + point.Y - canvasTranslateY,
            0,
            layout);
    }

    // 指针真的离开宿主边界（而不是在子元素之间移动 —— 那条 PointerExited 也会冒泡到这里）。
    private static void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var input = WorkflowInput.For(viewModel);

        var position = e.GetCurrentPoint(host).Position;
        if (position.X >= 0 && position.Y >= 0
            && position.X <= host.ActualWidth && position.Y <= host.ActualHeight)
        {
            return;
        }

        input.Route(new Wf.PointerExitedEventArgs(new Anchor(), Modifiers(e.KeyModifiers), host, null, new WorkflowEventHandle()));
    }

    // 按下：转发给输入面裁决（是否落在某条连线上、哪个键）。不置 Handled —— 画布手势照旧。
    private static void OnLinkPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        // 宿主常把侧栏与画布装在同一个 UserControl 里（完整 Demo 的元素列表就是）：这一处挂在整个宿主上，
        // 只该处理**画布自己那块区域**里的按下。否则侧栏按钮一按就被收走焦点，慢速点击时按钮的 Click
        // 不再落地 —— 表现为「点 Locate 只让输入栏获得焦点，什么都不发生」。滚轮那处用的是同一道门。
        if (state.ScrollViewer is not { } surfaceViewer
            || !IsWithinScrollViewer(e.OriginalSource as DependencyObject, surfaceViewer))
        {
            return;
        }

        // 键要有人接：任何一次按下都先把焦点收到表面，Ctrl+Z 才有路由（原先只在悬停到连线时收，点空白收不到）。
        // 可编辑控件与按钮除外 —— 前者要输入，后者要靠这一串指针完成 Click；焦点被收走，按钮的点击就不落地。
        // 本处理器挂 handledEventsToo，冒泡链上每一下都经过它，所以这一处就是那道门。
        if (!OwnsItsFocus(e.OriginalSource as DependencyObject))
        {
            surfaceViewer.Focus(FocusState.Pointer);
        }

        // 这一笔已经有人路由过（节点卡片/插槽自己，或表面在平移那一处）：不再路由第二遍，只把标记
        // 消费掉 —— 本处理器是冒泡链上最后一个，把它清了就不会泄漏到下一次按下。
        if (state.PressRouted)
        {
            state.PressRouted = false;
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.ScrollViewer is null)
        {
            return;
        }

        var properties = e.GetCurrentPoint(host).Properties;
        var button = properties.IsRightButtonPressed ? Wf.MouseButton.Right
            : properties.IsMiddleButtonPressed ? Wf.MouseButton.Middle
            : properties.IsLeftButtonPressed ? Wf.MouseButton.Left
            : Wf.MouseButton.None;
        if (button == Wf.MouseButton.None)
        {
            return;
        }

        RoutePointer(state, viewModel, e, host, e.OriginalSource as DependencyObject,
            (position, target, handle) => new Wf.PointerPressedEventArgs(
                position, Modifiers(e.KeyModifiers), host, target, button, 1, handle));
    }

    // 落在可编辑控件或按钮里的按下，焦点归那个控件 —— 表面不该把它抢走。
    private static bool OwnsItsFocus(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox or RichEditBox or PasswordBox
                or ButtonBase or ComboBox or ToggleSwitch or RangeBase) return true;
        }

        return false;
    }

    // Delete 归 Core：本层只把按键翻译过去，由它决定「现在指针停着的哪条线」要删。
    private static void OnLinkKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var input = WorkflowInput.For(viewModel);

        // target 用路由**已经在维护**的那个指针目标，不再要求「指针停在一条线上」：Ctrl+Z 是树级的键，
        // 指针在空白画布上时链的尽头仍是树（占位/拖拽预览那类旧 sender 也不参与判定）。
        input.Route(new Wf.KeyDownEventArgs(
            ToKey(e.Key), (int)e.Key, KeyModifiersNow(), false, host, input.PointerTarget, new WorkflowEventHandle()));

        // 只吞本层自己那一手管的键。先前无条件吞，于是指针停在一条线上时方向键、翻页键、空格全被吃掉，
        // 画布那段时间对键盘整段无响应；Avalonia 只有 Delete 走得到这里、Jalium 也只吞 Delete，与它们同形。
        if (e.Key == Windows.System.VirtualKey.Delete)
        {
            e.Handled = true;
        }
    }

    private static void OnLinkKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var input = WorkflowInput.For(viewModel);
        input.Route(new Wf.KeyUpEventArgs(
            ToKey(e.Key), (int)e.Key, KeyModifiersNow(), false, host, input.PointerTarget, new WorkflowEventHandle()));
    }

    // 指针进来时统一在这里翻译：位置的 Z 取来源视图所在图层，被指到的对象由 ResolveTarget 回答。
    // 返回这一笔输入的句柄 —— 订阅者说「这一次不要框架那一手」的地方，而框架那一手就在调用方手上；
    // 丢掉句柄，订阅者的否决就没有任何人会读（Ctrl+滚轮缩放现在读它）。
    private static WorkflowEventHandle RoutePointer(
        SurfaceState state, IWorkflowTreeViewModel tree, PointerRoutedEventArgs e, UserControl host,
        DependencyObject? hit,
        Func<Anchor, IWorkflowViewModel?, WorkflowEventHandle, Wf.PointerEventArgs> args)
    {
        var anchor = ToCanvasLocalAnchor(state, e, tree.Layout);
        var input = WorkflowInput.For(tree);
        var target = ResolveTarget(hit, tree, anchor.Horizontal, anchor.Vertical, input.HitRadius);

        var handle = new WorkflowEventHandle();
        input.Route(args(anchor, target, handle));

        // 悬停到连线上就把焦点收到表面：Delete 要的按键事件经过它，而「悬停（不点）就能删」是契约。
        // 焦点放在滚动容器上，不放在宿主（理由见 Attach 上方的注释）。
        if (input.HoveredLink is not null)
        {
            state.ScrollViewer?.Focus(FocusState.Pointer);
        }

        return handle;
    }

    // 组件（节点卡片 / 插槽）把落在自己身上的按下交回来路由：这家没有隧道相，组件在表面的冒泡处理器
    // 之前跑，所以句柄只能由组件自己取。target 就是组件本身，`WorkflowInput.Chain` 里
    // slot → node → tree 那条链才走得通。解析不出宿主/树时返回 null，调用方按「没否决」处理。
    internal static WorkflowEventHandle? RouteComponentPress(
        DependencyObject? control, IWorkflowViewModel component, PointerRoutedEventArgs e)
    {
        var host = ResolveSurfaceHost(control);
        if (host?.GetValue(StateProperty) is not SurfaceState state
            || host.DataContext is not IWorkflowTreeViewModel tree
            || state.ScrollViewer is null)
        {
            return null;
        }

        var anchor = ToCanvasLocalAnchor(state, e, tree.Layout);
        var handle = new WorkflowEventHandle();
        WorkflowInput.For(tree).Route(new Wf.PointerPressedEventArgs(
            anchor, Modifiers(e.KeyModifiers), host, component, CurrentButton(e, host), 1, handle));

        // 标记这一笔已路由：表面那两处（平移、连线）到此为止，不重复路由同一个物理按下。
        state.PressRouted = true;
        return handle;
    }

    // 沿父链找启用着本行为的表面宿主 —— 组件把自己那一笔交回表面时用它定位。
    private static UserControl? ResolveSurfaceHost(DependencyObject? control)
    {
        if (control is null)
        {
            return null;
        }

        return EnumerateVisualAncestors(control).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
    }

    // 按下的键：平台把三键压在同一份 PointerPointProperties 上，逐个问。
    private static Wf.MouseButton CurrentButton(PointerRoutedEventArgs e, UIElement relativeTo)
    {
        var properties = e.GetCurrentPoint(relativeTo).Properties;
        return properties.IsRightButtonPressed ? Wf.MouseButton.Right
            : properties.IsMiddleButtonPressed ? Wf.MouseButton.Middle
            : properties.IsLeftButtonPressed ? Wf.MouseButton.Left
            : Wf.MouseButton.None;
    }

    // 指针底下是什么，由适配器回答 —— 节点和插槽也是答案的一部分。只认连线的话，路由的 Target 就永远
    // 只是「连线或空白」，`WorkflowInput.Chain` 里 slot → node → tree 那条链于是永远走不到。
    private static IWorkflowViewModel? ResolveTarget(
        DependencyObject? hit, IWorkflowTreeViewModel tree, double x, double y, double radius)
    {
        for (var current = hit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            switch (current)
            {
                case FrameworkElement { DataContext: IWorkflowNodeViewModel node }:
                    return node;
                case FrameworkElement { DataContext: IWorkflowSlotViewModel slot }:
                    return slot;
            }
        }

        return tree.HitTestVisibleLinks(x, y, radius);
    }

    private static Wf.InputModifiers Modifiers(VirtualKeyModifiers keys)
    {
        var modifiers = Wf.InputModifiers.None;
        if ((keys & VirtualKeyModifiers.Menu) != 0) modifiers |= Wf.InputModifiers.Alt;
        if ((keys & VirtualKeyModifiers.Control) != 0) modifiers |= Wf.InputModifiers.Control;
        if ((keys & VirtualKeyModifiers.Shift) != 0) modifiers |= Wf.InputModifiers.Shift;
        if ((keys & VirtualKeyModifiers.Windows) != 0) modifiers |= Wf.InputModifiers.Meta;
        return modifiers;
    }

    // 键事件不带修饰键状态，只能问当前线程的键盘状态。
    private static Wf.InputModifiers KeyModifiersNow()
    {
        var modifiers = Wf.InputModifiers.None;
        if (IsDown(VirtualKey.Menu)) modifiers |= Wf.InputModifiers.Alt;
        if (IsDown(VirtualKey.Control)) modifiers |= Wf.InputModifiers.Control;
        if (IsDown(VirtualKey.Shift)) modifiers |= Wf.InputModifiers.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= Wf.InputModifiers.Meta;
        return modifiers;

        static bool IsDown(VirtualKey key)
            => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    // 键按字母/数字/功能键三段连续区间做算术映射（两边枚举的这几段都是连续的），其余逐个点名，没点到的报 Unknown。
    private static Wf.InputKey ToKey(VirtualKey key)
    {
        if (key >= VirtualKey.A && key <= VirtualKey.Z) return Wf.InputKey.A + (key - VirtualKey.A);
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9) return Wf.InputKey.D0 + (key - VirtualKey.Number0);
        if (key >= VirtualKey.F1 && key <= VirtualKey.F12) return Wf.InputKey.F1 + (key - VirtualKey.F1);

        return key switch
        {
            VirtualKey.Cancel => Wf.InputKey.Cancel,
            VirtualKey.Back => Wf.InputKey.Back,
            VirtualKey.Tab => Wf.InputKey.Tab,
            VirtualKey.Enter => Wf.InputKey.Enter,
            VirtualKey.Escape => Wf.InputKey.Escape,
            VirtualKey.Space => Wf.InputKey.Space,
            VirtualKey.PageUp => Wf.InputKey.PageUp,
            VirtualKey.PageDown => Wf.InputKey.PageDown,
            VirtualKey.End => Wf.InputKey.End,
            VirtualKey.Home => Wf.InputKey.Home,
            VirtualKey.Left => Wf.InputKey.Left,
            VirtualKey.Up => Wf.InputKey.Up,
            VirtualKey.Right => Wf.InputKey.Right,
            VirtualKey.Down => Wf.InputKey.Down,
            VirtualKey.Insert => Wf.InputKey.Insert,
            VirtualKey.Delete => Wf.InputKey.Delete,
            // 修饰键也要点名：宿主想跟踪「Shift 现在按没按住」只能听它自己的按下与抬起，`Modifiers` 说的是
            // 「按别的键时谁被按着」。这几个值都在上面三段算术区间之外（实测 16–18、91–92、160–165），不会被那三条 if 抢走。
            VirtualKey.LeftShift => Wf.InputKey.LeftShift,
            VirtualKey.RightShift => Wf.InputKey.RightShift,
            VirtualKey.LeftControl => Wf.InputKey.LeftCtrl,
            VirtualKey.RightControl => Wf.InputKey.RightCtrl,
            VirtualKey.LeftMenu => Wf.InputKey.LeftAlt,
            VirtualKey.RightMenu => Wf.InputKey.RightAlt,
            VirtualKey.LeftWindows => Wf.InputKey.LWin,
            VirtualKey.RightWindows => Wf.InputKey.RWin,
            // 无侧的修饰键码（Shift=16、Control=17、Menu=18）：真实键盘的 WM_KEYDOWN 里左右 Shift 报的都是
            // 0x10（左右之分在扫描码里），SendInput 合成的键更是**只有**这个码。不点名就落 Unknown，宿主
            // 「按 Shift 判横滚、松手复位」那对按下/抬起就配不上 —— 按下那半还有 e.Modifiers 兜着，**抬起那半
            // 永远不触发**，于是松开 Shift 之后一直按横滚走（MAUI 那家实测过，两家是同一套 VirtualKey 词汇）。
            // 归到左侧变体，与 WPF 的 KeyInterop（VK_SHIFT → Key.LeftShift）同一条。
            VirtualKey.Shift => Wf.InputKey.LeftShift,
            VirtualKey.Control => Wf.InputKey.LeftCtrl,
            VirtualKey.Menu => Wf.InputKey.LeftAlt,
            _ => Wf.InputKey.Unknown,
        };
    }

    private static void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (state.IsPanning)
        {
            state.IsPanning = false;
            e.Handled = true;
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        viewModel.VirtualLink.Sender.State &= ~SlotState.PreviewSender;
        viewModel.ResetVirtualLinkCommand.Execute(null);

        // 松手也进输入面：谁要收「这次手势结束了」就订它，输入面自己不做任何事。
        var properties = e.GetCurrentPoint(host).Properties;
        var button = properties.IsRightButtonPressed ? Wf.MouseButton.Right
            : properties.IsMiddleButtonPressed ? Wf.MouseButton.Middle
            : Wf.MouseButton.Left;
        RoutePointer(state, viewModel, e, host, e.OriginalSource as DependencyObject,
            (position, target, handle) => new Wf.PointerReleasedEventArgs(
                position, Modifiers(e.KeyModifiers), host, target, button, 1, handle));
    }

    private static void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
        {
            return;
        }

        var host = EnumerateVisualAncestors(viewer).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is not null)
        {
            // 滚动落地后才清目标；Refresh 里的 ApplyLayout 于是回到 ActualOffset，与已落地的滚动对齐。
            if (host.GetValue(StateProperty) is SurfaceState state)
            {
                TryClearPendingScroll(state);
            }

            Refresh(host);
        }
    }

    private static void OnCanvasPanMoved(UserControl host, SurfaceState state, PointerRoutedEventArgs e)
    {
        if (!state.IsPanning || state.ScrollViewer is null || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        if (!IsPanStillActive(host, e))
        {
            state.IsPanning = false;
            state.PointerPressSource?.ReleasePointerCaptures();
            return;
        }

        var current = e.GetCurrentPoint(host).Position;
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
            // 画布的长大要先落进布局，ScrollViewer 的 Extent 才会跟着变 —— 否则这里读到的仍是旧范围，
            // 下面那次 ChangeView 会按旧 max 落短，拖到边缘再松手时画面就被扯回旧边界。
            state.Canvas?.UpdateLayout();
            state.ScrollViewer.UpdateLayout();
            host.UpdateLayout();
            maxH = GetHorizontalScrollMaximum(state.ScrollViewer);
            maxV = GetVerticalScrollMaximum(state.ScrollViewer);
            newOffsetX = Math.Min(newOffsetX, maxH);
            newOffsetY = Math.Min(newOffsetY, maxV);
            state.PanStart = current;
            state.PanStartOffset = new Windows.Foundation.Point(
                WorkflowSurfaceMath.ClampValue(newOffsetX, 0, maxH),
                WorkflowSurfaceMath.ClampValue(newOffsetY, 0, maxV));
        }

        var appliedOffsetX = WorkflowSurfaceMath.ClampValue(newOffsetX, 0, maxH);
        var appliedOffsetY = WorkflowSurfaceMath.ClampValue(newOffsetY, 0, maxV);

        RequestScroll(state, appliedOffsetX, appliedOffsetY);
        state.PanStart = current;
        state.PanStartOffset = new Windows.Foundation.Point(appliedOffsetX, appliedOffsetY);
        UpdateVisibleRegion(host, state);
        e.Handled = true;
    }

    // 当前逻辑上的滚动位置：有在途请求时取目标，否则取 ScrollViewer 实际值。
    // 所有「读当前位置」的地方都经它，避免在 ChangeView 落地前读到旧值（平移起点、指针换算、可见区、装饰器）。
    private static double EffectiveScrollX(SurfaceState state)
        => state.ScrollViewer is { } viewer
            ? (double.IsNaN(state.PendingScrollX) ? viewer.HorizontalOffset : state.PendingScrollX)
            : 0d;

    private static double EffectiveScrollY(SurfaceState state)
        => state.ScrollViewer is { } viewer
            ? (double.IsNaN(state.PendingScrollY) ? viewer.VerticalOffset : state.PendingScrollY)
            : 0d;

    // ChangeView 落地后清掉目标；两轴独立，按 0.5 的容差判断。
    private static void TryClearPendingScroll(SurfaceState state)
    {
        if (state.ScrollViewer is not { } viewer)
        {
            state.PendingScrollX = double.NaN;
            state.PendingScrollY = double.NaN;
            return;
        }

        var maxH = GetHorizontalScrollMaximum(viewer);
        var maxV = GetVerticalScrollMaximum(viewer);

        // 到达目标，或目标已经落在当前可滚范围外（ScrollViewer 会夹取，永远到不了）——两种情况都要清掉，
        // 否则补偿会一直挂在一个到不了的位置上，画面与实际滚动永久错开。
        if (!double.IsNaN(state.PendingScrollX)
            && (Math.Abs(viewer.HorizontalOffset - state.PendingScrollX) <= 0.5
                || state.PendingScrollX < -0.5
                || state.PendingScrollX > maxH + 0.5))
        {
            state.PendingScrollX = double.NaN;
        }

        if (!double.IsNaN(state.PendingScrollY)
            && (Math.Abs(viewer.VerticalOffset - state.PendingScrollY) <= 0.5
                || state.PendingScrollY < -0.5
                || state.PendingScrollY > maxV + 0.5))
        {
            state.PendingScrollY = double.NaN;
        }

        if (double.IsNaN(state.PendingScrollX) && double.IsNaN(state.PendingScrollY))
        {
            state.PendingScrollTimer?.Stop();
        }
    }

    // 所有滚动写入的唯一入口。先记目标、让 ApplyLayout 把内容按「目标位置」摆好，再交 ChangeView；
    // 这样 ChangeView 异步落地的那一两帧里画面已经停在目标处，落地只是把补偿收回，不会再跳一次。
    // 别的控件（小地图）也要走同一个写入口：否则它的 ChangeView 会绕过在途补偿，留在途目标把画面钉在旧位置。
    internal static bool TryRequestScroll(DependencyObject source, double x, double y)
    {
        var host = EnumerateVisualAncestors(source).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host?.GetValue(StateProperty) is not SurfaceState state)
        {
            return false;
        }

        RequestScroll(state, x, y);
        return true;
    }

    // 在途目标的兜底寿命：ChangeView 正常落地时由 TryClearPendingScroll 立即清；这里只处理
    // 「被夹取/被忽略/用户途中拖动滚动条」这几种不会自己到达目标的情况。
    private static void ArmPendingScrollTimer(SurfaceState state)
    {
        if (state.ScrollViewer is not { } viewer)
        {
            return;
        }

        if (state.PendingScrollTimer is null)
        {
            var timer = viewer.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(150);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                if (state.Host is not { } host
                    || (double.IsNaN(state.PendingScrollX) && double.IsNaN(state.PendingScrollY)))
                {
                    return;
                }

                state.PendingScrollX = double.NaN;
                state.PendingScrollY = double.NaN;
                Refresh(host);
            };
            state.PendingScrollTimer = timer;
        }

        state.PendingScrollTimer.Stop();
        state.PendingScrollTimer.Start();
    }

    private static void RequestScroll(SurfaceState state, double? x, double? y)
    {
        if (state.ScrollViewer is not { } viewer)
        {
            return;
        }

        if (x is { } targetX) state.PendingScrollX = targetX;
        if (y is { } targetY) state.PendingScrollY = targetY;

        if (state.Host is { } host && host.DataContext is IWorkflowTreeViewModel)
        {
            ApplyLayout(host, state);
        }

        viewer.ChangeView(x, y, null, disableAnimation: true);
        ArmPendingScrollTimer(state);
    }

    private static void ApplyLayout(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
        {
            return;
        }

        var translateX = viewModel.Layout.ActualOffset.Horizontal;
        var translateY = viewModel.Layout.ActualOffset.Vertical;

        // 在途滚动：内容当帧就按目标位置摆放，抵消 ChangeView 落地前的时差（负向扩画布时 ActualOffset 也变了）。
        if (state.ScrollViewer is { } pendingViewer)
        {
            if (!double.IsNaN(state.PendingScrollX))
            {
                translateX -= state.PendingScrollX - pendingViewer.HorizontalOffset;
            }

            if (!double.IsNaN(state.PendingScrollY))
            {
                translateY -= state.PendingScrollY - pendingViewer.VerticalOffset;
            }
        }

        var transform = new TranslateTransform
        {
            X = translateX,
            Y = translateY
        };

        // WinUI 子元素（不同于 WPF）不把 RenderTransform 绑到 CanvasTransformBehavior.Transform；直接经合成变换把偏移应用到画布（布局感知的做法）。
        state.Canvas.Translation = new System.Numerics.Vector3(
            (float)translateX,
            (float)translateY,
            0f);

        WorkflowCanvasTransformBehavior.Apply(host, transform);

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
    }

    private static void UpdateVisibleRegion(UserControl host, SurfaceState state)
    {
        if (state.IsVisibleRegionUpdateQueued)
        {
            return;
        }

        state.IsVisibleRegionUpdateQueued = true;

        var accepted = host.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            state.IsVisibleRegionUpdateQueued = false;

            if (!GetIsEnabled(host)
                || host.GetValue(StateProperty) is not SurfaceState currentState
                || !ReferenceEquals(currentState, state))
            {
                return;
            }

            if (ApplyPendingViewportRestore(state))
            {
                return;
            }

            ApplyVisibleRegion(host, state);
        });

        // 入队被拒时旗标要立刻放回 —— 它是在回调里清的，而回调只有被接受才会跑。否则这次拒绝会变成永久：
        // 可见区域从此再也不重算，画布停在旧的一批视图上（或干脆空着）。
        if (!accepted)
        {
            state.IsVisibleRegionUpdateQueued = false;
            System.Diagnostics.Debug.WriteLine(
                "WorkflowSurfaceBehavior: the dispatcher refused the visible-region update; the canvas keeps the views it has.");
        }
    }

    private static void ApplyVisibleRegion(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.ScrollViewer is null)
        {
            return;
        }

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
        // 有在途滚动时按目标位置算可见区：内容当帧已经在目标处（见 ApplyLayout 的补偿）。
        var viewportX = WorkflowSurfaceMath.ToWorld(EffectiveScrollX(state), viewModel.Layout.ActualOffset.Horizontal);
        var viewportY = WorkflowSurfaceMath.ToWorld(EffectiveScrollY(state), viewModel.Layout.ActualOffset.Vertical);
        viewModel.GetHelper().Viewport = new Viewport(
            viewportX, viewportY,
            state.ScrollViewer.ViewportWidth,
            state.ScrollViewer.ViewportHeight);

        // 持久化视口位置，使其能熬过序列化往返。
        viewModel.Layout.ViewportOffset = new Offset(viewportX, viewportY);
    }

    /// <summary>Re-run viewport virtualization against a committed scroll offset after a zoom. The
    /// helper otherwise virtualizes on its ~10fps dirty timer / the Low-priority viewport queue, so
    /// pooled node/link views lag the freshly collapsed anchors; <see cref="Virtualize"/> has no
    /// equality short-circuit (always rebuilds; the spatial extension re-entrancy-guards itself), so
    /// this keeps VisibleItems in lock-step with the committed zoom even when the window is unchanged.</summary>
    private static void VirtualizeAtScroll(IWorkflowTreeViewModel viewModel,
        double scrollX, double scrollY, double viewportW, double viewportH)
    {
        var layout = viewModel.Layout;
        var helper = viewModel.GetHelper();
        helper.Viewport = new Viewport(
            WorkflowSurfaceMath.ToWorld(scrollX, layout.ActualOffset.Horizontal),
            WorkflowSurfaceMath.ToWorld(scrollY, layout.ActualOffset.Vertical),
            viewportW, viewportH);
        helper.Virtualize(helper.Viewport);
    }

    private static void UpdateGridDecorator(IWorkflowTreeViewModel viewModel, SurfaceState state)
    {
        if (state.GridDecorator is not IWorkflowGridDecorator decorator || state.ScrollViewer is null)
        {
            return;
        }

        decorator.ScrollOffsetX = EffectiveScrollX(state);
        decorator.ScrollOffsetY = EffectiveScrollY(state);
        decorator.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        decorator.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;

        // 让虚拟化可见区修正与装饰器的浮动标尺带保持一致，标尺下方的节点才不会提前一个标尺厚度被剔除。
        viewModel.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand);
    }

    private static void UpdateMinimapOverlay(IWorkflowTreeViewModel viewModel, SurfaceState state)
    {
        if (state.MinimapOverlay is not IWorkflowMinimapOverlay minimap || state.ScrollViewer is null)
        {
            return;
        }

        minimap.ScrollOffsetX = EffectiveScrollX(state);
        minimap.ScrollOffsetY = EffectiveScrollY(state);
        minimap.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        minimap.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
        // 推实际可见区（ScrollViewer 的渲染尺寸）。窗口缩小时 ViewportWidth 可能报有效/更大的值；缩略图的可拖块必须跟踪真实屏幕区域（与缩略图自己的 OnScrollViewerResized 一致，后者用布局趟后落定的 ActualWidth）。
        minimap.ViewportWidth = Math.Max(0, state.ScrollViewer.ActualWidth);
        minimap.ViewportHeight = Math.Max(0, state.ScrollViewer.ActualHeight);
        minimap.WorkflowTree = viewModel;
    }

    private static double GetHorizontalScrollMaximum(ScrollViewer scrollViewer)
        => WorkflowSurfaceMath.ScrollMax(scrollViewer.ExtentWidth, scrollViewer.ViewportWidth);

    private static double GetVerticalScrollMaximum(ScrollViewer scrollViewer)
        => WorkflowSurfaceMath.ScrollMax(scrollViewer.ExtentHeight, scrollViewer.ViewportHeight);

    private static bool ShouldStartPan(PointerRoutedEventArgs e, SurfaceState state)
    {
        var properties = e.GetCurrentPoint(state.ScrollViewer!).Properties;
        return (properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed)
            && IsSurfaceBlankInteraction(e.OriginalSource as DependencyObject, state);
    }

    private static bool IsPanStillActive(UserControl host, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(host).Properties;
        return properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed;
    }

    private static bool IsSurfaceBlankInteraction(DependencyObject? source, SurfaceState state)
    {
        if (source is null)
        {
            return false;
        }

        if (IsWorkflowNodeOrSlotVisual(source))
        {
            return false;
        }

        var ancestors = EnumerateVisualAncestors(source).ToArray();
        if (ancestors.Any(IsWorkflowNodeOrSlotVisual))
        {
            return false;
        }

        if (IsWorkflowLinkVisual(source) || ancestors.Any(IsWorkflowLinkVisual))
        {
            return true;
        }

        return source == state.Canvas
            || source == state.ScrollViewer
            || source == state.PointerPressSource
            || source == state.GridDecorator
            || ancestors.Any(x => x == state.Canvas
                || x == state.ScrollViewer
                || x == state.PointerPressSource
                || x == state.GridDecorator
                || string.Equals(x.GetType().Name, "ScrollContentPresenter", StringComparison.Ordinal));
    }

    private static bool IsWorkflowNodeOrSlotVisual(DependencyObject source)
        => source is FrameworkElement { DataContext: IWorkflowNodeViewModel or IWorkflowSlotViewModel };

    // 只为「空白处平移」判定做连线识别：指针下是哪条连线由共享命中判定（LinkHitTestEx）决定。这里 DataContext 就够 —— 连线视图的内容会继承它；原先把类名当兜底是多余的，还让这条路径变成字符串类型化。
    private static bool IsWorkflowLinkVisual(DependencyObject source)
        => source is FrameworkElement { DataContext: IWorkflowLinkViewModel };

    // Whether a press actually happened over the canvas's own scroll container — the side bar's buttons
    // and lists are siblings of it, not descendants, and must keep their own clicks and focus.
    private static bool IsWithinScrollViewer(DependencyObject? source, ScrollViewer viewer)
    {
        if (source is null) return false;
        if (ReferenceEquals(source, viewer)) return true;

        foreach (var ancestor in EnumerateVisualAncestors(source))
        {
            if (ReferenceEquals(ancestor, viewer)) return true;
        }

        return false;
    }

    private static IEnumerable<DependencyObject> EnumerateVisualAncestors(DependencyObject source)
    {
        var current = VisualTreeHelper.GetParent(source);
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }

    }
