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
        public ScrollViewer? ScrollViewer { get; set; }
        public Canvas? Canvas { get; set; }
        public FrameworkElement? GridDecorator { get; set; }
        public FrameworkElement? MinimapOverlay { get; set; }
        public FrameworkElement? PointerPressSource { get; set; }
        public PointerEventHandler? ZoomHandler { get; set; }

        // 这一笔按下已被路由过（组件自己路由的，或表面在平移那一处路由的）。
        // 不能用 e.Handled 代替：表面的按下处理器是 handledEventsToo:true 挂的，它无视 Handled 照跑，
        // 于是同一笔会被路由第二遍。这个标记是那一笔「已经路由」的唯一凭据。
        public bool PressRouted { get; set; }

        // 连线右键菜单：菜单由模板声明（条目归用户，见 LinkMenuKeyProperty），订阅、定位、弹出、挂起都在这里。
        public MenuFlyout? LinkMenu { get; set; }
        public IWorkflowLinkViewModel? MenuLink { get; set; }

        // 这棵树的输入路由：菜单开着时由它挂起指针跟踪，接线的那两个订阅也从它来。
        public WorkflowInput? Input { get; set; }
        public EventHandler<Wf.PointerPressedEventArgs>? MenuPressed { get; set; }
        public EventHandler<IWorkflowLinkViewModel>? MenuLinkRemoved { get; set; }
        public EventHandler<object>? MenuOpened { get; set; }
        public EventHandler<object>? MenuClosed { get; set; }

        // 上一棵被挂上来的树（引用比较）。恢复只因「换了树」触发一次，之后的 Refresh 不再把用户滚回去。
        public IWorkflowTreeViewModel? LastRestoreTree { get; set; }
        public bool HasPendingRestore { get; set; }
        public double PendingRestoreX { get; set; }
        public double PendingRestoreY { get; set; }
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

        viewer.ChangeView(
            WorkflowSurfaceMath.ClampValue(state.PendingRestoreX, 0, GetHorizontalScrollMaximum(viewer)),
            WorkflowSurfaceMath.ClampValue(state.PendingRestoreY, 0, GetVerticalScrollMaximum(viewer)),
            null,
            disableAnimation: true);
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

        // 这一家没有可设的 `Focusable`（WinUI 是 `IsTabStop` + `Focus(FocusState)`）：宿主本来就能拿焦点，
        // 悬停时 `Focus(FocusState.Pointer)` 收得回来，Delete 的按键事件因此经过它。

        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.DataContextChanged += OnDataContextChanged;
        control.PointerMoved += OnPointerMoved;
        control.PointerEntered += OnPointerEntered;
        control.PointerExited += OnPointerExited;
        control.PointerWheelChanged += OnLinkPointerWheel;
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
        control.PointerWheelChanged -= OnLinkPointerWheel;
        control.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased));
        control.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLinkPointerPressed));
        control.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnLinkKeyDown));
        control.RemoveHandler(UIElement.KeyUpEvent, new KeyEventHandler(OnLinkKeyUp));

        if (control.GetValue(StateProperty) is SurfaceState state)
        {
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
            Refresh(control);
        }
    }

    private static void ResolveNamedControls(UserControl control, SurfaceState state)
    {
        UnsubscribeResolvedControls(state);

        var scrollViewerName = GetScrollViewerName(control);
        if (!string.IsNullOrWhiteSpace(scrollViewerName))
        {
            state.ScrollViewer = control.FindName(scrollViewerName) as ScrollViewer;
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

        if (GetZoomEnabled(control))
        {
            HookZoom(state);
        }
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

        UnhookZoom(state);
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

        if (Equals(e.NewValue, true))
        {
            HookZoom(state);
        }
        else
        {
            UnhookZoom(state);
        }
    }

    // WinUI 没有 PreviewMouseWheel，所以滚轮在 SCROLLVIEWER 上处理（它始终位于其全部内容的冒泡路径上）。用 handledEventsToo:true 挂接，节点先处理了滚轮也仍触发。
    // Ctrl+滚轮在处理器跑之前可能还会滚一丁点 —— 缩放到处都还是会应用（改挂画布则只覆盖其可命中区域）。
    private static void HookZoom(SurfaceState state)
    {
        if (state.ScrollViewer is not null && state.ZoomHandler is null)
        {
            state.ZoomHandler = new PointerEventHandler(OnZoomPointerWheelChanged);
            state.ScrollViewer.AddHandler(UIElement.PointerWheelChangedEvent, state.ZoomHandler, true);
        }
    }

    private static void UnhookZoom(SurfaceState state)
    {
        if (state.ScrollViewer is not null && state.ZoomHandler is not null)
        {
            state.ScrollViewer.RemoveHandler(UIElement.PointerWheelChangedEvent, state.ZoomHandler);
            state.ZoomHandler = null;
        }
    }

    private static void OnZoomPointerWheelChanged(object sender, PointerRoutedEventArgs e)
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

        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            return;
        }

        var delta = e.GetCurrentPoint(source as UIElement ?? host).Properties.MouseWheelDelta;

        // Ctrl+滚轮也进路由，订阅者才有机会说「这一次别缩放」。不会造成二次路由：非 Ctrl 的滚轮走
        // OnLinkPointerWheel 那一条，两支笔各走各的。
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
                sv.HorizontalOffset, sv.VerticalOffset, sv.ViewportWidth, sv.ViewportHeight, layout);
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
            sv.ChangeView(committedX, committedY, null, disableAnimation: true);

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
                VirtualizeAtScroll(viewModel, elseViewer.HorizontalOffset, elseViewer.VerticalOffset,
                    elseViewer.ViewportWidth, elseViewer.ViewportHeight);
            }
        }
        e.Handled = true;
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
        state.PanStartOffset = new Windows.Foundation.Point(state.ScrollViewer.HorizontalOffset, state.ScrollViewer.VerticalOffset);
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
            state.ScrollViewer!.HorizontalOffset + point.X - canvasTranslateX,
            state.ScrollViewer!.VerticalOffset + point.Y - canvasTranslateY,
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

    // 滚轮也进输入面（缩放那条路是 Ctrl+滚轮，挂在 ScrollViewer 上，两者不重叠）。
    private static void OnLinkPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.ScrollViewer is null)
        {
            return;
        }

        if (Modifiers(e.KeyModifiers).HasFlag(Wf.InputModifiers.Control))
        {
            return;
        }

        var delta = e.GetCurrentPoint(host).Properties.MouseWheelDelta;
        RoutePointer(state, viewModel, e, host, e.OriginalSource as DependencyObject,
            (position, target, handle) => new Wf.PointerWheelEventArgs(position, Modifiers(e.KeyModifiers), host, target, 0d, delta, handle));
    }

    // 按下：转发给输入面裁决（是否落在某条连线上、哪个键）。不置 Handled —— 画布手势照旧。
    private static void OnLinkPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
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

        // 命中一条线就把焦点收到本宿主：Delete 要的按键事件经过它，悬停才删得掉。
        if (WorkflowInput.For(viewModel).HoveredLink is not null)
        {
            host.Focus(FocusState.Pointer);
        }
    }

    // Delete 归 Core：本层只把按键翻译过去，由它决定「现在指针停着的哪条线」要删。
    private static void OnLinkKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var input = WorkflowInput.For(viewModel);
        if (input.HoveredLink is null)
        {
            return;
        }

        input.Route(new Wf.KeyDownEventArgs(
            ToKey(e.Key), (int)e.Key, KeyModifiersNow(), false, host, input.HoveredLink, new WorkflowEventHandle()));
        e.Handled = true;
    }

    private static void OnLinkKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var input = WorkflowInput.For(viewModel);
        input.Route(new Wf.KeyUpEventArgs(
            ToKey(e.Key), (int)e.Key, KeyModifiersNow(), false, host, input.HoveredLink, new WorkflowEventHandle()));
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

        // 悬停到连线上就把焦点收到宿主：Delete 要的按键事件经过它，而「悬停（不点）就能删」是契约。
        if (input.HoveredLink is not null)
        {
            host.Focus(FocusState.Pointer);
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

        state.ScrollViewer.ChangeView(appliedOffsetX, appliedOffsetY, null, true);
        state.PanStart = current;
        state.PanStartOffset = new Windows.Foundation.Point(appliedOffsetX, appliedOffsetY);
        UpdateVisibleRegion(host, state);
        e.Handled = true;
    }

    private static void ApplyLayout(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
        {
            return;
        }

        var transform = new TranslateTransform
        {
            X = viewModel.Layout.ActualOffset.Horizontal,
            Y = viewModel.Layout.ActualOffset.Vertical
        };

        // WinUI 子元素（不同于 WPF）不把 RenderTransform 绑到 CanvasTransformBehavior.Transform；直接经合成变换把偏移应用到画布（布局感知的做法）。
        state.Canvas.Translation = new System.Numerics.Vector3(
            (float)viewModel.Layout.ActualOffset.Horizontal,
            (float)viewModel.Layout.ActualOffset.Vertical,
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
        var viewportX = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.HorizontalOffset, viewModel.Layout.ActualOffset.Horizontal);
        var viewportY = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.VerticalOffset, viewModel.Layout.ActualOffset.Vertical);
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

        decorator.ScrollOffsetX = state.ScrollViewer.HorizontalOffset;
        decorator.ScrollOffsetY = state.ScrollViewer.VerticalOffset;
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

        minimap.ScrollOffsetX = state.ScrollViewer.HorizontalOffset;
        minimap.ScrollOffsetY = state.ScrollViewer.VerticalOffset;
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
