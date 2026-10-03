using System.ComponentModel;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSurfaceBehavior
{
    private sealed class SurfaceState
    {
        public ContentView? Host { get; set; }
        public ScrollView? ScrollViewer { get; set; }
        public AbsoluteLayout? Canvas { get; set; }
        public View? GridDecorator { get; set; }
        public View? MinimapOverlay { get; set; }
        public View? PointerPressSource { get; set; }
        public PanGestureRecognizer? PanGesture { get; set; }
        public PinchGestureRecognizer? ZoomGesture { get; set; }
        public double ZoomStartScale { get; set; }
#if WINDOWS
        public Microsoft.UI.Xaml.Input.PointerEventHandler? ZoomWheelHandler { get; set; }
#endif
        public INotifyPropertyChanged? LayoutNotifier { get; set; }
        public PropertyChangedEventHandler? LayoutChangedHandler { get; set; }
        /// <summary>Anchor scroll offset of the current pan gesture — the scroll position the
        /// gesture began at (or the last edge/node-drag re-anchor). Each Running computes the
        /// target ABSOLUTELY as anchor − (Total − anchorTotal), the same math WPF and the minimap
        /// use. No per-delta accumulation: a clamped/rounded ScrollToAsync can't accumulate
        /// bookkeeping drift, which is what jittered during drag and jumped at release.</summary>
        public double PanAccumulatedX { get; set; }
        /// <summary>Anchor scroll offset of the current pan gesture (vertical).</summary>
        public double PanAccumulatedY { get; set; }
        /// <summary>Gesture TotalX at the pan anchor. The pointer's current distance from the
        /// anchor is (TotalX − PanAnchorTotalX); the scroll target is anchor − that distance.</summary>
        public double PanAnchorTotalX { get; set; }
        /// <summary>Gesture TotalY at the pan anchor (vertical).</summary>
        public double PanAnchorTotalY { get; set; }
        public bool HasPendingScrollRestore { get; set; }
        // 上一棵被挂上来的树（引用比较）。恢复只因「换了树」触发一次，之后的 Refresh 不再把用户滚回去。
        public IWorkflowTreeViewModel? LastRestoreTree { get; set; }
        public bool IsRefreshing { get; set; }
        public bool IsVisibleRegionUpdateQueued { get; set; }
        public double PendingViewportX { get; set; }
        public double PendingViewportY { get; set; }

        /// <summary>Cancels the previous in-flight ScrollToAsync on each new pan delta,
        /// preventing stack-up of outdated scroll operations.</summary>
        public CancellationTokenSource? PanCts { get; set; }

        /// <summary>True while a pan gesture is active (Started through Completed/Canceled).
        /// No longer gates OnScrolled or the decorator writers — those are always active now so
        /// grid + content track the native offset together. Kept for the diagnostic trace.</summary>
        public bool PanGestureActive { get; set; }

        /// <summary>True while the Windows pointer-driven pan owns the pointer (see
        /// <c>OnPlatformPanPressed</c>).</summary>
        public bool PointerPanActive { get; set; }

        /// <summary>Pointer position at the pan anchor, in the press source's coordinate space.
        /// The pointer's distance from the anchor is the same quantity the gesture's TotalX/TotalY
        /// carried.</summary>
        public double PointerAnchorX { get; set; }
        public double PointerAnchorY { get; set; }

        // 连线右键菜单：菜单由模板声明（条目归用户，见 LinkMenuKeyProperty），订阅、定位、弹出、开合上报都在这里。
        public MenuFlyout? LinkMenu { get; set; }
        public IWorkflowLinkViewModel? MenuLink { get; set; }
        public Anchor MenuPosition { get; set; } = new();
        public LinkInteraction? MenuHub { get; set; }
        public EventHandler<ContextMenuRequestedEventArgs>? MenuRequested { get; set; }
        public EventHandler<ContextMenuDismissRequestedEventArgs>? MenuDismissed { get; set; }

#if WINDOWS
        /// <summary>The press source's platform element with the native pointer handlers attached.</summary>
        public Microsoft.UI.Xaml.UIElement? PlatformPressSource { get; set; }

        // Windows 上正在弹的原生 flyout；hub 收不了它，由这里 Hide，并在它的 Closed 里清掉。
        public Microsoft.UI.Xaml.Controls.MenuFlyout? OpenFlyout { get; set; }
#else
        // 非 Windows 没有点弹出物：条目物化进这个由适配器自建的浮层（模板不再携带它）。
        public Grid? LinkMenuLayer { get; set; }
        public Grid? LinkMenuScrim { get; set; }
        public Border? LinkMenuHost { get; set; }
        public VerticalStackLayout? LinkMenuItems { get; set; }
        public EventHandler<TappedEventArgs>? MenuScrimTapped { get; set; }
#endif
    }

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSurfaceBehavior),
        false,
        propertyChanged: OnIsEnabledChanged);

    public static readonly BindableProperty ScrollViewerNameProperty = BindableProperty.CreateAttached(
        "ScrollViewerName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty CanvasNameProperty = BindableProperty.CreateAttached(
        "CanvasName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty GridDecoratorNameProperty = BindableProperty.CreateAttached(
        "GridDecoratorName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty PointerPressSourceNameProperty = BindableProperty.CreateAttached(
        "PointerPressSourceName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty MinimapOverlayNameProperty = BindableProperty.CreateAttached(
        "MinimapOverlayName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty ZoomEnabledProperty = BindableProperty.CreateAttached(
        "ZoomEnabled",
        typeof(bool),
        typeof(WorkflowSurfaceBehavior),
        false,
        propertyChanged: OnZoomEnabledChanged);

    /// <summary>
    /// Resource key of the context menu a right press on a link opens. The entries are the user's — declare a
    /// <see cref="MenuFlyout"/> resource under that key, put its items in it, and name the key here; the surface
    /// resolves the menu by key, feeds the pressed link to each item, positions it, opens it, and reports
    /// open/close to the interaction hub.
    /// <para>
    /// A key rather than the menu itself: this property sits on the surface's own root element, and a
    /// <c>{StaticResource}</c> there would be resolved before the very resource dictionary that defines it.
    /// </para>
    /// </summary>
    public static readonly BindableProperty LinkMenuKeyProperty = BindableProperty.CreateAttached(
        "LinkMenuKey",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    private static readonly BindableProperty StateProperty = BindableProperty.CreateAttached(
        "State",
        typeof(SurfaceState),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static bool GetIsEnabled(BindableObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetZoomEnabled(BindableObject element) => (bool)element.GetValue(ZoomEnabledProperty);
    public static void SetZoomEnabled(BindableObject element, bool value) => element.SetValue(ZoomEnabledProperty, value);
    public static string? GetScrollViewerName(BindableObject element) => (string?)element.GetValue(ScrollViewerNameProperty);
    public static void SetScrollViewerName(BindableObject element, string? value) => element.SetValue(ScrollViewerNameProperty, value);
    public static string? GetCanvasName(BindableObject element) => (string?)element.GetValue(CanvasNameProperty);
    public static void SetCanvasName(BindableObject element, string? value) => element.SetValue(CanvasNameProperty, value);
    public static string? GetGridDecoratorName(BindableObject element) => (string?)element.GetValue(GridDecoratorNameProperty);
    public static void SetGridDecoratorName(BindableObject element, string? value) => element.SetValue(GridDecoratorNameProperty, value);
    public static string? GetPointerPressSourceName(BindableObject element) => (string?)element.GetValue(PointerPressSourceNameProperty);
    public static void SetPointerPressSourceName(BindableObject element, string? value) => element.SetValue(PointerPressSourceNameProperty, value);
    public static string? GetMinimapOverlayName(BindableObject element) => (string?)element.GetValue(MinimapOverlayNameProperty);
    public static void SetMinimapOverlayName(BindableObject element, string? value) => element.SetValue(MinimapOverlayNameProperty, value);

    /// <summary>Gets the resource key of the link context menu declared for <paramref name="element"/>.</summary>
    public static string? GetLinkMenuKey(BindableObject element) => (string?)element.GetValue(LinkMenuKeyProperty);

    /// <summary>Sets the resource key of the link context menu declared for <paramref name="element"/>.</summary>
    public static void SetLinkMenuKey(BindableObject element, string? value) => element.SetValue(LinkMenuKeyProperty, value);

    public static void Refresh(ContentView host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!GetIsEnabled(host))
        {
            return;
        }

        var state = (SurfaceState?)host.GetValue(StateProperty);
        if (state is null)
        {
            return;
        }

        WireLinkMenu(host, state);

        // Re-entrancy guard: prevent cascading Refresh cycles when canvas expansion
        // during ApplyLayout triggers Scrolled/SizeChanged which call Refresh again.
        // Without this guard, each canvas expansion cascades 2-3 Refresh calls,
        // compounding into a positive-feedback slowdown spiral.
        if (state.IsRefreshing)
        {
            return;
        }

        state.IsRefreshing = true;
        try
        {
            ApplyLayout(host, state);
            UpdateVisibleRegion(host, state);
            ApplyPendingScrollRestore(host, state);
        }
        finally
        {
            state.IsRefreshing = false;
        }
    }

    // 连线右键菜单：**条目由模板声明**（挂在 LinkMenuKey 上），**接线在这里** —— 订中枢、定位、弹出、
    // 把开合报回去，模板因此没有一行交互代码。菜单指着的那条线离树时中枢发 DismissRequested，这里收自己那份。
    // MAUI 的 MenuFlyout 是资源、不是可视物，也没有 Opened/Closed，所以开合由两条呈现路径各自上报。
    private static void WireLinkMenu(ContentView host, SurfaceState state)
    {
        // 资源在 attach 之后才一定就绪（第一次 Refresh 可能早于 Resources 解析完），所以每次 Refresh 都重查一次。
        var key = GetLinkMenuKey(host);
        state.LinkMenu = key is { Length: > 0 } ? FindLinkMenuResource(host, key) : null;

        var hub = ResolveTreeViewModel(host, state) is { } tree ? LinkInteraction.For(tree) : null;
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
#if WINDOWS
            // 隐藏原生 flyout；它的 Closed 处理器照常把 Closed 报回 hub，挂起随之放开。
            state.OpenFlyout?.Hide();
#else
            DismissLinkMenu(state);
#endif
        };
        hub.ContextMenuRequested += state.MenuRequested;
        hub.ContextMenuDismissRequested += state.MenuDismissed;
    }

    private static void UnwireLinkMenu(SurfaceState state)
    {
        if (state.MenuHub is not null)
        {
            if (state.MenuRequested is not null) state.MenuHub.ContextMenuRequested -= state.MenuRequested;
            if (state.MenuDismissed is not null) state.MenuHub.ContextMenuDismissRequested -= state.MenuDismissed;
            state.MenuHub = null;
        }

        state.MenuRequested = null;
        state.MenuDismissed = null;
        state.LinkMenu = null;

#if WINDOWS
        // 先摘 hub 再收弹窗：Closed 处理器里 state.MenuHub 已为空，不会往 hub 补发一发。
        state.OpenFlyout?.Hide();
        state.OpenFlyout = null;
#else
        // 浮层是适配器加的，摘挂时连同 Tap 手势一起从宿主布局里移走，免得重复 attach 叠层。
        if (state.LinkMenuLayer is { } layer)
        {
            layer.IsVisible = false;
            if (layer.Parent is Layout layout) layout.Children.Remove(layer);
        }

        state.LinkMenuLayer = null;
        state.LinkMenuScrim = null;
        state.LinkMenuHost = null;
        state.LinkMenuItems = null;
        state.MenuScrimTapped = null;
#endif

        state.MenuLink = null;
    }

    // 条目自带 Command 就用它（绑定上下文是那条连线，弹出前设好）；没有就落到默认动作：删掉这条连线。
    private static void RunItem(MenuFlyoutItem item, IWorkflowLinkViewModel link)
    {
        if (item.Command is { } command)
        {
            var parameter = item.CommandParameter ?? link;
            if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }

            return;
        }

        if (link.DeleteCommand.CanExecute(null))
        {
            link.DeleteCommand.Execute(null);
        }
    }

    // 资源先查宿主自己的字典，再沿父链往上，最后落到 Application（与 ViewManager 查 DataTemplate 同一条路）。
    private static MenuFlyout? FindLinkMenuResource(Element host, string key)
    {
        for (Element? current = host; current is not null; current = current.Parent)
        {
            if (current is VisualElement visualElement
                && visualElement.Resources.TryGetValue(key, out var resource)
                && resource is MenuFlyout menu)
            {
                return menu;
            }
        }

        return Application.Current?.Resources.TryGetValue(key, out var appResource) == true
            && appResource is MenuFlyout appMenu
                ? appMenu
                : null;
    }

#if WINDOWS
    // 菜单条目在 LinkMenuKey 指向的资源里声明；这里只管定位与弹出。
    // 画布坐标 → 视口像素：px = Ruler + 锚点 + 内容偏移 − 滚动偏移（与链接层绘制/命中共用同一条换算）。
    private static void ShowLinkMenu(ContentView host, SurfaceState state, ContextMenuRequestedEventArgs e)
    {
        // 空白画布没有可操作的对象，不给菜单。
        var menu = state.LinkMenu;
        var gridDecorator = state.GridDecorator;
        if (e.Link is null || menu is null || gridDecorator is null) return;
        if (ResolveTreeViewModel(host, state) is null) return;
        if (gridDecorator is not IWorkflowGridDecorator decorator) return;
        if (gridDecorator.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement hostElement) return;

        state.MenuLink = e.Link;
        state.MenuPosition = e.Position;

        var flyout = BuildPlatformMenu(menu, e.Link);
        state.OpenFlyout = flyout;

        // 开合报回 hub：菜单开着时指针飞到菜单上，也不该清掉这次选中的连线。
        flyout.Closed += (_, _) =>
        {
            state.OpenFlyout = null;
            state.MenuLink = null;
            state.MenuHub?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, state.MenuPosition, e.Link));
        };
        state.MenuHub?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, state.MenuPosition, e.Link));

        var ruler = Math.Max(0d, decorator.RulerBand);
        var x = ruler + e.Position.Horizontal + decorator.ContentOffsetX - decorator.ScrollOffsetX;
        var y = ruler + e.Position.Vertical + decorator.ContentOffsetY - decorator.ScrollOffsetY;
        flyout.ShowAt(hostElement, new Windows.Foundation.Point(x, y));
    }

    // MAUI 没有能在指定点弹出的跨平台菜单；只有 Windows 的原生 MenuFlyout 能做到，所以把声明的条目翻成它。
    private static Microsoft.UI.Xaml.Controls.MenuFlyout BuildPlatformMenu(MenuFlyout declared, IWorkflowLinkViewModel link)
    {
        var flyout = new Microsoft.UI.Xaml.Controls.MenuFlyout();
        foreach (var element in declared)
        {
            switch (element)
            {
                // MenuFlyoutSeparator derives from MenuFlyoutItem, so it must be matched first.
                case MenuFlyoutSeparator:
                    flyout.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());
                    break;

                case MenuFlyoutItem item:
                    // 平台条目没有 DataContext，逐条把这条连线喂给它，条目里的绑定才解析得到。
                    item.BindingContext = link;
                    var native = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
                    {
                        Text = item.Text,
                        IsEnabled = item.IsEnabled,
                    };
                    native.Click += (_, _) => RunItem(item, link);
                    flyout.Items.Add(native);
                    break;
            }
        }

        return flyout;
    }
#else
    // 非 Windows 没有能在指定点弹出的跨平台菜单，所以把声明的条目物化进适配器自建的浮层，
    // 落在长按处。长按本身由链接层翻译成右键交给 hub；这里只负责呈现。
    private static void ShowLinkMenu(ContentView host, SurfaceState state, ContextMenuRequestedEventArgs e)
    {
        // 空白画布没有可操作的对象，不给菜单。
        var menu = state.LinkMenu;
        var gridDecorator = state.GridDecorator;
        if (e.Link is null || menu is null || gridDecorator is null) return;
        if (ResolveTreeViewModel(host, state) is null) return;
        if (gridDecorator is not IWorkflowGridDecorator decorator) return;

        EnsureLinkMenuLayer(state);
        var layer = state.LinkMenuLayer;
        var menuHost = state.LinkMenuHost;
        var items = state.LinkMenuItems;
        if (layer is null || menuHost is null || items is null) return;

        state.MenuLink = e.Link;
        state.MenuPosition = e.Position;

        items.Children.Clear();
        foreach (var element in menu)
        {
            switch (element)
            {
                case MenuFlyoutSeparator:
                    items.Children.Add(new BoxView
                    {
                        HeightRequest = 1,
                        Color = Color.FromArgb("#40FFFFFF"),
                        Margin = new Thickness(6, 2),
                    });
                    break;

                case MenuFlyoutItem item:
                    item.BindingContext = e.Link;
                    var button = new Button
                    {
                        Text = item.Text,
                        IsEnabled = item.IsEnabled,
                        BackgroundColor = Colors.Transparent,
                        TextColor = Colors.White,
                        HeightRequest = 36,
                        Padding = new Thickness(12, 0),
                        HorizontalOptions = LayoutOptions.Fill,
                    };
                    var captured = item;
                    button.Clicked += (_, _) => SelectMenuItem(state, captured);
                    items.Children.Add(button);
                    break;
            }
        }

        var ruler = Math.Max(0d, decorator.RulerBand);
        var x = ruler + e.Position.Horizontal + decorator.ContentOffsetX - decorator.ScrollOffsetX;
        var y = ruler + e.Position.Vertical + decorator.ContentOffsetY - decorator.ScrollOffsetY;

        menuHost.Margin = new Thickness(Math.Max(0d, x), Math.Max(0d, y), 0, 0);
        layer.IsVisible = true;
        state.MenuHub?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, e.Position, e.Link));
    }

    // 条目被点：先收起（收起会报 Closed），再执行条目自己的动作。
    private static void SelectMenuItem(SurfaceState state, MenuFlyoutItem item)
    {
        var link = state.MenuLink;
        DismissLinkMenu(state);
        if (link is not null)
        {
            RunItem(item, link);
        }
    }

    private static void DismissLinkMenu(SurfaceState state)
    {
        if (state.LinkMenuLayer is not { IsVisible: true } layer) return;

        layer.IsVisible = false;
        var link = state.MenuLink;
        state.MenuLink = null;
        if (link is not null)
        {
            state.MenuHub?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, new Anchor(), link));
        }
    }

    // 浮层是呈现，归适配器：条目物化在这里，模板不再携带这层标记。挂到装饰器**外**最近的一层 Layout 上
    // （装饰器自己也是 Grid，从它起步会把浮层塞进装饰器、压在它 ZIndex=10 的标尺下面），
    // 与装饰器共用原点，菜单的 Margin 才和算出来的画布坐标对得上。
    private static void EnsureLinkMenuLayer(SurfaceState state)
    {
        if (state.LinkMenuLayer is not null) return;

        Layout? parent = null;
        for (Element? current = state.GridDecorator?.Parent; current is not null; current = current.Parent)
        {
            if (current is Layout layout)
            {
                parent = layout;
                break;
            }
        }

        if (parent is null) return;

        var items = new VerticalStackLayout { Spacing = 2 };
        var menuHost = new Border
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Padding = new Thickness(4),
            BackgroundColor = Color.FromArgb("#F22B2B2B"),
            Stroke = Color.FromArgb("#40FFFFFF"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6) },
            Content = items,
        };

        var scrim = new Grid { BackgroundColor = Colors.Transparent };
        var tapped = (EventHandler<TappedEventArgs>)((_, _) => DismissLinkMenu(state));
        var tap = new TapGestureRecognizer();
        tap.Tapped += tapped;
        scrim.GestureRecognizers.Add(tap);

        var layer = new Grid { IsVisible = false };
        layer.Children.Add(scrim);
        layer.Children.Add(menuHost);
        parent.Children.Add(layer);

        state.LinkMenuLayer = layer;
        state.LinkMenuScrim = scrim;
        state.LinkMenuHost = menuHost;
        state.LinkMenuItems = items;
        state.MenuScrimTapped = tapped;
    }
#endif

    // 树刚被挂上来且不是上一棵：把它存档里的视口位置排进待恢复。
    // 必须在紧随其后的 Refresh 之前调用 —— 那次 UpdateVisibleRegion 会拿控件当前（还没滚过去的）位置
    // 把 ViewportOffset 覆盖掉，存档位置就此消失。
    // 传进来的是世界坐标，与 RequestViewportRestore 同一个约定。
    private static void CaptureViewportRestore(ContentView control, SurfaceState state)
    {
        if (ResolveTreeViewModel(control, state) is not { } viewModel) return;
        if (ReferenceEquals(viewModel, state.LastRestoreTree)) return;

        state.LastRestoreTree = viewModel;

        if (!WorkflowSurfaceMath.HasViewportRestore(viewModel.Layout)) return;

        state.PendingViewportX = viewModel.Layout.ViewportOffset.Horizontal;
        state.PendingViewportY = viewModel.Layout.ViewportOffset.Vertical;
        state.HasPendingScrollRestore = true;
    }

    public static void RequestViewportRestore(ContentView host, double viewportX, double viewportY)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!GetIsEnabled(host))
        {
            return;
        }

        var state = (SurfaceState?)host.GetValue(StateProperty);
        if (state is null)
        {
            return;
        }

        state.PendingViewportX = viewportX;
        state.PendingViewportY = viewportY;
        state.HasPendingScrollRestore = true;
        Refresh(host);
    }

    internal static bool TryGetViewport(ContentView host, out double viewportX, out double viewportY)
    {
        viewportX = 0;
        viewportY = 0;

        if (!GetIsEnabled(host)
            || host.GetValue(StateProperty) is not SurfaceState state
            || state.ScrollViewer is null
            || ResolveTreeViewModel(host, state) is not { } viewModel)
        {
            return false;
        }

        viewportX = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.ScrollX, viewModel.Layout.ActualOffset.Horizontal);
        viewportY = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.ScrollY, viewModel.Layout.ActualOffset.Vertical);
        return true;
    }

    private static void OnIsEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not ContentView control)
        {
            return;
        }

        if (newValue is true)
        {
            Attach(control);
            return;
        }

        Detach(control);
    }

    private static void Attach(ContentView control)
    {
        Detach(control);

        var state = new SurfaceState
        {
            Host = control,
        };
        control.SetValue(StateProperty, state);
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.BindingContextChanged += OnBindingContextChanged;
        control.SizeChanged += OnHostSizeChanged;
        ResolveNamedControls(control, state);
        UpdateLayoutSubscription(control, state);
        CaptureViewportRestore(control, state);
        Refresh(control);
    }

    private static void Detach(ContentView control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.BindingContextChanged -= OnBindingContextChanged;
        control.SizeChanged -= OnHostSizeChanged;

        if (control.GetValue(StateProperty) is SurfaceState state)
        {
            UnsubscribeResolvedControls(state);
            UnsubscribeLayout(state);
            UnwireLinkMenu(state);
            state.Host = null;
        }

        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is ContentView control && control.GetValue(StateProperty) is SurfaceState state)
        {
            ResolveNamedControls(control, state);
            UpdateLayoutSubscription(control, state);
            CaptureViewportRestore(control, state);
            Refresh(control);
        }
    }

    private static void OnUnloaded(object? sender, EventArgs e)
    {
        if (sender is ContentView control && control.GetValue(StateProperty) is SurfaceState state)
        {
            UnsubscribeLayout(state);
        }
    }

    private static void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is not ContentView control || control.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        UpdateLayoutSubscription(control, state);
        CaptureViewportRestore(control, state);
        Refresh(control);
    }

    private static void OnHostSizeChanged(object? sender, EventArgs e)
    {
        if (sender is ContentView control)
        {
            Refresh(control);
        }
    }

    private static void ResolveNamedControls(ContentView control, SurfaceState state)
    {
        UnsubscribeResolvedControls(state);

        var scrollViewerName = GetScrollViewerName(control);
        var canvasName = GetCanvasName(control);
        var gridDecoratorName = GetGridDecoratorName(control);
        var pointerPressSourceName = GetPointerPressSourceName(control);

        if (!string.IsNullOrWhiteSpace(scrollViewerName))
        {
            state.ScrollViewer = control.FindByName<ScrollView>(scrollViewerName);
        }

        if (!string.IsNullOrWhiteSpace(canvasName))
        {
            state.Canvas = control.FindByName<AbsoluteLayout>(canvasName);
            if (state.Canvas is not null)
            {
                state.Canvas.ChildAdded += OnCanvasChildAdded;
                state.Canvas.ChildRemoved += OnCanvasChildRemoved;
            }
        }

        if (!string.IsNullOrWhiteSpace(gridDecoratorName))
        {
            state.GridDecorator = control.FindByName<View>(gridDecoratorName);
        }

        var minimapOverlayName = GetMinimapOverlayName(control);
        if (!string.IsNullOrWhiteSpace(minimapOverlayName))
        {
            state.MinimapOverlay = control.FindByName<View>(minimapOverlayName);
        }

        if (!string.IsNullOrWhiteSpace(pointerPressSourceName))
        {
            state.PointerPressSource = control.FindByName<View>(pointerPressSourceName);
            if (state.PointerPressSource is not null)
            {
#if WINDOWS
                // 平移走原生指针事件，不用 PanGestureRecognizer：只要还在用 manipulation，
                // 原生 ScrollViewer 就会在松手那一刻把它自己累积的偏移补上（实测 423→186，
                // 见 OnScrollViewerHandlerChanged）。指针事件不产生 manipulation，它便无从插手。
                PlatformPanHosts[state.PointerPressSource] = state;
                state.PointerPressSource.HandlerChanged += OnPointerPressSourceHandlerChanged;
                HookPlatformPan(state.PointerPressSource);
#else
                state.PanGesture = new PanGestureRecognizer();
                state.PanGesture.PanUpdated += OnPanUpdated;
                state.PointerPressSource.GestureRecognizers.Add(state.PanGesture);
#endif
            }
        }

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.Scrolled += OnScrolled;
            state.ScrollViewer.SizeChanged += OnScrollViewerSizeChanged;
            // Configure the native ScrollViewer once its platform view exists (the handler can
            // be created after this attachment runs). Re-runs if the handler is re-created.
            state.ScrollViewer.HandlerChanged += OnScrollViewerHandlerChanged;
            OnScrollViewerHandlerChanged(state.ScrollViewer, EventArgs.Empty);
        }

        if (GetZoomEnabled(control))
        {
            HookZoom(control, state);
        }
    }

    private static void UnsubscribeResolvedControls(SurfaceState state)
    {
        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.Scrolled -= OnScrolled;
            state.ScrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
            state.ScrollViewer.HandlerChanged -= OnScrollViewerHandlerChanged;
        }

        if (state.Canvas is not null)
        {
            state.Canvas.ChildAdded -= OnCanvasChildAdded;
            state.Canvas.ChildRemoved -= OnCanvasChildRemoved;
        }

        if (state.PointerPressSource is not null)
        {
#if WINDOWS
            state.PointerPressSource.HandlerChanged -= OnPointerPressSourceHandlerChanged;
            PlatformPanHosts.Remove(state.PointerPressSource);
            UnhookPlatformPan(state);
#else
            if (state.PanGesture is not null)
            {
                state.PanGesture.PanUpdated -= OnPanUpdated;
                state.PointerPressSource.GestureRecognizers.Remove(state.PanGesture);
            }
#endif
        }

        UnhookZoom(state);
        state.PanCts?.Cancel();
        state.PanCts = null;
        state.ScrollViewer = null;
        state.Canvas = null;
        state.GridDecorator = null;
        state.MinimapOverlay = null;
        state.PointerPressSource = null;
        state.PanGesture = null;
    }

    private static void OnZoomEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not ContentView control || control.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (Equals(newValue, true))
        {
            HookZoom(control, state);
        }
        else
        {
            UnhookZoom(state);
        }
    }

    // MAUI is cross-platform: touch zooms via a pinch gesture (works everywhere); on Windows,
    // Ctrl + mouse-wheel is also accepted. Both write Layout.Scale (Core collapses the nodes).
    private static void HookZoom(ContentView control, SurfaceState state)
    {
        if (state.PointerPressSource is not null && state.ZoomGesture is null)
        {
            state.ZoomGesture = new PinchGestureRecognizer();
            state.ZoomGesture.PinchUpdated += OnPinchUpdated;
            state.PointerPressSource.GestureRecognizers.Add(state.ZoomGesture);
        }

#if WINDOWS
        if (state.ZoomWheelHandler is null)
        {
            // Capture the MAUI host in the closure: the platform element has no DataContext, but the
            // MAUI ContentView's BindingContext is the workflow tree.
            var captured = control;
            state.ZoomWheelHandler = (s, ev) => OnZoomWheelChanged(s, ev, captured);
            if (control.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement el)
            {
                el.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent, state.ZoomWheelHandler, true);
            }
            else
            {
                control.HandlerChanged += OnZoomHandlerChanged;
            }
        }
#endif
    }

    private static void UnhookZoom(SurfaceState state)
    {
        if (state.PointerPressSource is not null && state.ZoomGesture is not null)
        {
            state.ZoomGesture.PinchUpdated -= OnPinchUpdated;
            state.PointerPressSource.GestureRecognizers.Remove(state.ZoomGesture);
        }

        state.ZoomGesture = null;

#if WINDOWS
        if (state.Host is not null && state.ZoomWheelHandler is not null)
        {
            state.Host.HandlerChanged -= OnZoomHandlerChanged;
            if (state.Host.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement el)
            {
                el.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent, state.ZoomWheelHandler);
            }
        }

        state.ZoomWheelHandler = null;
#endif
    }

#if WINDOWS
    private static void OnZoomHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not ContentView control || control.GetValue(StateProperty) is not SurfaceState state || state.ZoomWheelHandler is null)
        {
            return;
        }

        if (control.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement el)
        {
            el.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent, state.ZoomWheelHandler, true);
        }
    }

    private static void OnZoomWheelChanged(object? sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e, ContentView control)
    {
        if (control.GetValue(StateProperty) is not SurfaceState state
            || control.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement source
            || ResolveTreeViewModel(control, state) is not { } viewModel
            || !e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
        {
            return;
        }

        var delta = e.GetCurrentPoint(source).Properties.MouseWheelDelta;
        // Wheel up (positive delta) zooms in: Scale is a collapse factor, so zoom-in divides it by 1/1.1.
        var factor = delta > 0 ? 1 / 1.1 : 1.1;
        var next = Math.Max(0.1, Math.Min(10, viewModel.Layout.Scale.Horizontal * factor));
        var layout = viewModel.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter && state.ScrollViewer is { } sv)
        {
            // Capture the world point under the viewport center, collapse about it, then recenter the
            // scroll via the deferred async path (MAUI's native extent re-measures asynchronously).
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                sv.ScrollX, sv.ScrollY, sv.Width, sv.Height, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);
            RecenterOnWorldPointAsync(control, state, wx, wy);
        }
        else
        {
            layout.Scale = new Scale(next, next);
            // World-origin zoom keeps content aligned to the top-left, so a cover that grew to keep
            // negative-quadrant content reachable must be re-applied here — nothing downstream (no
            // PivotCenterScroll) would otherwise pick up the new ActualOffset.
            if (WorkflowSurfaceMath.EnsureNegativeCover(viewModel))
            {
                ApplyLayout(control, state);
                UpdateVisibleRegion(control, state);
            }
        }
        e.Handled = true;
    }
#endif

    /// <summary>
    /// Fire-and-forget recenter after a viewport-center zoom: sets the scroll so the world point
    /// <paramref name="worldX"/>/<paramref name="worldY"/> (the pivot the nodes just collapsed about)
    /// sits at the viewport center. Reads the native scroll and viewport AFTER yielding so MAUI's
    /// async layout has settled on the new scale/pivot.
    /// </summary>
    private static async void RecenterOnWorldPointAsync(ContentView host, SurfaceState state, double worldX, double worldY)
    {
        try
        {
            if (state.ScrollViewer is null)
            {
                return;
            }

            await Task.Yield();

            var viewModel = ResolveTreeViewModel(host, state);
            if (viewModel is null)
            {
                return;
            }

            // A deep zoom-in collapses negative-world content to w/Scale, and the fixed NegativeOffset
            // (== ActualOffset) stops covering it below some scale — content escapes the scrollable
            // region on the left/top and links truncate there. Grow the cover first (monotonic, no-op
            // for positive-only content) so the PivotCenterScroll below reads the NEW ActualOffset and
            // auto-shifts the recenter by exactly the growth.
            if (WorkflowSurfaceMath.EnsureNegativeCover(viewModel))
            {
                // The cover grew the model ActualOffset/ActualSize. Push the canvas element (translate +
                // WidthRequest) to the new values BEFORE the native scroll max below is read — otherwise
                // GetHorizontalScrollMaximum still reflects the pre-cover extent, the clamp lands short,
                // and the grown negative content stays out of reach (left/top truncation lags a notch).
                ApplyLayout(host, state);
                await Task.Yield(); // let MAUI's async native extent re-measure against the grown canvas
            }

            var svW = double.IsNaN(state.ScrollViewer.Width) ? 1 : state.ScrollViewer.Width;
            var svH = double.IsNaN(state.ScrollViewer.Height) ? 1 : state.ScrollViewer.Height;
            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(worldX, worldY, viewModel.Layout, svW, svH);
            // Overscroll-expand the canvas so the pivot is always reachable; a plain clamp would push
            // the pivot off-center and the next wheel tick re-captures the drift as jitter.
            _ = WorkflowSurfaceMath.ClampScrollOffset(tx, GetHorizontalScrollMaximum(state), viewModel.Layout, horizontal: true);
            _ = WorkflowSurfaceMath.ClampScrollOffset(ty, GetVerticalScrollMaximum(state), viewModel.Layout, horizontal: false);
            tx = WorkflowSurfaceMath.ClampValue(tx, 0, GetHorizontalScrollMaximum(state));
            ty = WorkflowSurfaceMath.ClampValue(ty, 0, GetVerticalScrollMaximum(state));

            if (!double.IsFinite(tx)) tx = 0;
            if (!double.IsFinite(ty)) ty = 0;

            if (Math.Abs(state.ScrollViewer.ScrollX - tx) > 0.5
                || Math.Abs(state.ScrollViewer.ScrollY - ty) > 0.5)
            {
                await state.ScrollViewer.ScrollToAsync(tx, ty, false);
            }

            UpdateVisibleRegion(host, state);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkflowSurfaceBehavior] RecenterAfterZoom error: {ex.Message}");
        }
    }

    private static void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (sender is not BindableObject bindable)
        {
            return;
        }

        var host = FindAncestorContentView(bindable);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        var tree = ResolveTreeViewModel(host, state);
        if (tree is null)
        {
            return;
        }

        switch (e.Status)
        {
            case GestureStatus.Started:
                state.ZoomStartScale = tree.Layout.Scale.Horizontal;
                break;
            case GestureStatus.Running:
                var factor = Math.Max(0.1, Math.Min(10, state.ZoomStartScale * e.Scale));
                var layout = tree.Layout;
                if (layout.ZoomCenter == ZoomCenter.ViewportCenter && state.ScrollViewer is { } sv)
                {
                    var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                        sv.ScrollX, sv.ScrollY, sv.Width, sv.Height, layout);
                    layout.CollapsePivot = new Anchor(wx, wy, 0);
                    layout.Scale = new Scale(factor, factor);
                    RecenterOnWorldPointAsync(host, state, wx, wy);
                }
                else
                {
                    layout.Scale = new Scale(factor, factor);
                    // Same world-origin re-apply as the wheel path: grow the cover when deep zoom-in
                    // collapses content past the fixed NegativeOffset, then push the new extent out.
                    if (WorkflowSurfaceMath.EnsureNegativeCover(tree))
                    {
                        ApplyLayout(host, state);
                        UpdateVisibleRegion(host, state);
                    }
                }
                break;
        }
    }

    private static void UpdateLayoutSubscription(ContentView control, SurfaceState state)
    {
        UnsubscribeLayout(state);

        var tree = ResolveTreeViewModel(control, state);
        if (tree is null || tree.Layout is not INotifyPropertyChanged notifier)
        {
            return;
        }

        state.LayoutNotifier = notifier;
        state.LayoutChangedHandler = (_, e) =>
        {
            // Only react to OriginSize changes which can happen outside of
            // scroll/pan (e.g. AdaptTo, programmatic resize).  During pan,
            // PositiveOffset/NegativeOffset change on every frame but those
            // are already handled by ApplyPanAsync calling ApplyLayout +
            // UpdateVisibleRegion directly.  Reacting to them here would
            // triple the work per pan frame (LayoutChangedHandler fires,
            // then OnScrolled fires from ScrollToAsync), causing cascading
            // slowdown.
            // ViewportOffset is also filtered to avoid the circular update
            // where ApplyVisibleRegion sets ViewportOffset which would
            // trigger another Refresh.
            if (e.PropertyName is nameof(CanvasLayout.OriginSize))
            {
                Refresh(control);
            }
        };
        notifier.PropertyChanged += state.LayoutChangedHandler;
    }

    private static void UnsubscribeLayout(SurfaceState state)
    {
        if (state.LayoutNotifier is not null && state.LayoutChangedHandler is not null)
        {
            state.LayoutNotifier.PropertyChanged -= state.LayoutChangedHandler;
            state.LayoutNotifier = null;
            state.LayoutChangedHandler = null;
        }
    }

    private static void OnCanvasChildAdded(object? sender, ElementEventArgs e)
    {
        if (sender is not AbsoluteLayout canvas)
        {
            return;
        }

        if (e.Element is ContentView child)
        {
            WorkflowSlotLayoutBehavior.Refresh(child);
        }

        var host = FindAncestorContentView(canvas);
        if (host is not null)
        {
            MainThread.BeginInvokeOnMainThread(() => Refresh(host));
        }
    }

    private static void OnCanvasChildRemoved(object? sender, ElementEventArgs e)
    {
        if (sender is AbsoluteLayout canvas && FindAncestorContentView(canvas) is { } host)
        {
            MainThread.BeginInvokeOnMainThread(() => Refresh(host));
        }
    }

    private static ContentView? FindAncestorContentView(BindableObject bindable)
    {
        Element? current = bindable as Element;
        while (current is not null)
        {
            if (current is ContentView contentView && GetIsEnabled(contentView))
            {
                return contentView;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        if (sender is not ScrollView viewer)
        {
            return;
        }

        var host = FindAncestorContentView(viewer);
        if (host is null)
        {
            return;
        }

        var state = (SurfaceState?)host.GetValue(StateProperty);
        if (state is null)
        {
            return;
        }

        // CRITICAL: do NOT suppress Scrolled during a pan. MAUI fires this on every native
        // ViewChanged (intermediate + final), and ScrollX/ScrollY are the native offsets at
        // that instant — the ONLY trustworthy position. ScrollToAsync is a fire-and-forget
        // ChangeView that can land short of the request (it never guarantees the target is hit),
        // and on Windows the native ScrollViewer's manipulation can move the content on its
        // own. Suppressing Scrolled froze the decorators at the requested target during the
        // drag, so when the native settled elsewhere after release the decorators snapped —
        // the release jump. Letting every scroll through (the minimap's path) keeps grid +
        // content glued at all times.
        Refresh(host);
    }

    private static void OnScrollViewerSizeChanged(object? sender, EventArgs e)
    {
        if (sender is ScrollView viewer)
        {
            var host = FindAncestorContentView(viewer);
            if (host is not null)
            {
                Refresh(host);
            }
        }
    }

    /// <summary>
    /// On Windows the native ScrollViewer must be a PASSIVE receiver of ChangeView only. MAUI's
    /// pan recognizer drives the surface through native manipulation events (ManipulationMode 35
    /// on the pointer-press source), and the nested ScrollViewer — a manipulation-capable control
    /// with the default <c>ManipulationMode.System</c> — would otherwise claim the manipulation
    /// as its container and scroll the content itself, with inertia. It then fights our
    /// programmatic ChangeView calls, and on pointer release its manipulation completes and
    /// applies its OWN accumulated offset — the post-release content jump we chased during
    /// the release-pan investigation.
    ///
    /// We demote it to a passive MANUAL container instead: <c>ManipulationMode</c> to the same
    /// TranslateX|TranslateY|Scale value MAUI uses on gesture containers. A non-<c>System</c>
    /// mode stops the ScrollViewer from claiming the gesture for its own scrolling, so ChangeView
    /// is the sole scroll driver (the same model the WinUI adapter uses). The manipulation still
    /// initiates on this control (it is the nearest non-<c>None</c> ancestor of the canvas) and
    /// bubbles to the parent's handlers, which is how the pan keeps working.
    ///
    /// IMPORTANT: do NOT use <c>ManipulationModes.None</c> here. None disables manipulation for
    /// the element AND its entire subtree, so no manipulation ever initiates inside the canvas —
    /// the pan stops working entirely (observed regression).
    /// </summary>
    private static void OnScrollViewerHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not ScrollView viewer)
        {
            return;
        }
#if WINDOWS
        if (viewer.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ScrollViewer sv)
        {
            sv.IsScrollInertiaEnabled = false;
            // None，而不是「去掉 System 再给三个轴」：TranslateX/TranslateY 正是 ScrollViewer
            // 用来做操纵滚动的那两个轴，留着它们它就还是第二个滚动驱动者，并在松手时把累积量补上
            // （实测 423→186，60ms 后）。平移已经改走指针事件、不再需要任何 manipulation，
            // 所以这里可以彻底关掉 —— 早先「None 会让平移失灵」的结论只对「平移仍靠 manipulation」
            // 那套成立。ChangeView 是程序化的，不受 ManipulationMode 影响。
            sv.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.None;
        }
#endif
    }

    private static void ApplyLayout(ContentView host, SurfaceState state)
    {
        var viewModel = ResolveTreeViewModel(host, state);
        if (viewModel is null || state.Canvas is null)
        {
            return;
        }

        var actualOffset = viewModel.Layout.ActualOffset;
        var actualSize = viewModel.Layout.ActualSize;

        state.Canvas.Margin = new Thickness(0);
        // Canvas-level TranslationX shifts ALL children simultaneously, keeping
        // nodes, links, and grid in perfect sync on every frame.
        // Per-child TranslationX (tried previously) causes frame-level desync
        // between decorator updates and child transforms.
        // WidthRequest is set in the SAME call from model data (not XAML binding
        // which adds async lag), avoiding the clipping that motivated the per-child
        // experiment.
        state.Canvas.TranslationX = actualOffset.Horizontal;
        state.Canvas.TranslationY = actualOffset.Vertical;

        if (state.Canvas.WidthRequest < actualSize.Width ||
            double.IsNaN(state.Canvas.WidthRequest))
            state.Canvas.WidthRequest = Math.Max(1, actualSize.Width);
        if (state.Canvas.HeightRequest < actualSize.Height ||
            double.IsNaN(state.Canvas.HeightRequest))
            state.Canvas.HeightRequest = Math.Max(1, actualSize.Height);

        // Decorator/minimap offsets are viewport data. Written from ScrollX (the native offset
        // as of the last ViewChanged) on EVERY Refresh — including during a pan — so grid +
        // content never diverge. The decorators coalesce their redraws (ScheduleInvalidate),
        // so the second write from ApplyVisibleRegion later in this Refresh is free.
        if (state.ScrollViewer is not null)
        {
            UpdateGridDecorator(viewModel, state, state.ScrollViewer.ScrollX, state.ScrollViewer.ScrollY);
            UpdateMinimapOverlay(viewModel, state, state.ScrollViewer.ScrollX, state.ScrollViewer.ScrollY);
        }
    }

    private static async void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        try
        {
            if (sender is not BindableObject bindable)
            {
                return;
            }

            var host = FindAncestorContentView(bindable);
            if (host is null || host.GetValue(StateProperty) is not SurfaceState state || state.ScrollViewer is null)
            {
                return;
            }

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    // Anchor the pan at the current scroll + pointer position. Each Running
                    // computes the target absolutely from this anchor (WPF/minimap style)
                    // instead of accumulating per-delta differences, which drift whenever a
                    // clamped ScrollToAsync lands off the requested offset.
                    state.PanAccumulatedX = state.ScrollViewer.ScrollX;
                    state.PanAccumulatedY = state.ScrollViewer.ScrollY;
                    state.PanAnchorTotalX = e.TotalX;
                    state.PanAnchorTotalY = e.TotalY;
                    state.PanGestureActive = true;
                    break;
                case GestureStatus.Running:
                    if (WorkflowNodeDragBehavior.IsDraggingNode || WorkflowSlotConnectionBehavior.IsDraggingConnection)
                    {
                        // Keep the anchor glued to the current scroll + pointer while a
                        // node/connection drag suppresses canvas panning, so that when the
                        // drag ends mid-gesture the resumed pan continues from the current
                        // position instead of jumping by the node-drag pointer distance.
                        state.PanAccumulatedX = state.ScrollViewer.ScrollX;
                        state.PanAccumulatedY = state.ScrollViewer.ScrollY;
                        state.PanAnchorTotalX = e.TotalX;
                        state.PanAnchorTotalY = e.TotalY;
                        break;
                    }

                    await ApplyPanAsync(host, state, e);
                    break;
                case GestureStatus.Canceled:
                case GestureStatus.Completed:
                    {
                        state.PanCts?.Cancel();
                        state.PanCts = null;
                        state.PanGestureActive = false;
                        state.PanAccumulatedX = state.ScrollViewer.ScrollX;
                        state.PanAccumulatedY = state.ScrollViewer.ScrollY;
                        // No forced finalize: the last ChangeView's landing fires Scrolled ->
                        // OnScrolled -> Refresh, which writes the decorators from the settled
                        // native offset. Dropping the flag is safe now that OnScrolled is the
                        // single decorator writer and reads ScrollX (native truth) directly.
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkflowSurfaceBehavior] Pan error: {ex.Message}");
        }
    }

    private static async Task ApplyPanAsync(ContentView host, SurfaceState state, PanUpdatedEventArgs e)
    {
        // Absolute target from the pan anchor — the same math WPF uses (startOffset + pointer
        // movement since the anchor). Reading the actual ScrollX each frame is what caused the
        // flash-back; accumulating a per-delta offset is what jittered (a clamped ScrollToAsync
        // lets the bookkeeping drift from the real position, so the content sticks then jumps).
        // The anchor only moves in Started, node-drag suppression, and the edge re-anchor below,
        // so it never accumulates error.
        await ApplyPanTargetAsync(
            host, state,
            state.PanAccumulatedX - (e.TotalX - state.PanAnchorTotalX),
            state.PanAccumulatedY - (e.TotalY - state.PanAnchorTotalY),
            e.TotalX, e.TotalY);
    }

    /// <summary>
    /// Applies an absolute pan target. Shared by the gesture path (non-Windows) and the native
    /// pointer path (Windows) — both end up with "where should the offset be" and "how far has the
    /// pointer travelled from the anchor", which is all this needs.
    /// </summary>
#if WINDOWS
    private static readonly Dictionary<Microsoft.UI.Xaml.UIElement, SurfaceState> PlatformPanStates = [];

    /// <summary>Press-source views that belong to a surface, so the handler can find its state.
    /// The state lives on the host <c>ContentView</c>, NOT on the press source — reading
    /// <c>StateProperty</c> off the press source always yields null.</summary>
    private static readonly Dictionary<View, SurfaceState> PlatformPanHosts = [];

    private static void OnPointerPressSourceHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is View view)
        {
            HookPlatformPan(view);
        }
    }

    private static void HookPlatformPan(View view)
    {
        if (!PlatformPanHosts.TryGetValue(view, out var state))
        {
            return;
        }

        UnhookPlatformPan(state);

        if (view.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
        {
            return;
        }

        element.PointerPressed += OnPlatformPanPressed;
        element.PointerMoved += OnPlatformPanMoved;
        element.PointerReleased += OnPlatformPanReleased;
        element.PointerCaptureLost += OnPlatformPanCaptureLost;
        PlatformPanStates[element] = state;
        state.PlatformPressSource = element;
    }

    private static void UnhookPlatformPan(SurfaceState state)
    {
        if (state.PlatformPressSource is null)
        {
            return;
        }

        state.PlatformPressSource.PointerPressed -= OnPlatformPanPressed;
        state.PlatformPressSource.PointerMoved -= OnPlatformPanMoved;
        state.PlatformPressSource.PointerReleased -= OnPlatformPanReleased;
        state.PlatformPressSource.PointerCaptureLost -= OnPlatformPanCaptureLost;
        PlatformPanStates.Remove(state.PlatformPressSource);
        state.PlatformPressSource = null;
    }

    private static void OnPlatformPanPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.UIElement element
            || !PlatformPanStates.TryGetValue(element, out var state)
            || state.ScrollViewer is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(null);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 节点/插槽拖拽优先：按下落在它们身上时画布不动
        if (WorkflowNodeDragBehavior.IsDraggingNode || WorkflowSlotConnectionBehavior.IsDraggingConnection)
        {
            state.PointerPanActive = false;
            return;
        }

        state.PanAccumulatedX = state.ScrollViewer.ScrollX;
        state.PanAccumulatedY = state.ScrollViewer.ScrollY;
        // The anchor is the same shape the gesture path keeps: PanAnchorTotal* is the cumulative
        // pointer travel AT the anchor, which is zero here because the pointer anchor is this point.
        state.PanAnchorTotalX = 0;
        state.PanAnchorTotalY = 0;
        state.PointerAnchorX = point.Position.X;
        state.PointerAnchorY = point.Position.Y;
        state.PointerPanActive = true;
        state.PanGestureActive = true;
        element.CapturePointer(e.Pointer);
    }

    private static async void OnPlatformPanMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not Microsoft.UI.Xaml.UIElement element
                || !PlatformPanStates.TryGetValue(element, out var state)
                || !state.PointerPanActive
                || state.ScrollViewer is null
                || state.Host is null)
            {
                return;
            }

            var point = e.GetCurrentPoint(null);
            if (!point.Properties.IsLeftButtonPressed)
            {
                EndPlatformPan(state, element, e);
                return;
            }

            if (WorkflowNodeDragBehavior.IsDraggingNode || WorkflowSlotConnectionBehavior.IsDraggingConnection)
            {
                // 拖节点那一段画布不跟着走；把锚点贴到当前位置，手势结束时才不会补跳一段
                state.PanAccumulatedX = state.ScrollViewer.ScrollX;
                state.PanAccumulatedY = state.ScrollViewer.ScrollY;
                state.PanAnchorTotalX = 0;
                state.PanAnchorTotalY = 0;
                state.PointerAnchorX = point.Position.X;
                state.PointerAnchorY = point.Position.Y;
                return;
            }

            var totalX = point.Position.X - state.PointerAnchorX;
            var totalY = point.Position.Y - state.PointerAnchorY;

            // Same form as the gesture path: the core re-anchors PanAnchorTotal* on overscroll, and
            // a target of "anchor - total" would then add the travel again on every later frame.
            await ApplyPanTargetAsync(
                state.Host, state,
                state.PanAccumulatedX - (totalX - state.PanAnchorTotalX),
                state.PanAccumulatedY - (totalY - state.PanAnchorTotalY),
                totalX, totalY);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkflowSurfaceBehavior] Pointer pan error: {ex.Message}");
        }
    }

    private static void OnPlatformPanReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.UIElement element
            && PlatformPanStates.TryGetValue(element, out var state))
        {
            EndPlatformPan(state, element, e);
        }
    }

    private static void OnPlatformPanCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.UIElement element
            && PlatformPanStates.TryGetValue(element, out var state))
        {
            Settle(state);
        }
    }

    private static void EndPlatformPan(
        SurfaceState state, Microsoft.UI.Xaml.UIElement element, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!state.PointerPanActive)
        {
            return;
        }

        Settle(state);
        element.ReleasePointerCapture(e.Pointer);
    }

    private static void Settle(SurfaceState state)
    {
        state.PointerPanActive = false;
        state.PanGestureActive = false;
        state.PanCts?.Cancel();
        state.PanCts = null;

        if (state.ScrollViewer is not null)
        {
            state.PanAccumulatedX = state.ScrollViewer.ScrollX;
            state.PanAccumulatedY = state.ScrollViewer.ScrollY;
        }
    }
#endif

    private static async Task ApplyPanTargetAsync(
        ContentView host, SurfaceState state, double desiredX, double desiredY, double totalX, double totalY)
    {
        var viewModel = ResolveTreeViewModel(host, state);
        if (viewModel is null || state.ScrollViewer is null)
        {
            return;
        }

        // Cancel any previous in-flight ScrollToAsync to prevent cascading.
        state.PanCts?.Cancel();
        state.PanCts = new CancellationTokenSource();
        var ct = state.PanCts.Token;

        // The absolute target arrives already resolved (gesture deltas on non-Windows, raw pointer
        // travel on Windows); everything below is the shared clamp / expand / apply.
        var maxH = GetHorizontalScrollMaximum(state);
        var maxV = GetVerticalScrollMaximum(state);
        // layoutChanged = the desired offset overshoots [0, max] on either axis, which is
        // exactly when ClampScrollOffset (below) expands the canvas.  Kept adapter-specific
        // so the max-recompute flow below stays unchanged.
        var layoutChanged = desiredX < 0 || desiredX > maxH || desiredY < 0 || desiredY > maxV;

        // Expand canvas when pan reaches the edge. Expansion IS the correct behavior
        // (matching WPF). The original crash was caused by a cascade:
        // expansion → Refresh → more expansion. The IsRefreshing guard in Refresh()
        // breaks this cycle.

        // ClampScrollOffset writes the overshoot into NegativeOffset (before origin) or
        // PositiveOffset (past the content edge) and returns the clamped offset to apply.
        // threshold 0 = always expand, matching the previous inline branches.
        var newOffsetX = WorkflowSurfaceMath.ClampScrollOffset(
            desiredX, maxH, viewModel.Layout, horizontal: true, threshold: 0, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
        var newOffsetY = WorkflowSurfaceMath.ClampScrollOffset(
            desiredY, maxV, viewModel.Layout, horizontal: false, threshold: 0, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);

        if (layoutChanged)
        {
            ApplyLayout(host, state);
            // Recompute max from model — canvas size was just updated via ApplyLayout
            // but MAUI layout is async so ScrollViewer.ContentSize is stale.
            maxH = GetHorizontalScrollMaximum(state);
            maxV = GetVerticalScrollMaximum(state);
        }

        // Guard against NaN from ScrollViewer.Width/Height during async layout.
        // Math.Max(0, NaN) = NaN, which would crash ScrollToAsync.
        if (!double.IsFinite(maxH)) maxH = 0;
        if (!double.IsFinite(maxV)) maxV = 0;

        // Bound the applied target by the NATIVE ScrollViewer extent, not just the model.
        // On overscroll the model's ActualSize (and canvas WidthRequest) grows
        // SYNCHRONOUSLY, but the native content re-measures ASYNCHRONOUSLY, so during a
        // fast drag the native extent can lag the model by thousands of pixels. ChangeView
        // clamps to the native extent, so a model-based target lands short of the native edge.
        // Re-anchoring at the model edge then sets the bookkeeping AHEAD of the native:
        // the content pins at the stale native edge and, when the native finally re-measures,
        // jumps forward in one frame to catch the anchor. Clamping the applied target to the
        // live native extent makes every ChangeView land exactly, so the content eases with
        // the re-measure instead of jumping (and never over-shoots on release).
        var appliedOffsetX = WorkflowSurfaceMath.ClampValue(newOffsetX, 0, GetNativeScrollMaximum(state, horizontal: true));
        var appliedOffsetY = WorkflowSurfaceMath.ClampValue(newOffsetY, 0, GetNativeScrollMaximum(state, horizontal: false));

        if (layoutChanged)
        {
            // Re-anchor at the APPLIED (native-bounded) offset — never the model edge — so
            // the bookkeeping stays exactly where the content will actually land this frame.
            state.PanAccumulatedX = appliedOffsetX;
            state.PanAccumulatedY = appliedOffsetY;
            state.PanAnchorTotalX = totalX;
            state.PanAnchorTotalY = totalY;
        }

        // The decorators are NOT written from the requested target: ChangeView is fire-and-forget
        // and can land short of the request (the native manipulation fights it on Windows), so
        // writing the requested target would desync the grid from the content. The native
        // ViewChanged -> Scrolled -> OnScrolled -> Refresh path writes the decorators from the
        // ACTUAL landed position on every move — the same single-writer path the smooth minimap uses.
        try
        {
            await state.ScrollViewer.ScrollToAsync(appliedOffsetX, appliedOffsetY, false);
        }
        catch (OperationCanceledException)
        {
            // Previous scroll was superseded by a newer pan delta — expected.
            return;
        }

        if (!ct.IsCancellationRequested)
        {
            UpdateVisibleRegion(host, state);
        }
    }



    private static double GetHorizontalScrollMaximum(SurfaceState state)
    {
        // Compute max scroll from the layout model.  Canvas size is driven
        // by ViewModel binding (Layout.ActualSize).  The ScrollView content
        // is the AbsoluteLayout which follows ActualSize from the binding,
        // so ActualSize - viewportWidth gives the correct scroll extent.
        var viewModel = ResolveTreeViewModel(state.Host!, state);
        if (viewModel is null || state.ScrollViewer is null) return 0;
        var w = viewModel.Layout.ActualSize.Width - state.ScrollViewer.Width;
        return double.IsNaN(w) || w < 0 ? 0 : w;
    }

    private static double GetVerticalScrollMaximum(SurfaceState state)
    {
        var viewModel = ResolveTreeViewModel(state.Host!, state);
        if (viewModel is null || state.ScrollViewer is null) return 0;
        var h = viewModel.Layout.ActualSize.Height - state.ScrollViewer.Height;
        return double.IsNaN(h) || h < 0 ? 0 : h;
    }

    /// <summary>
    /// The scroll extent the native ScrollViewer will actually accept RIGHT NOW:
    /// <c>min(model extent, native Extent − viewport)</c>. On overscroll the model's
    /// <see cref="CanvasLayout.ActualSize"/> (and the canvas WidthRequest/HeightRequest set by
    /// <see cref="ApplyLayout"/>) grows synchronously while the native content re-measures
    /// asynchronously, so the native extent can lag the model by a lot during a fast drag.
    /// ChangeView clamps to the native extent, so this is the true ceiling for any applied
    /// target — a model-based ceiling lets the content pin at the stale native edge and then
    /// jump forward when the native catches up. Falls back to the model extent on non-Windows
    /// platforms (no ChangeView clamp desync there).
    /// </summary>
    private static double GetNativeScrollMaximum(SurfaceState state, bool horizontal)
    {
        var modelMax = horizontal ? GetHorizontalScrollMaximum(state) : GetVerticalScrollMaximum(state);
#if WINDOWS
        if (state.ScrollViewer?.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ScrollViewer sv)
        {
            var extent = horizontal ? sv.ExtentWidth : sv.ExtentHeight;
            var viewport = horizontal ? sv.ViewportWidth : sv.ViewportHeight;
            if (double.IsFinite(extent) && double.IsFinite(viewport) && extent > 0 && viewport > 0)
            {
                var nativeMax = extent - viewport;
                if (double.IsFinite(nativeMax) && nativeMax >= 0)
                {
                    return Math.Min(modelMax, nativeMax);
                }
            }
        }
#endif
        return modelMax;
    }

    private static IWorkflowTreeViewModel? ResolveTreeViewModel(ContentView host, SurfaceState state)
        => host.BindingContext as IWorkflowTreeViewModel
            ?? state.Canvas?.BindingContext as IWorkflowTreeViewModel
            ?? state.ScrollViewer?.BindingContext as IWorkflowTreeViewModel
            ?? state.GridDecorator?.BindingContext as IWorkflowTreeViewModel
            ?? state.PointerPressSource?.BindingContext as IWorkflowTreeViewModel;

    private static void UpdateVisibleRegion(ContentView host, SurfaceState state)
    {
        if (state.IsVisibleRegionUpdateQueued)
        {
            return;
        }

        state.IsVisibleRegionUpdateQueued = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!GetIsEnabled(host)
                || host.GetValue(StateProperty) is not SurfaceState currentState
                || !ReferenceEquals(currentState, state))
            {
                return;
            }

            state.IsVisibleRegionUpdateQueued = false;
            ApplyVisibleRegion(host, state);
        });
    }

    private static void ApplyVisibleRegion(ContentView host, SurfaceState state)
    {
        var viewModel = ResolveTreeViewModel(host, state);
        if (viewModel is null || state.ScrollViewer is null)
        {
            return;
        }

        // CRITICAL: After ScrollToAsync completes, ScrollViewer dimensions and
        // scroll position can still be NaN/zero during MAUI's async layout pass.
        // NaN propagates through Viewport → Virtualize → spatial index, causing
        // all VisibleItems to be cleared (links permanently disappear).
        // NaN <= 0 returns false in C#, so Virtualize's guard does NOT catch this.
        // CRITICAL: always write from ScrollX — the native offset as of the last ViewChanged,
        // i.e. where the content ACTUALLY is. The requested target can differ from ScrollX
        // whenever a ChangeView lands short, and writing the requested target here is exactly
        // what made the grid snap back to the true position after release.
        var scrollX = state.ScrollViewer.ScrollX;
        var scrollY = state.ScrollViewer.ScrollY;
        var svW = state.ScrollViewer.Width;
        var svH = state.ScrollViewer.Height;

        if (double.IsNaN(scrollX) || double.IsNaN(scrollY) ||
            double.IsNaN(svW) || double.IsNaN(svH) ||
            svW <= 0 || svH <= 0)
        {
            return;
        }

        UpdateGridDecorator(viewModel, state, scrollX, scrollY);
        UpdateMinimapOverlay(viewModel, state, scrollX, scrollY);

        var viewportX = WorkflowSurfaceMath.ToWorld(scrollX, viewModel.Layout.ActualOffset.Horizontal);
        var viewportY = WorkflowSurfaceMath.ToWorld(scrollY, viewModel.Layout.ActualOffset.Vertical);
        viewModel.GetHelper().Viewport = new Viewport(
            double.IsNaN(viewportX) ? 0 : viewportX,
            double.IsNaN(viewportY) ? 0 : viewportY,
            svW, svH);

        // Persist the viewport position so it survives serialization round-trip. World, like every other
        // adapter and like the value just handed to Viewport above — the restore path reads it back as world
        // (ApplyPendingScrollRestoreCore → ToScreen), so raw scroll here would be added to ActualOffset twice.
        viewModel.Layout.ViewportOffset = new Offset(
            double.IsNaN(viewportX) ? 0 : viewportX,
            double.IsNaN(viewportY) ? 0 : viewportY);
    }

    private static void UpdateGridDecorator(IWorkflowTreeViewModel viewModel, SurfaceState state, double scrollX, double scrollY)
    {
        if (state.GridDecorator is not IWorkflowGridDecorator decorator)
        {
            return;
        }

        decorator.ScrollOffsetX = double.IsNaN(scrollX) ? 0 : scrollX;
        decorator.ScrollOffsetY = double.IsNaN(scrollY) ? 0 : scrollY;
        decorator.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        decorator.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;

        // Keep the virtualization visible-region correction in sync with the decorator's
        // floating ruler band so nodes beneath it are not culled a ruler-thickness early.
        viewModel.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand);
    }

    private static void UpdateMinimapOverlay(IWorkflowTreeViewModel viewModel, SurfaceState state, double scrollX, double scrollY)
    {
        if (state.MinimapOverlay is not IWorkflowMinimapOverlay minimap || state.ScrollViewer is null)
        {
            return;
        }

        minimap.ScrollOffsetX = double.IsNaN(scrollX) ? 0 : scrollX;
        minimap.ScrollOffsetY = double.IsNaN(scrollY) ? 0 : scrollY;
        minimap.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        minimap.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
        minimap.ViewportWidth = double.IsNaN(state.ScrollViewer.Width) ? 1 : state.ScrollViewer.Width;
        minimap.ViewportHeight = double.IsNaN(state.ScrollViewer.Height) ? 1 : state.ScrollViewer.Height;
        minimap.WorkflowTree = viewModel;
    }

    private static void ApplyPendingScrollRestore(ContentView host, SurfaceState state)
    {
        if (!state.HasPendingScrollRestore || state.ScrollViewer is null)
        {
            return;
        }

        state.HasPendingScrollRestore = false;

        // Dispatch async restoration via IDispatcher to avoid async void.
        // The Task is fire-and-forget but will not silently swallow exceptions.
        _ = host.Dispatcher.DispatchAsync(() => ApplyPendingScrollRestoreCore(host, state));
    }

    private static async Task ApplyPendingScrollRestoreCore(ContentView host, SurfaceState state)
    {
        try
        {
            if (state.ScrollViewer is null)
            {
                return;
            }

            // Yield once so that any pending layout pass (from ApplyLayout called
            // before this method) settles, ensuring ActualOffset is up-to-date.
            await Task.Yield();

            var viewModel = ResolveTreeViewModel(host, state);
            if (viewModel is null)
            {
                return;
            }

            // ToScreen: screen = world + ActualOffset (the pending viewport is in world space).
            var target = WorkflowSurfaceMath.ToScreen(state.PendingViewportX, state.PendingViewportY, viewModel.Layout);
            var targetX = WorkflowSurfaceMath.ClampValue(target.Horizontal, 0, GetHorizontalScrollMaximum(state));
            var targetY = WorkflowSurfaceMath.ClampValue(target.Vertical, 0, GetVerticalScrollMaximum(state));

            if (Math.Abs(state.ScrollViewer.ScrollX - targetX) > 0.5
                || Math.Abs(state.ScrollViewer.ScrollY - targetY) > 0.5)
            {
                await state.ScrollViewer.ScrollToAsync(targetX, targetY, false);
            }

            UpdateVisibleRegion(host, state);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[WorkflowSurfaceBehavior] ScrollRestore error: {ex.Message}");
        }
    }
}
