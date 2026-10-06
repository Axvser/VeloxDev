using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PlatformInput = System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors
{
public sealed class WorkflowSurfaceBehavior : DependencyObject
{
    private static readonly MouseButtonEventHandler MouseUpHandler = OnMouseUp;

    private sealed class SurfaceState
    {
        public bool IsPanning { get; set; }
        public Point PanStart { get; set; }
        public Vector PanStartOffset { get; set; }
        public ScrollViewer? ScrollViewer { get; set; }
        public Canvas? Canvas { get; set; }
        public FrameworkElement? GridDecorator { get; set; }
        public FrameworkElement? MinimapOverlay { get; set; }
        public FrameworkElement? PointerPressSource { get; set; }

        // 宿主本身：悬停焦点在连线可视对象不可聚焦时的落点（模板/Trimmed 的连线视图就是这种）。
        public UserControl? Host { get; set; }

        // 当前持有键盘焦点的连线可视对象（悬停焦点）。只在换了对象时才 Focus，避免每帧重复取焦点。
        public IInputElement? HoverFocus { get; set; }

        // 连线右键菜单：菜单由模板声明（条目归用户，见 LinkMenuKeyProperty），订阅、定位、弹出、挂起都在这里。
        public ContextMenu? LinkMenu { get; set; }
        public IWorkflowLinkViewModel? MenuLink { get; set; }

        // 这棵树的输入路由：菜单开着时由它挂起指针跟踪，接线的那两个订阅也从它来。
        public WorkflowInput? Input { get; set; }
        public EventHandler<Wf.PointerPressedEventArgs>? MenuPressed { get; set; }
        public EventHandler<IWorkflowLinkViewModel>? MenuLinkRemoved { get; set; }
        public RoutedEventHandler? MenuOpened { get; set; }
        public RoutedEventHandler? MenuClosed { get; set; }

        // 上一棵被挂上来的树（引用比较）。恢复只因「换了树」触发一次，之后的 Refresh 不再把用户滚回去。
        public IWorkflowTreeViewModel? LastRestoreTree { get; set; }
        public bool HasPendingRestore { get; set; }
        public bool RestoreQueued { get; set; }
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
    /// <see cref="ContextMenu"/> resource under that key, put its items in it, and name the key here; the surface
    /// resolves which link, positions the menu, opens it, and suspends the tree's <see cref="WorkflowInput"/> while it is open.
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

    public static string? GetScrollViewerName(DependencyObject element) => (string?)element.GetValue(ScrollViewerNameProperty);
    public static void SetScrollViewerName(DependencyObject element, string? value) => element.SetValue(ScrollViewerNameProperty, value);

    public static string? GetCanvasName(DependencyObject element) => (string?)element.GetValue(CanvasNameProperty);
    public static void SetCanvasName(DependencyObject element, string? value) => element.SetValue(CanvasNameProperty, value);

    public static string? GetGridDecoratorName(DependencyObject element) => (string?)element.GetValue(GridDecoratorNameProperty);
    public static void SetGridDecoratorName(DependencyObject element, string? value) => element.SetValue(GridDecoratorNameProperty, value);

    public static string? GetPointerPressSourceName(DependencyObject element) => (string?)element.GetValue(PointerPressSourceNameProperty);
    public static void SetPointerPressSourceName(DependencyObject element, string? value) => element.SetValue(PointerPressSourceNameProperty, value);

    public static string? GetMinimapOverlayName(DependencyObject element) => (string?)element.GetValue(MinimapOverlayNameProperty);
    public static void SetMinimapOverlayName(DependencyObject element, string? value) => element.SetValue(MinimapOverlayNameProperty, value);

    public static bool GetZoomEnabled(DependencyObject element) => (bool)element.GetValue(ZoomEnabledProperty);
    public static void SetZoomEnabled(DependencyObject element, bool value) => element.SetValue(ZoomEnabledProperty, value);

    public static string? GetLinkMenuKey(DependencyObject element) => (string?)element.GetValue(LinkMenuKeyProperty);
    public static void SetLinkMenuKey(DependencyObject element, string? value) => element.SetValue(LinkMenuKeyProperty, value);

    public static void Refresh(UserControl host)
    {
        if (!GetIsEnabled(host))
        {
            return;
        }

        var state = (SurfaceState?)host.GetValue(StateProperty) ?? new SurfaceState();
        host.SetValue(StateProperty, state);
        ResolveNamedControls(host, state);
        WireLinkMenu(host, state);
        CaptureViewportRestore(host, state);
        ApplyLayout(host, state);
        UpdateVisibleRegion(host, state);
        QueueViewportRestore(host, state);
    }

    // 连线右键菜单：**条目由模板声明**（资源键挂在 LinkMenuKeyProperty 上），**接线在这里** —— 订输入面、
    // 定位、弹出、挂起指针跟踪，模板因此没有一行交互代码。菜单指着的那条线离树时树会报 LinkRemoved，这里收自己那份。
    private static void WireLinkMenu(UserControl host, SurfaceState state)
    {
        // 资源在 attach 之后才一定就绪（第一次 Refresh 可能早于 Resources 解析完），所以每次 Refresh 都重查一次。
        var key = GetLinkMenuKey(host);
        ContextMenu? menu = null;
        if (!string.IsNullOrWhiteSpace(key) && host.TryFindResource(key) is ContextMenu found)
        {
            menu = found;
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
            if (state.LinkMenu is { } open) open.IsOpen = false;
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

    // 右键落在表面上，而弹出要屏幕坐标；只有表面同时知道画布与屏幕两件事，所以菜单由表面弹。
    private static void ShowLinkMenu(UserControl host, SurfaceState state, Wf.PointerPressedEventArgs e)
    {
        // 只有右键、且落在连线上才弹：空白画布没有可操作的对象。
        if (e.Button != Wf.MouseButton.Right) return;
        if (e.Target is not IWorkflowLinkViewModel link) return;
        if (state.LinkMenu is null || state.Canvas is null) return;
        if (host.DataContext is not IWorkflowTreeViewModel tree) return;

        // 链上更靠前的一级（连线自己）可以否决这次按下 —— 它说不给菜单，这里就不给。
        if (e.Handle.PreventDefault) return;

        state.MenuLink = link;

        // 菜单的 DataContext 就是这条连线，条目据此绑定命令。
        state.LinkMenu.DataContext = link;

        // 画布坐标 → 设备坐标：先按适配器那套逆变换（world + ActualOffset）回到画布局部，再由画布换到屏幕；
        // AbsolutePoint 用 DIP，所以最后按 DPI 折回去。
        var local = WorkflowSurfaceMath.ToScreen(e.Position.Horizontal, e.Position.Vertical, tree.Layout);
        var device = state.Canvas.PointToScreen(new Point(local.Horizontal, local.Vertical));
        // netframework4.6.1 没有 VisualTreeHelper.GetDpi，退回呈现源的设备变换，两者给的是同一个缩放。
#if NETFRAMEWORK
        var toDevice = PresentationSource.FromVisual(state.Canvas)?.CompositionTarget?.TransformToDevice;
        var scaleX = toDevice?.M11 ?? 1d;
        var scaleY = toDevice?.M22 ?? 1d;
#else
        var dpi = VisualTreeHelper.GetDpi(state.Canvas);
        var scaleX = dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY;
#endif

        state.LinkMenu.PlacementTarget = host;
        state.LinkMenu.Placement = PlacementMode.AbsolutePoint;
        state.LinkMenu.HorizontalOffset = device.X / scaleX;
        state.LinkMenu.VerticalOffset = device.Y / scaleY;
        state.LinkMenu.IsOpen = true;
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
        host.Dispatcher.BeginInvoke(() =>
        {
            state.RestoreQueued = false;

            if (!state.HasPendingRestore || !GetIsEnabled(host) || state.ScrollViewer is not { } viewer)
                return;

            state.HasPendingRestore = false;

            var maxH = GetHorizontalScrollMaximum(viewer);
            var maxV = GetVerticalScrollMaximum(viewer);
            viewer.ScrollToHorizontalOffset(WorkflowSurfaceMath.ClampValue(state.PendingRestoreX, 0, maxH));
            viewer.ScrollToVerticalOffset(WorkflowSurfaceMath.ClampValue(state.PendingRestoreY, 0, maxV));

            // 恢复后的位置立刻写回模型，免得控件与 Layout.ViewportOffset 各说各话。
            UpdateVisibleRegion(host, state);
        }, DispatcherPriority.Loaded);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UserControl control)
        {
            return;
        }

        if (Equals(e.NewValue, true))
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
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.DataContextChanged += OnDataContextChanged;
        control.PreviewMouseMove += OnPreviewMouseMove;
        control.PreviewMouseDown += OnLinkPointerPressed;
        control.MouseUp += OnLinkPointerReleased;
        control.MouseEnter += OnLinkPointerEntered;
        control.MouseLeave += OnLinkPointerExited;
        control.MouseWheel += OnLinkPointerWheel;
        control.KeyDown += OnLinkKeyDown;
        control.KeyUp += OnLinkKeyUp;
        control.AddHandler(UIElement.MouseUpEvent, MouseUpHandler, true);
        ResolveNamedControls(control, state);
        Refresh(control);
    }

    private static void Detach(UserControl control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.DataContextChanged -= OnDataContextChanged;
        control.PreviewMouseMove -= OnPreviewMouseMove;
        control.PreviewMouseDown -= OnLinkPointerPressed;
        control.MouseUp -= OnLinkPointerReleased;
        control.MouseEnter -= OnLinkPointerEntered;
        control.MouseLeave -= OnLinkPointerExited;
        control.MouseWheel -= OnLinkPointerWheel;
        control.KeyDown -= OnLinkKeyDown;
        control.KeyUp -= OnLinkKeyUp;
        control.RemoveHandler(UIElement.MouseUpEvent, MouseUpHandler);

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

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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
            state.PointerPressSource.PreviewMouseDown += OnPointerPressed;
            state.PointerPressSource.PreviewMouseLeftButtonUp += OnSurfaceMouseLeftButtonUp;
        }

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.ScrollChanged += OnScrollChanged;
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
            state.PointerPressSource.PreviewMouseDown -= OnPointerPressed;
            state.PointerPressSource.PreviewMouseLeftButtonUp -= OnSurfaceMouseLeftButtonUp;
        }

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.ScrollChanged -= OnScrollChanged;
            state.ScrollViewer.PreviewMouseWheel -= OnZoomPreviewMouseWheel;
        }

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

    private static void HookZoom(SurfaceState state)
    {
        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.PreviewMouseWheel += OnZoomPreviewMouseWheel;
        }
    }

    private static void UnhookZoom(SurfaceState state)
    {
        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.PreviewMouseWheel -= OnZoomPreviewMouseWheel;
        }
    }

    private static void OnZoomPreviewMouseWheel(object sender, MouseWheelEventArgs e)
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

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        // 滚轮向上（增量为正）放大：Scale 是折叠因子，放大要除以 1/1.1。
        var factor = e.Delta > 0 ? 1 / 1.1 : 1.1;
        var next = Math.Max(0.1, Math.Min(10, viewModel.Layout.Scale.Horizontal * factor));
        var layout = viewModel.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter
            && host.GetValue(StateProperty) is SurfaceState state
            && state.ScrollViewer is { } sv)
        {
            // 捕获视口中心下方的世界点，绕它折叠节点，再滚动让该点不动：缩放时视口中心附近的节点保持可见。
            // 缩放不动画布几何（ActualOffset == NegativeOffset，固定）——只动滚动，画布位置因此永不抖动。
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                sv.HorizontalOffset, sv.VerticalOffset, sv.ViewportWidth, sv.ViewportHeight, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);

            // 深度放大把负向内容折叠越过固定画布平移（ActualOffset == NegativeOffset）；必须在 ApplyLayout 采纳新偏移前扩大覆盖。
            // 下面的 PivotCenterScroll 会读到长大的偏移，多出的覆盖被滚动目标吸收、枢轴保持居中 —— 无需手动增量。只有正向内容时无事发生。
            WorkflowSurfaceMath.EnsureNegativeCover(viewModel);

            // 让 ScrollViewer 在读取最大值前采纳（可能自动延伸的）范围。比例低于 1 的放大让画布长大、缩小可能让它变小；夹取必须看到落定的范围，否则枢轴偏离中心、下一格又重捕漂移。
            ApplyLayout(host, state);
            state.Canvas?.UpdateLayout();
            sv.UpdateLayout();
            host.UpdateLayout();

            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, sv.ViewportWidth, sv.ViewportHeight);
            var maxH = GetHorizontalScrollMaximum(sv);
            var maxV = GetVerticalScrollMaximum(sv);

            // 越界扩展画布（与拖过边界同一机制），让枢轴总能到达；单纯夹取会把枢轴推离中心并逐格漂移。
            var newX = WorkflowSurfaceMath.ClampScrollOffset(tx, maxH, layout, horizontal: true);
            var newY = WorkflowSurfaceMath.ClampScrollOffset(ty, maxV, layout, horizontal: false);
            if (Math.Abs(newX - tx) > double.Epsilon || Math.Abs(newY - ty) > double.Epsilon)
            {
                // 扩展改变了范围；重应用并重读最大值，tx/ty 才落在其内。
                ApplyLayout(host, state);
                state.Canvas?.UpdateLayout();
                sv.UpdateLayout();
                host.UpdateLayout();
                maxH = GetHorizontalScrollMaximum(sv);
                maxV = GetVerticalScrollMaximum(sv);
            }

            sv.ScrollToHorizontalOffset(WorkflowSurfaceMath.ClampValue(tx, 0, maxH));
            sv.ScrollToVerticalOffset(WorkflowSurfaceMath.ClampValue(ty, 0, maxV));
        }
        else
        {
            layout.Scale = new Scale(next, next);
            // 世界原点缩放：只有重应用布局时画布平移才变，所以覆盖长大后把新偏移走视口居中分支同样的布局趟推出去（trimmed 示例是视口居中，此路径通常休眠）。
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
        }
        e.Handled = true;
    }

    private static void OnPointerPressed(object sender, MouseButtonEventArgs e)
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

        if (e.ChangedButton != PlatformInput.MouseButton.Left)
        {
            return;
        }

        // 只有点击落在空白背景（非节点/插槽/连线或其它交互元素）时才起画布平移。
        if (e.OriginalSource is not DependencyObject originalSource
            || !IsSurfaceBlankInteraction(originalSource, state))
        {
            return;
        }

        state.IsPanning = true;
        state.PanStart = e.GetPosition(host);
        state.PanStartOffset = new Vector(state.ScrollViewer.HorizontalOffset, state.ScrollViewer.VerticalOffset);
        Mouse.Capture(source);
        e.Handled = true;
    }

    private static void OnSurfaceMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement source)
        {
            return;
        }

        var host = EnumerateVisualAncestors(source).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || !viewModel.VirtualLink.IsVisible)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject originalSource)
        {
            return;
        }

        if (!IsSurfaceBlankInteraction(originalSource, state))
        {
            return;
        }

        viewModel.ResetVirtualLinkCommand.Execute(null);
        e.Handled = true;
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
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

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
        {
            return;
        }

        var point = e.GetPosition(state.Canvas);
        viewModel.SetPointerCommand.Execute(WorkflowSurfaceMath.ToWorldAnchor(point.X, point.Y, 0, viewModel.Layout));

        // 拉线时指针下正挂着橡皮筋，那段时间不转发，否则沿途实连线会一路亮起。
        if (!viewModel.VirtualLink.IsVisible)
        {
            RoutePointer(state, viewModel, point, host,
                (anchor, target, handle) => new Wf.PointerMovedEventArgs(anchor, Modifiers(), host, target, handle));
        }
    }

    // 悬停到连线上的指针消息：进入与移动走同一条翻译 —— 输入面只看点在哪，不看事件叫什么。
    private static void OnLinkPointerEntered(object sender, MouseEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (state.Canvas is null
            || host.DataContext is not IWorkflowTreeViewModel viewModel
            || viewModel.VirtualLink.IsVisible)
        {
            return;
        }

        RoutePointer(state, viewModel, e.GetPosition(state.Canvas), host,
            (anchor, target, handle) => new Wf.PointerEnteredEventArgs(anchor, Modifiers(), host, target, handle));
    }

    // 指针离开整块输入面：指针目标跟着走。菜单弹出引起的那一次离开由输入面按 IsSuspended 认出并忽略。
    private static void OnLinkPointerExited(object sender, MouseEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var input = WorkflowInput.For(viewModel);
        input.Route(new Wf.PointerExitedEventArgs(new Anchor(), Modifiers(), host, null, new WorkflowEventHandle()));
        FocusHoveredLink(input, state);
    }

    // 按下哪条线由这里判（对着发布出去的曲线），输入面只负责把这次按下发给那一条与其祖先，不置 Handled ——
    // 平移与端口拖线仍要照常收到这次按下。
    private static void OnLinkPointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (state.Canvas is null
            || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var button = ToButton(e.ChangedButton);
        if (button == Wf.MouseButton.None)
        {
            return;
        }

        RoutePointer(state, viewModel, e.GetPosition(state.Canvas), host,
            (anchor, target, handle) => new Wf.PointerPressedEventArgs(
                anchor, Modifiers(), host, target, button, e.ClickCount, handle));
    }

    private static void OnLinkPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (state.Canvas is null
            || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var button = ToButton(e.ChangedButton);
        if (button == Wf.MouseButton.None)
        {
            return;
        }

        RoutePointer(state, viewModel, e.GetPosition(state.Canvas), host,
            (anchor, target, handle) => new Wf.PointerReleasedEventArgs(
                anchor, Modifiers(), host, target, button, e.ClickCount, handle));
    }

    // 滚轮也进输入面（缩放那条路是 Ctrl+滚轮，归 ScrollViewer 的预览事件，两者不重叠）。
    private static void OnLinkPointerWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (state.Canvas is null
            || host.DataContext is not IWorkflowTreeViewModel viewModel
            || Keyboard.Modifiers == ModifierKeys.Control)
        {
            return;
        }

        RoutePointer(state, viewModel, e.GetPosition(state.Canvas), host,
            (anchor, target, handle) => new Wf.PointerWheelEventArgs(
                anchor, Modifiers(), host, target, 0d, e.Delta, handle));
    }

    // 指针进来时统一在这里翻译：Anchor 带上来源视图所在图层当 Z，被指到的那条线由 Core 那条共享的曲线命中判出来 ——
    // 表面一个人画完所有线、没有「每线的可视对象」，所以命中只能对着发布出去的曲线做。
    private static void RoutePointer(
        SurfaceState state, IWorkflowTreeViewModel tree, Point point, FrameworkElement source,
        Func<Anchor, IWorkflowViewModel?, WorkflowEventHandle, Wf.PointerEventArgs> args)
    {
        var anchor = WorkflowSurfaceMath.ToWorldAnchor(point.X, point.Y, Panel.GetZIndex(source), tree.Layout);
        var input = WorkflowInput.For(tree);
        var target = tree.HitTestVisibleLinks(anchor.Horizontal, anchor.Vertical, input.HitRadius);

        input.Route(args(anchor, target, new WorkflowEventHandle()));
        FocusHoveredLink(input, state);
    }

    private static Wf.MouseButton ToButton(PlatformInput.MouseButton button) => button switch
    {
        PlatformInput.MouseButton.Left => Wf.MouseButton.Left,
        PlatformInput.MouseButton.Right => Wf.MouseButton.Right,
        PlatformInput.MouseButton.Middle => Wf.MouseButton.Middle,
        PlatformInput.MouseButton.XButton1 => Wf.MouseButton.XButton1,
        PlatformInput.MouseButton.XButton2 => Wf.MouseButton.XButton2,
        _ => Wf.MouseButton.None,
    };

    private static Wf.InputModifiers Modifiers()
    {
        var keys = Keyboard.Modifiers;
        var modifiers = Wf.InputModifiers.None;
        if ((keys & ModifierKeys.Alt) != 0) modifiers |= Wf.InputModifiers.Alt;
        if ((keys & ModifierKeys.Control) != 0) modifiers |= Wf.InputModifiers.Control;
        if ((keys & ModifierKeys.Shift) != 0) modifiers |= Wf.InputModifiers.Shift;
        if ((keys & ModifierKeys.Windows) != 0) modifiers |= Wf.InputModifiers.Meta;
        return modifiers;
    }

    // 键按字母/数字/功能键三段连续区间做算术映射（两边枚举的这几段都是连续的），其余逐个点名，没点到的报 Unknown。
    private static Wf.InputKey ToKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return Wf.InputKey.A + (key - Key.A);
        if (key >= Key.D0 && key <= Key.D9) return Wf.InputKey.D0 + (key - Key.D0);
        if (key >= Key.F1 && key <= Key.F12) return Wf.InputKey.F1 + (key - Key.F1);

        return key switch
        {
            Key.None => Wf.InputKey.None,
            Key.Cancel => Wf.InputKey.Cancel,
            Key.Back => Wf.InputKey.Back,
            Key.Tab => Wf.InputKey.Tab,
            Key.Enter => Wf.InputKey.Enter,
            Key.Escape => Wf.InputKey.Escape,
            Key.Space => Wf.InputKey.Space,
            Key.PageUp => Wf.InputKey.PageUp,
            Key.PageDown => Wf.InputKey.PageDown,
            Key.End => Wf.InputKey.End,
            Key.Home => Wf.InputKey.Home,
            Key.Left => Wf.InputKey.Left,
            Key.Up => Wf.InputKey.Up,
            Key.Right => Wf.InputKey.Right,
            Key.Down => Wf.InputKey.Down,
            Key.Insert => Wf.InputKey.Insert,
            Key.Delete => Wf.InputKey.Delete,
            _ => Wf.InputKey.Unknown,
        };
    }

    // 悬停把键盘焦点交给能接住它的东西：Delete 才能沿焦点所在子树冒泡到宿主的键路由。
    // 优先交给画出那条线的控件；它不可聚焦时（**模板与 Trimmed 的连线视图默认就是**）退回宿主本身 ——
    // 否则「悬停 + Delete」在生成出来的工程里根本没有路由，而且不报错。
    // 节点卡里的输入框自己处理 Delete 时事件已被标记、冒泡到宿主前就被吃掉，编辑文本不受影响。
    private static void FocusHoveredLink(WorkflowInput input, SurfaceState state)
    {
        var hovered = input.HoveredLink?.HitTarget()?.Visual as UIElement;
        IInputElement? target = hovered is null
            ? null
            : hovered is { Focusable: true } ? hovered : state.Host;

        if (ReferenceEquals(state.HoverFocus, target))
        {
            return;
        }

        state.HoverFocus = target;
        target?.Focus();
    }

    // Delete 走冒泡而不是隧穿：聚焦的输入框先吃掉它改自己的光标时必须让它赢。
    // 键没有坐标，目标就是指针停着的那条线 —— 由适配器交给输入面。
    private static void OnLinkKeyDown(object sender, PlatformInput.KeyEventArgs e)
    {
        if (sender is not UserControl host
            || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var input = WorkflowInput.For(viewModel);
        if (input.HoveredLink is null)
        {
            return;
        }

        input.Route(new Wf.KeyDownEventArgs(
            ToKey(e.Key), (int)e.Key, Modifiers(), e.IsRepeat, host, input.HoveredLink, new WorkflowEventHandle()));
        e.Handled = true;
    }

    private static void OnLinkKeyUp(object sender, PlatformInput.KeyEventArgs e)
    {
        if (sender is not UserControl host
            || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var input = WorkflowInput.For(viewModel);
        input.Route(new Wf.KeyUpEventArgs(
            ToKey(e.Key), (int)e.Key, Modifiers(), e.IsRepeat, host, input.HoveredLink, new WorkflowEventHandle()));
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (state.IsPanning)
        {
            state.IsPanning = false;
            Mouse.Capture(null);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != PlatformInput.MouseButton.Left)
        {
            return;
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
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

    private static void OnCanvasPanMoved(UserControl host, SurfaceState state, MouseEventArgs e)
    {
        if (!state.IsPanning || state.ScrollViewer is null || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            state.IsPanning = false;
            if (Mouse.Captured is not null)
            {
                Mouse.Capture(null);
            }

            return;
        }

        var current = e.GetPosition(host);
        var desiredX = state.PanStartOffset.X + (state.PanStart.X - current.X);
        var desiredY = state.PanStartOffset.Y + (state.PanStart.Y - current.Y);
        var maxH = GetHorizontalScrollMaximum(state.ScrollViewer);
        var maxV = GetVerticalScrollMaximum(state.ScrollViewer);

        // 规范越界夹取：拖过内容边缘时经 Negative/PositiveOffset 扩展画布，再返回夹取后的滚动偏移（WorkflowSurfaceMath.ClampScrollOffset）。
        var newOffsetX = WorkflowSurfaceMath.ClampScrollOffset(
            desiredX, maxH, viewModel.Layout, horizontal: true, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
        var newOffsetY = WorkflowSurfaceMath.ClampScrollOffset(
            desiredY, maxV, viewModel.Layout, horizontal: false, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
        var layoutChanged = newOffsetX != desiredX || newOffsetY != desiredY;

        if (layoutChanged)
        {
            ApplyLayout(host, state);
            state.Canvas?.UpdateLayout();
            state.ScrollViewer.UpdateLayout();
            host.UpdateLayout();
            maxH = GetHorizontalScrollMaximum(state.ScrollViewer);
            maxV = GetVerticalScrollMaximum(state.ScrollViewer);
            newOffsetX = Math.Min(newOffsetX, maxH);
            newOffsetY = Math.Min(newOffsetY, maxV);
            state.PanStart = current;
            state.PanStartOffset = new Vector(
                WorkflowSurfaceMath.ClampValue(newOffsetX, 0, maxH),
                WorkflowSurfaceMath.ClampValue(newOffsetY, 0, maxV));
        }

        var appliedOffsetX = WorkflowSurfaceMath.ClampValue(newOffsetX, 0, maxH);
        var appliedOffsetY = WorkflowSurfaceMath.ClampValue(newOffsetY, 0, maxV);

        state.ScrollViewer.ScrollToHorizontalOffset(appliedOffsetX);
        state.ScrollViewer.ScrollToVerticalOffset(appliedOffsetY);
        state.PanStart = current;
        state.PanStartOffset = new Vector(appliedOffsetX, appliedOffsetY);
        UpdateVisibleRegion(host, state);
        e.Handled = true;
    }

    private static void ApplyLayout(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
        {
            return;
        }

        var transform = new TranslateTransform(
            viewModel.Layout.ActualOffset.Horizontal,
            viewModel.Layout.ActualOffset.Vertical);

        WorkflowCanvasTransformBehavior.Apply(host, transform);

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
    }

    private static void UpdateVisibleRegion(UserControl host, SurfaceState state)
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

    private static void UpdateGridDecorator(IWorkflowTreeViewModel viewModel, SurfaceState state)
    {
        if (state.GridDecorator is null || state.ScrollViewer is null)
        {
            return;
        }

        if (state.GridDecorator is IWorkflowGridDecorator decorator)
        {
            decorator.ScrollOffsetX = state.ScrollViewer.HorizontalOffset;
            decorator.ScrollOffsetY = state.ScrollViewer.VerticalOffset;
            decorator.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
            decorator.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;

            // 让虚拟化可见区修正与装饰器的浮动标尺带保持一致，标尺下方的节点才不会提前一个标尺厚度被剔除。
            viewModel.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand);
        }
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
        minimap.ViewportWidth = state.ScrollViewer.ViewportWidth;
        minimap.ViewportHeight = state.ScrollViewer.ViewportHeight;
        minimap.WorkflowTree = viewModel;
    }

    private static double GetHorizontalScrollMaximum(ScrollViewer scrollViewer)
        => WorkflowSurfaceMath.ScrollMax(scrollViewer.ExtentWidth, scrollViewer.ViewportWidth);

    private static double GetVerticalScrollMaximum(ScrollViewer scrollViewer)
        => WorkflowSurfaceMath.ScrollMax(scrollViewer.ExtentHeight, scrollViewer.ViewportHeight);

    private static bool IsSurfaceBlankInteraction(DependencyObject source, SurfaceState state)
    {
        if (IsWorkflowNodeOrSlotVisual(source))
        {
            return false;
        }

        var ancestors = EnumerateVisualAncestors(source).ToArray();
        if (ancestors.Any(IsWorkflowNodeOrSlotVisual))
        {
            return false;
        }

        // 点击 ScrollViewer 的滚动条（Thumb/Track/RepeatButton/ScrollBar）时不起画布平移。
        if (source is System.Windows.Controls.Primitives.ScrollBar
            || ancestors.Any(x => x is System.Windows.Controls.Primitives.ScrollBar))
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
                || x is ScrollContentPresenter);
    }

    private static bool IsWorkflowNodeOrSlotVisual(DependencyObject source)
        => source is FrameworkElement { DataContext: IWorkflowNodeViewModel or IWorkflowSlotViewModel };

    private static bool IsWorkflowLinkVisual(DependencyObject source)
        => source is FrameworkElement element
            && (element.DataContext is IWorkflowLinkViewModel
                || string.Equals(element.GetType().Name, "BezierCurveView", StringComparison.Ordinal)
                || string.Equals(element.GetType().Name, "PolylineCurveView", StringComparison.Ordinal));

    private static System.Collections.Generic.IEnumerable<DependencyObject> EnumerateVisualAncestors(DependencyObject source)
    {
        var current = VisualTreeHelper.GetParent(source);
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }
}
}