using System.ComponentModel;
using PlatformInput = Microsoft.Maui.Controls;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;
using Wf = VeloxDev.WorkflowSystem;

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

        // 订阅者否决了这一笔平移（非 Windows 由手势读句柄得出）：Running 不再提交任何滚动。
        public bool PanPrevented { get; set; }

        /// <summary>Pointer position at the pan anchor, in the press source's coordinate space.
        /// The pointer's distance from the anchor is the same quantity the gesture's TotalX/TotalY
        /// carried.</summary>
        public double PointerAnchorX { get; set; }
        public double PointerAnchorY { get; set; }

        // 连线右键菜单：菜单由模板声明（条目归用户，见 LinkMenuKeyProperty），订阅、定位、弹出、挂起都在这里。
        public MenuFlyout? LinkMenu { get; set; }
        public IWorkflowLinkViewModel? MenuLink { get; set; }

        // 这棵树的输入路由：菜单开着时由它挂起指针跟踪，接线的那两个订阅也从它来。
        public WorkflowInput? Input { get; set; }
        public EventHandler<Wf.PointerPressedEventArgs>? MenuPressed { get; set; }
        public EventHandler<IWorkflowLinkViewModel>? MenuLinkRemoved { get; set; }

#if WINDOWS
        /// <summary>The press source's platform element with the native pointer handlers attached.</summary>
        public Microsoft.UI.Xaml.UIElement? PlatformPressSource { get; set; }

        // Windows 上正在弹的原生 flyout；输入路由管不到它，由这里 Hide，并在它的 Closed 里清掉。
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
    /// resolves the menu by key, feeds the pressed link to each item, positions it, opens it, and suspends the
    /// tree's <see cref="WorkflowInput"/> while it is open.
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

        // 重入防护：ApplyLayout 中的画布扩展会触发 Scrolled/SizeChanged 再次调用 Refresh，必须挡住。
        // 没有它，每次扩展级联 2-3 次 Refresh，滚成正反馈的减速螺旋。
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

    // 连线右键菜单：**条目由模板声明**（挂在 LinkMenuKey 上），**接线在这里** —— 订输入面、定位、弹出、
    // 挂起指针跟踪，模板因此没有一行交互代码。菜单指着的那条线离树时树会报 LinkRemoved，这里收自己那份。
    // MAUI 的 MenuFlyout 是资源、不是可视物，也没有 Opened/Closed，所以挂起由两条呈现路径各自上报。
    private static void WireLinkMenu(ContentView host, SurfaceState state)
    {
        // 资源在 attach 之后才一定就绪（第一次 Refresh 可能早于 Resources 解析完），所以每次 Refresh 都重查一次。
        var key = GetLinkMenuKey(host);
        state.LinkMenu = key is { Length: > 0 } ? FindLinkMenuResource(host, key) : null;

        var input = ResolveTreeViewModel(host, state) is { } tree ? WorkflowInput.For(tree) : null;
        if (ReferenceEquals(input, state.Input))
        {
            return;
        }

        if (state.Input is not null)
        {
            UnsubscribeMenu(state);
        }

        state.Input = input;
        if (input is null)
        {
            return;
        }

        var bound = input.Tree;
        state.MenuPressed = (_, e) => ShowLinkMenu(host, state, e);
        state.MenuLinkRemoved = (_, link) =>
        {
            // 「菜单不能比它指着的那条线活得久」：Delete、Undo、Agent 改树都走这条路。
            if (!ReferenceEquals(state.MenuLink, link)) return;
#if WINDOWS
            state.OpenFlyout?.Hide();
#else
            DismissLinkMenu(state);
#endif
        };
        ((Wf.IInputEvents)bound.GetHelper()).Input.PointerPressed += state.MenuPressed;
        bound.GetHelper().LinkRemoved += state.MenuLinkRemoved;
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

        state.Input = null;
        state.MenuPressed = null;
        state.MenuLinkRemoved = null;
        state.MenuLink = null;
    }

    private static void UnwireLinkMenu(SurfaceState state)
    {
        UnsubscribeMenu(state);
        state.LinkMenu = null;

#if WINDOWS
        // 先摘订阅再收弹窗：Closed 处理器里 state.Input 已为空，不会往路由补发一发。
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
    private static void ShowLinkMenu(ContentView host, SurfaceState state, Wf.PointerPressedEventArgs e)
    {
        // 只有右键、且落在连线上才弹：空白画布没有可操作的对象。
        if (e.Button != Wf.MouseButton.Right) return;
        if (e.Target is not IWorkflowLinkViewModel link) return;

        // 链上更靠前的一级（连线自己）可以否决这次按下 —— 它说不给菜单，这里就不给。
        if (e.Handle.PreventDefault) return;

        var menu = state.LinkMenu;
        var gridDecorator = state.GridDecorator;
        if (menu is null || gridDecorator is null) return;
        if (ResolveTreeViewModel(host, state) is null) return;
        if (gridDecorator is not IWorkflowGridDecorator decorator) return;
        if (gridDecorator.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement hostElement) return;

        state.MenuLink = link;

        var flyout = BuildPlatformMenu(menu, link);
        state.OpenFlyout = flyout;

        // 挂起由表面自己记账：菜单开着时指针飞到菜单上，也不该清掉这次选中的连线。
        flyout.Closed += (_, _) =>
        {
            state.OpenFlyout = null;
            state.MenuLink = null;
            if (state.Input is { } input) input.IsSuspended = false;
        };
        if (state.Input is { } opening) opening.IsSuspended = true;

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
                // MenuFlyoutSeparator 派生自 MenuFlyoutItem，必须先匹配它。
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
    private static void ShowLinkMenu(ContentView host, SurfaceState state, Wf.PointerPressedEventArgs e)
    {
        // 只有右键、且落在连线上才弹：空白画布没有可操作的对象。
        if (e.Button != Wf.MouseButton.Right) return;
        if (e.Target is not IWorkflowLinkViewModel link) return;

        // 链上更靠前的一级（连线自己）可以否决这次按下 —— 它说不给菜单，这里就不给。
        if (e.Handle.PreventDefault) return;

        var menu = state.LinkMenu;
        var gridDecorator = state.GridDecorator;
        if (menu is null || gridDecorator is null) return;
        if (ResolveTreeViewModel(host, state) is null) return;
        if (gridDecorator is not IWorkflowGridDecorator decorator) return;

        EnsureLinkMenuLayer(state);
        var layer = state.LinkMenuLayer;
        var menuHost = state.LinkMenuHost;
        var items = state.LinkMenuItems;
        if (layer is null || menuHost is null || items is null) return;

        state.MenuLink = link;

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
                    item.BindingContext = link;
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
        state.MenuLink = link;
        if (state.Input is { } opening) opening.IsSuspended = true;
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
        state.MenuLink = null;
        if (state.Input is { } input) input.IsSuspended = false;
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
            // 在平台视图出现后配置原生 ScrollViewer（handler 可能在本挂接之后才创建）；handler 重建时会再跑一次。
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

    // MAUI 跨平台：触摸用捏合手势缩放（到处可用）；Windows 上还接受 Ctrl+滚轮。两者都写 Layout.Scale（节点由 Core 折叠）。
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
            // 闭包捕获 MAUI 宿主：平台元素没有 DataContext，而 MAUI ContentView 的 BindingContext 就是工作流树。
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

        // 缩放也要能被订阅者否决。这一处只做「读得到句柄」这一步：转发一笔滚轮、把句柄读出来。
        // 否决后要不要退回平台自己的滚动，全仓还没拉平，暂不在这里单独定。
        if (RouteSurfaceWheel(state, e, delta)?.PreventDefault == true)
        {
            return;
        }

        // 滚轮向上（增量为正）放大：Scale 是折叠因子，放大要除以 1/1.1。
        var factor = delta > 0 ? 1 / 1.1 : 1.1;
        var next = Math.Max(0.1, Math.Min(10, viewModel.Layout.Scale.Horizontal * factor));
        var layout = viewModel.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter && state.ScrollViewer is { } sv)
        {
            // 捕获视口中心下方的世界点，绕它折叠，再经延后异步路径重新居中滚动（MAUI 原生范围异步重测）。
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                sv.ScrollX, sv.ScrollY, sv.Width, sv.Height, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);
            RecenterOnWorldPointAsync(control, state, wx, wy);
        }
        else
        {
            layout.Scale = new Scale(next, next);
            // 世界原点缩放让内容对齐左上角，所以要让负象限内容保持可达就得在这里重应用已扩大的覆盖 —— 别处（没有 PivotCenterScroll）不会采纳新的 ActualOffset。
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

            // 深度放大把负向内容折叠到 w/Scale，固定的 NegativeOffset（== ActualOffset）在某个比例以下就盖不住它 —— 内容跑出左/上可滚动区、连线在那里被截断。
            // 先扩大覆盖（单调，只有正向内容时无事），下面的 PivotCenterScroll 才能读到新的 ActualOffset，重居中正好平移增长量。
            if (WorkflowSurfaceMath.EnsureNegativeCover(viewModel))
            {
                // 覆盖让模型 ActualOffset/ActualSize 长大了；要在读取下面的原生滚动最大值之前把画布元素（平移 + WidthRequest）推到新值 ——
                // 否则 GetHorizontalScrollMaximum 还是覆盖前的范围，夹取落短，长出来的负向内容够不着（左/上截断迟一格）。
                ApplyLayout(host, state);
                await Task.Yield(); // let MAUI's async native extent re-measure against the grown canvas
            }

            var svW = double.IsNaN(state.ScrollViewer.Width) ? 1 : state.ScrollViewer.Width;
            var svH = double.IsNaN(state.ScrollViewer.Height) ? 1 : state.ScrollViewer.Height;
            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(worldX, worldY, viewModel.Layout, svW, svH);
            // 越界扩展画布让枢轴总能到达；单纯夹取会把枢轴推离中心，下一次滚轮又把这个漂移当成抖动重捕。
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
                    // 与滚轮路径同样的世界原点重应用：深度放大把内容折叠越过固定 NegativeOffset 时扩大覆盖，再把新范围推出去。
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
            // 只对 OriginSize 变化反应（它可能在滚动/平移之外发生，如 AdaptTo、程序化改尺寸）。平移期间 PositiveOffset/NegativeOffset 每帧都变，
            // 但 ApplyPanAsync 已直接调 ApplyLayout + UpdateVisibleRegion 处理；在这里再反应会让每帧平移的工作量翻三倍
            // （LayoutChangedHandler 先发，ScrollToAsync 又触发 OnScrolled），造成级联减速。ViewportOffset 也过滤掉，
            // 避免 ApplyVisibleRegion 写 ViewportOffset 又触发一次 Refresh 的循环。
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

        // 关键：平移期间不要压制 Scrolled。MAUI 每次原生 ViewChanged（中间 + 最终）都会发它，ScrollX/ScrollY 就是那一刻的原生偏移 —— 唯一可信的位置。
        // ScrollToAsync 是即发即忘的 ChangeView，可能没落到请求目标（从不保证命中），Windows 上原生 ScrollViewer 的 manipulation 还会自己移动内容。
        // 压制 Scrolled 曾让装饰块在拖拽中冻在请求目标上，释放后原生落到别处、装饰块就猛地一跳 —— 释放跳变。
        // 放行每次滚动（缩略图那条路）才能让网格与内容始终贴在一起。
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
        // 画布级 TranslationX 同时移动所有子元素，节点、连线、网格每帧完全同步；逐子元素 TranslationX（以前试过）会让装饰更新与子元素变换帧级不同步。
        // WidthRequest 在同一次调用里由模型数据直接设（不用会引入异步延迟的 XAML 绑定），避免当初推动逐子元素实验的裁剪问题。
        state.Canvas.TranslationX = actualOffset.Horizontal;
        state.Canvas.TranslationY = actualOffset.Vertical;

        if (state.Canvas.WidthRequest < actualSize.Width ||
            double.IsNaN(state.Canvas.WidthRequest))
            state.Canvas.WidthRequest = Math.Max(1, actualSize.Width);
        if (state.Canvas.HeightRequest < actualSize.Height ||
            double.IsNaN(state.Canvas.HeightRequest))
            state.Canvas.HeightRequest = Math.Max(1, actualSize.Height);

        // 装饰块/缩略图偏移是视口数据，每次 Refresh 都从 ScrollX（最近一次 ViewChanged 的原生偏移）写，平移期间也写，网格与内容因此不会分家。
        // 装饰块会合并重绘（ScheduleInvalidate），本 Refresh 稍后 ApplyVisibleRegion 的第二次写是免费的。
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
                    // 把平移锚在当前滚动 + 指针位置；每次 Running 从这个锚绝对地算目标（WPF/缩略图风格），而不是累加逐增量差 —— 夹取过的 ScrollToAsync 一旦没落到请求偏移就会漂移。
                    state.PanAccumulatedX = state.ScrollViewer.ScrollX;
                    state.PanAccumulatedY = state.ScrollViewer.ScrollY;
                    state.PanAnchorTotalX = e.TotalX;
                    state.PanAnchorTotalY = e.TotalY;
                    state.PanGestureActive = true;
                    // 这一笔的否决由别人转发时给出：本家没有隧道路由相，链接层挂在交互源上的按下转发
                    // 在平台手势之前跑完，句柄现成（见 RouteComponentPress）。手势这里没有可转发的东西
                    // —— PanUpdated 不给指针位置，转发一笔没有位置的按下只会让宿主听见一笔假的。
                    state.PanPrevented = ResolveTreeViewModel(host, state) is { } startedTree
                        && PeekRoutedPress(startedTree)?.PreventDefault == true;
                    break;
                case GestureStatus.Running:
                    if (state.PanPrevented)
                    {
                        break;
                    }

                    if (WorkflowNodeDragBehavior.IsDraggingNode || WorkflowSlotConnectionBehavior.IsDraggingConnection)
                    {
                        // 节点/连线拖拽压制画布平移期间，把锚点贴在当前滚动 + 指针上；这样拖拽在手势中途结束、平移恢复时是从当前位置继续，而不是跳过一段节点拖拽的指针距离。
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
                        state.PanPrevented = false;
                        state.PanAccumulatedX = state.ScrollViewer.ScrollX;
                        state.PanAccumulatedY = state.ScrollViewer.ScrollY;
                        // 不强制收尾：最后一次 ChangeView 落地会发 Scrolled → OnScrolled → Refresh，从稳定的原生偏移写装饰块。OnScrolled 现在是唯一的装饰写入者、直接读 ScrollX（原生真相），丢掉标记是安全的。
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
        // 从平移锚得到绝对目标 —— 与 WPF 同一套算法（起始偏移 + 锚点以来的指针位移）。每帧读实际 ScrollX 曾导致回闪；累加逐增量偏移曾导致抖动（被夹取的 ScrollToAsync 让记账偏离真实位置，内容先粘住再跳）。
        // 锚只在 Started、节点拖拽压制、以及下面的边缘重锚时移动，所以永不累积误差。
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

        // 这一笔由表面自己转发（与别家的表面同一手）：本家没有隧道路由相，链接层挂在交互源上的按下
        // 转发比这里晚，句柄只能在这里现做。订阅者置 PreventDefault 就是「这一次别平移」—— 连指针
        // 捕获都不做，那一笔要走的定制（宿主自己的拖拽）才拿得到指针。
        if (RouteSurfacePress(state, e)?.PreventDefault == true)
        {
            state.PointerPanActive = false;
            return;
        }

        state.PanAccumulatedX = state.ScrollViewer.ScrollX;
        state.PanAccumulatedY = state.ScrollViewer.ScrollY;
        // 锚与手势路径同形：PanAnchorTotal* 是锚点处的累计指针位移，这里为零，因为指针锚就是这一点。
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

            // 与手势路径同形：Core 在越界时重锚 PanAnchorTotal*，若目标写成 anchor - total 就会在之后每一帧重复加上位移。
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

    // 表面自己那一笔输入的落点：指针在画布元素里的坐标 —— 画布就摆在 Ruler + ContentOffset −
    // ScrollOffset 处（见链接层的类文档），所以这就是链接层那个 canvas-local 帧，两条路的句柄才指着
    // 同一处。量不到画布元素或树时返回 null：把这一笔让给链接层（它挂得更外，照样会转发）。
    private static (IWorkflowTreeViewModel Tree, VisualElement Canvas, Point Point)? ResolveSurfaceInput(
        SurfaceState state, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (state.Host is not { } host
            || state.Canvas is not { } canvas
            || canvas.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement canvasElement
            || ResolveTreeViewModel(host, state) is not { } tree)
        {
            return null;
        }

        var point = e.GetCurrentPoint(canvasElement).Position;
        return (tree, canvas, new Point(point.X, point.Y));
    }

    // 表面自己那一笔按下的转发。订阅者在画布或它命中的连线上置 PreventDefault 就是「这一次别平移」。
    private static WorkflowEventHandle? RouteSurfacePress(
        SurfaceState state, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ResolveSurfaceInput(state, e) is not { } input)
        {
            return null;
        }

        return RoutePressOnce(input.Tree, () =>
        {
            var routed = WorkflowInput.For(input.Tree);
            var handle = new WorkflowEventHandle();
            routed.Route(new Wf.PointerPressedEventArgs(
                new Anchor(input.Point.X, input.Point.Y, 0), Modifiers(e.KeyModifiers), input.Canvas,
                ResolveTarget(null, input.Tree, input.Point, routed.HitRadius),
                Wf.MouseButton.Left, 1, handle));
            return handle;
        });
    }

    // 表面自己那一笔滚轮的转发。滚轮的两条路（链接层与 Ctrl+滚轮的缩放）共用它，见 RouteWheelOnce。
    private static WorkflowEventHandle? RouteSurfaceWheel(
        SurfaceState state, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e, int delta)
    {
        if (ResolveSurfaceInput(state, e) is not { } input)
        {
            return null;
        }

        return RouteWheelOnce(e, () =>
        {
            var handle = new WorkflowEventHandle();
            var routed = WorkflowInput.For(input.Tree);
            routed.Route(new Wf.PointerWheelEventArgs(
                new Anchor(input.Point.X, input.Point.Y, 0), Modifiers(e.KeyModifiers), input.Canvas,
                ResolveTarget(null, input.Tree, input.Point, routed.HitRadius),
                0d, delta, handle));
            return handle;
        });
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

        // 取消上一个在飞的 ScrollToAsync，防止级联。
        state.PanCts?.Cancel();
        state.PanCts = new CancellationTokenSource();
        var ct = state.PanCts.Token;

        // 绝对目标传来时已解析好（非 Windows 是手势增量，Windows 是原始指针位移）；下面全是共用的夹取/扩展/应用。
        var maxH = GetHorizontalScrollMaximum(state);
        var maxV = GetVerticalScrollMaximum(state);
        // layoutChanged = 目标偏移在任一轴越过 [0, max]，正好是 ClampScrollOffset（下面）扩展画布的时候；保留为适配器专属，下面的最大重算流程不变。
        var layoutChanged = desiredX < 0 || desiredX > maxH || desiredY < 0 || desiredY > maxV;

        // 平移到达边缘时扩展画布，扩展本就是正确行为（与 WPF 一致）。当初的崩溃源于级联：扩展 → Refresh → 再扩展；Refresh() 里的 IsRefreshing 防护打断这个循环。

        // ClampScrollOffset 把越界量写进 NegativeOffset（原点前）或 PositiveOffset（内容边缘外），并返回要应用的夹取偏移；阈值 0 = 总是扩展，与之前的内联分支一致。
        var newOffsetX = WorkflowSurfaceMath.ClampScrollOffset(
            desiredX, maxH, viewModel.Layout, horizontal: true, threshold: 0, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
        var newOffsetY = WorkflowSurfaceMath.ClampScrollOffset(
            desiredY, maxV, viewModel.Layout, horizontal: false, threshold: 0, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);

        if (layoutChanged)
        {
            ApplyLayout(host, state);
            // 从模型重算最大值 —— 画布尺寸刚经 ApplyLayout 更新，但 MAUI 布局是异步的，ScrollViewer.ContentSize 还是旧的。
            maxH = GetHorizontalScrollMaximum(state);
            maxV = GetVerticalScrollMaximum(state);
        }

        // 防住异步布局期 ScrollViewer.Width/Height 的 NaN：Math.Max(0, NaN) = NaN，会让 ScrollToAsync 崩。
        if (!double.IsFinite(maxH)) maxH = 0;
        if (!double.IsFinite(maxV)) maxV = 0;

        // 用原生 ScrollViewer 范围而不是只凭模型来限制应用目标。越界时模型的 ActualSize（和画布 WidthRequest）同步长大，但原生内容异步重测，快速拖拽中原生范围可落后模型数千像素；
        // ChangeView 会夹到原生范围，基于模型的目标因此落不到原生边缘。若在模型边缘重锚，记账就跑到原生前面：内容钉在陈旧的原生边缘，等原生终于重测时一帧内前跳去追锚点。
        // 把应用目标夹到实时原生范围，每次 ChangeView 都精确落地，内容随重测平顺跟上（释放时也不会过冲）。
        var appliedOffsetX = WorkflowSurfaceMath.ClampValue(newOffsetX, 0, GetNativeScrollMaximum(state, horizontal: true));
        var appliedOffsetY = WorkflowSurfaceMath.ClampValue(newOffsetY, 0, GetNativeScrollMaximum(state, horizontal: false));

        if (layoutChanged)
        {
            // 在应用的（原生限定的）偏移处重锚 —— 绝不按模型边缘 —— 记账才正好落在内容本帧真正落地的位置。
            state.PanAccumulatedX = appliedOffsetX;
            state.PanAccumulatedY = appliedOffsetY;
            state.PanAnchorTotalX = totalX;
            state.PanAnchorTotalY = totalY;
        }

        // 装饰块不按请求目标写：ChangeView 即发即忘、可能落短（Windows 上原生 manipulation 还跟它抢），按请求目标写会让网格与内容脱节。
        // 原生 ViewChanged → Scrolled → OnScrolled → Refresh 路径每次移动都从实际落地位置写装饰块 —— 与平滑缩略图同一条单写入者路径。
        try
        {
            await state.ScrollViewer.ScrollToAsync(appliedOffsetX, appliedOffsetY, false);
        }
        catch (OperationCanceledException)
        {
            // 上一次滚动被更新的平移增量取代 —— 预期内。
            return;
        }

        if (!ct.IsCancellationRequested)
        {
            UpdateVisibleRegion(host, state);
        }
    }



    private static double GetHorizontalScrollMaximum(SurfaceState state)
    {
        // 从布局模型算最大滚动：画布尺寸由 ViewModel 绑定（Layout.ActualSize）驱动，ScrollView 内容就是跟随该绑定的 AbsoluteLayout，所以 ActualSize − 视口宽就是正确的滚动范围。
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

        // 关键：ScrollToAsync 完成后，MAUI 异步布局趟中 ScrollViewer 尺寸与滚动位置仍可能是 NaN/零。NaN 会经 Viewport → Virtualize → 空间索引传播，清空所有 VisibleItems（连线永久消失）；C# 里 NaN <= 0 为假，Virtualize 的防护挡不住。
        // 关键：一律从 ScrollX 写 —— 最近一次 ViewChanged 的原生偏移，即内容真正所在处；ChangeView 落短时请求目标会与 ScrollX 不同，按请求目标写正是释放后网格猛跳回真实位置的原因。
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

        // 持久化视口位置以熬过序列化往返。与其它各家和上面刚交给 Viewport 的值一样用世界坐标 —— 恢复路径按世界读回（ApplyPendingScrollRestoreCore → ToScreen），这里写原始滚动会在 ActualOffset 上被加两次。
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

        // 让虚拟化可见区修正与装饰器的浮动标尺带保持一致，标尺下方的节点才不会提前一个标尺厚度被剔除。
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

        // 用 IDispatcher 派发异步恢复，避免 async void；Task 即发即忘但不会静默吞异常。
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

            // 让出一拍，等本方法之前 ApplyLayout 引发的那趟布局落定，确保 ActualOffset 是最新的。
            await Task.Yield();

            var viewModel = ResolveTreeViewModel(host, state);
            if (viewModel is null)
            {
                return;
            }

            // ToScreen：screen = world + ActualOffset（待恢复视口是世界坐标）。
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

    // ── 组件自己转发输入（本家没有隧道路由相）────────────────────────────────────

    // 「这一笔输入已经路由过」的标记：本家没有隧道路由相，组件（节点/插槽）自己的处理器比链接层挂在
    // 交互源上的钩子更早跑（平台事件从命中元素往上冒泡，越深越早），所以句柄只能由先到者那次路由产出。
    // 同一笔再让后到者转发一次，宿主就会一笔听见两次，还会多收一对 Entered/Exited。
    // 按下的登记留到转发松手时才作废（平移在 Started 与第一帧读的是同一条），滚轮的由后到者读走。
    private static IWorkflowTreeViewModel? RoutedPressTree;
    private static WorkflowEventHandle? RoutedPressHandle;
    // 滚轮的登记按**事件对象**认，不按树：一次物理滚轮必然是新实例，所以没有谁需要负责收尾，也漏不到
    // 下一笔。按树登记时，普通滚轮只有链接层一条来者，登记没人消费就留到下一笔上，那笔会被判成「已经
    // 路由过」而静默丢掉（实测：三格滚轮到得了订阅者的行数与格数对不上）。
    private static object? RoutedWheelEvent;
    private static WorkflowEventHandle? RoutedWheelHandle;

    // 组件把落在自己身上的一笔按下转发出去：目标就是它自己（沿视图的 BindingContext 链认，含自身）。
    // 于是 WorkflowInput.Chain 里 slot → node → tree 那条链走得到 —— 只认连线的路由永远只答「连线或
    // 空白」，那两级是死代码。订阅者在组件自己的 InputRelay 上置 PreventDefault，就是「这一次别拖 / 别连」。
    // pointerInCanvas 是画布坐标系里的指针位置；量不到（非 Windows 的 Pan 手势不给位置）就退回组件
    // 自己的锚点。返回 null 表示找不到表面或树，调用方按「没人否决」继续。
    internal static WorkflowEventHandle? RouteComponentPress(View source, Point? pointerInCanvas)
    {
        if (FindAncestorContentView(source) is not { } host
            || host.GetValue(StateProperty) is not SurfaceState state
            || ResolveTreeViewModel(host, state) is not { } tree)
        {
            return null;
        }

        var input = WorkflowInput.For(tree);
        var target = ResolveTarget(source, tree, pointerInCanvas, input.HitRadius);
        var handle = new WorkflowEventHandle();

        input.Route(new Wf.PointerPressedEventArgs(
            AnchorFor(target, pointerInCanvas), ModifiersNow(), source, target,
            Wf.MouseButton.Left, 1, handle));

        RoutedPressTree = tree;
        RoutedPressHandle = handle;
        return handle;
    }

    // 一笔按下只转发一次：已经转发过（登记在案）就读现成的，否则自己转发并登记。链接层与平移都用它 ——
    // 两者谁先跑到由平台的钩子顺序决定，先到的那条转发，后到的那条只是读。
    internal static WorkflowEventHandle RoutePressOnce(IWorkflowTreeViewModel tree, Func<WorkflowEventHandle> route)
    {
        if (ReferenceEquals(RoutedPressTree, tree) && RoutedPressHandle is { } routed)
        {
            return routed;
        }

        var handle = route();
        RoutedPressTree = tree;
        RoutedPressHandle = handle;
        return handle;
    }

    // 只读这一笔按下的句柄（不转发、不登记）：非 Windows 的平移手势读的就是它 —— 那一笔由链接层
    // 转发过（PanUpdated 不给位置，手势自己转发不出来）。
    internal static WorkflowEventHandle? PeekRoutedPress(IWorkflowTreeViewModel tree)
        => ReferenceEquals(RoutedPressTree, tree) ? RoutedPressHandle : null;

    // 松手：这一笔按下的登记作废，下一次按下重新开始记。
    internal static void ClearRoutedPress(IWorkflowTreeViewModel tree)
    {
        if (!ReferenceEquals(RoutedPressTree, tree))
        {
            return;
        }

        RoutedPressTree = null;
        RoutedPressHandle = null;
    }

    // 滚轮：链接层与缩放两条路可能都跑在同一笔滚轮上，谁先到谁转发、后到的读现成的 —— 两份都要拿到同一个
    // 句柄。滚轮没有松手可挂，所以登记按**事件对象**认、由后到的那一条读走并清掉；只有一条来者时
    // （普通滚轮）它就自己留着，而下一笔是新实例，不会被它挡住。
    internal static WorkflowEventHandle RouteWheelOnce(object? eventKey, Func<WorkflowEventHandle> route)
    {
        if (ReferenceEquals(RoutedWheelEvent, eventKey) && RoutedWheelHandle is { } routed)
        {
            RoutedWheelEvent = null;
            RoutedWheelHandle = null;
            return routed;
        }

        var handle = route();
        RoutedWheelEvent = eventKey;
        RoutedWheelHandle = handle;
        return handle;
    }

    // 指针底下是什么：沿命中视图及其可视祖先找 BindingContext 是节点/插槽的那个（含自身），找不到
    // 再回退到共享的曲线判定。只认连线的话，路由的 Target 就永远只是「连线或空白」。
    internal static IWorkflowViewModel? ResolveTarget(
        Element? hit, IWorkflowTreeViewModel tree, Point? pointerInCanvas, double radius)
    {
        for (var current = hit; current is not null; current = current.Parent)
        {
            switch (current.BindingContext)
            {
                case IWorkflowNodeViewModel node:
                    return node;
                case IWorkflowSlotViewModel slot:
                    return slot;
            }
        }

        return pointerInCanvas is { } point
            ? tree.HitTestVisibleLinks(point.X, point.Y, radius)
            : null;
    }

    // 转发用的锚点：量到指针就用指针（画布坐标系，与插槽布局、平移同一条），量不到就用组件自己的。
    private static Anchor AnchorFor(IWorkflowViewModel? target, Point? pointerInCanvas)
    {
        if (pointerInCanvas is { } point)
        {
            return new Anchor(point.X, point.Y, 0);
        }

        return target switch
        {
            IWorkflowNodeViewModel node => node.Anchor,
            IWorkflowSlotViewModel slot => slot.Anchor,
            _ => new Anchor(),
        };
    }

    // 平台事件自带的修饰键逐个映射。只在本家 Windows 头可用 —— 那一头的 PointerRoutedEventArgs 带 KeyModifiers
    // （与 WinUI 同形）；KeyRoutedEventArgs 不带，走 ModifiersNow()。
#if WINDOWS
    internal static Wf.InputModifiers Modifiers(Windows.System.VirtualKeyModifiers keys)
    {
        var modifiers = Wf.InputModifiers.None;
        if ((keys & Windows.System.VirtualKeyModifiers.Menu) != 0) modifiers |= Wf.InputModifiers.Alt;
        if ((keys & Windows.System.VirtualKeyModifiers.Control) != 0) modifiers |= Wf.InputModifiers.Control;
        if ((keys & Windows.System.VirtualKeyModifiers.Shift) != 0) modifiers |= Wf.InputModifiers.Shift;
        if ((keys & Windows.System.VirtualKeyModifiers.Windows) != 0) modifiers |= Wf.InputModifiers.Meta;
        return modifiers;
    }
#endif

    // 不带修饰键状态的输入（指针悬停/按下、组件转发）只能问当前线程的键盘状态。Windows 头与 WinUI 同源
    // （Microsoft.UI.Input 现取）；其余平台 MAUI 没有跨平台键态 API（PointerEventArgs 只有 Button/PlatformArgs），
    // Android/iOS/MacCatalyst 无从现取，返回 None。
    internal static Wf.InputModifiers ModifiersNow()
    {
#if WINDOWS
        var modifiers = Wf.InputModifiers.None;
        if (IsDown(Windows.System.VirtualKey.Menu)) modifiers |= Wf.InputModifiers.Alt;
        if (IsDown(Windows.System.VirtualKey.Control)) modifiers |= Wf.InputModifiers.Control;
        if (IsDown(Windows.System.VirtualKey.Shift)) modifiers |= Wf.InputModifiers.Shift;
        if (IsDown(Windows.System.VirtualKey.LeftWindows) || IsDown(Windows.System.VirtualKey.RightWindows)) modifiers |= Wf.InputModifiers.Meta;
        return modifiers;

        static bool IsDown(Windows.System.VirtualKey key)
            => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
#else
        return Wf.InputModifiers.None;
#endif
    }

    // 组件量指针位置用：指针要换算到画布元素自己的坐标系，组件才有和内部各处一致的那个点。
    internal static VisualElement? ResolveCanvasForRouting(View source)
        => FindAncestorContentView(source) is { } host && host.GetValue(StateProperty) is SurfaceState state
            ? state.Canvas
            : null;
}
