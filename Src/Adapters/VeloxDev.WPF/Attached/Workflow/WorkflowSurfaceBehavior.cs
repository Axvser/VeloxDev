using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

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

        // 连线右键菜单：菜单由模板声明（条目归用户，见 LinkMenuKeyProperty），订阅、定位、弹出、开合上报都在这里。
        public ContextMenu? LinkMenu { get; set; }
        public IWorkflowLinkViewModel? MenuLink { get; set; }
        public Anchor MenuPosition { get; set; } = new();
        public LinkInteraction? MenuHub { get; set; }
        public EventHandler<ContextMenuRequestedEventArgs>? MenuRequested { get; set; }
        public EventHandler<ContextMenuDismissRequestedEventArgs>? MenuDismissed { get; set; }
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
    /// resolves which link, positions the menu, opens it, and reports open/close to the interaction hub.
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

    // 连线右键菜单：**条目由模板声明**（资源键挂在 LinkMenuKeyProperty 上），**接线在这里** —— 订中枢、定位、
    // 弹出、把开合报回去，模板因此没有一行交互代码。菜单指着的那条线离树时中枢发 DismissRequested，这里收自己那份。
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
            if (state.LinkMenu is { } open) open.IsOpen = false;
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

        state.MenuLink = e.Link;
        state.MenuPosition = e.Position;

        // 菜单的 DataContext 就是这条连线，条目据此绑定命令。
        state.LinkMenu.DataContext = e.Link;

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
        control.MouseEnter += OnLinkPointerEntered;
        control.MouseLeave += OnLinkPointerExited;
        control.KeyDown += OnLinkKeyDown;
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
        control.MouseEnter -= OnLinkPointerEntered;
        control.MouseLeave -= OnLinkPointerExited;
        control.KeyDown -= OnLinkKeyDown;
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

        // Wheel up (positive delta) zooms in: Scale is a collapse factor, so zoom-in divides it by 1/1.1.
        var factor = e.Delta > 0 ? 1 / 1.1 : 1.1;
        var next = Math.Max(0.1, Math.Min(10, viewModel.Layout.Scale.Horizontal * factor));
        var layout = viewModel.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter
            && host.GetValue(StateProperty) is SurfaceState state
            && state.ScrollViewer is { } sv)
        {
            // Capture the world point under the viewport center, collapse the nodes about it, then
            // scroll so that point stays put: nodes near the viewport center remain visible while zooming.
            // The canvas geometry is untouched by the zoom (ActualOffset == NegativeOffset, fixed) — only
            // the scroll moves, so the canvas position never jitters.
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                sv.HorizontalOffset, sv.VerticalOffset, sv.ViewportWidth, sv.ViewportHeight, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);

            // Deep zoom-in collapses negative-world content past the fixed canvas translate (ActualOffset
            // == NegativeOffset); grow the cover BEFORE ApplyLayout adopts the new offset. PivotCenterScroll
            // below reads the grown offset, so the extra cover is absorbed by the scroll target and the
            // pivot stays centered — no manual delta needed. Positive-only content is a no-op.
            WorkflowSurfaceMath.EnsureNegativeCover(viewModel);

            // Let the ScrollViewer adopt the (possibly auto-extended) extent BEFORE reading the max.
            // Zoom-in below scale 1 grows the canvas, zoom-out may shrink it; the clamp must see the
            // settled extent or the pivot lands off-center and the next tick re-captures the drift.
            ApplyLayout(host, state);
            state.Canvas?.UpdateLayout();
            sv.UpdateLayout();
            host.UpdateLayout();

            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, sv.ViewportWidth, sv.ViewportHeight);
            var maxH = GetHorizontalScrollMaximum(sv);
            var maxV = GetVerticalScrollMaximum(sv);

            // Overscroll-expand the canvas (same mechanism as panning past an edge) so the pivot is
            // always reachable; a plain clamp would push the pivot off-center and drift on each tick.
            var newX = WorkflowSurfaceMath.ClampScrollOffset(tx, maxH, layout, horizontal: true);
            var newY = WorkflowSurfaceMath.ClampScrollOffset(ty, maxV, layout, horizontal: false);
            if (Math.Abs(newX - tx) > double.Epsilon || Math.Abs(newY - ty) > double.Epsilon)
            {
                // Expansion changed the extent; re-apply and re-read the max so tx/ty land inside it.
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
            // World-origin zoom: the canvas translate only changes when the layout is re-applied, so if
            // the cover grew, push the new offset through the same layout pass the viewport-center branch
            // does (the trimmed demos are viewport-center, so this path normally stays dormant).
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

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Start canvas panning only when the click lands on blank background (not on nodes/slots/links or other interactive elements).
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
        var anchor = WorkflowSurfaceMath.ToWorldAnchor(point.X, point.Y, 0, viewModel.Layout);
        viewModel.SetPointerCommand.Execute(anchor);

        // 悬停归 Core：曲线发布在 canvas-local 空间（子控件的画布平移之内），指针要过同一次逆变换
        // 才能和曲线比。拉线时指针下正挂着橡皮筋，那段时间不转发，否则沿途实连线会一路亮起。
        if (!viewModel.VirtualLink.IsVisible)
        {
            var interaction = LinkInteraction.For(viewModel);
            interaction.Publish(new PointerEvent(PointerPhase.Moved, anchor));
            FocusHoveredLink(interaction, state);
        }
    }

    // 悬停到连线上的指针消息：进入与移动走同一条翻译 —— Core 只看点在哪，不看事件叫什么。
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

        var point = e.GetPosition(state.Canvas);
        var interaction = LinkInteraction.For(viewModel);
        interaction.Publish(new PointerEvent(
            PointerPhase.Entered,
            WorkflowSurfaceMath.ToWorldAnchor(point.X, point.Y, 0, viewModel.Layout)));
        FocusHoveredLink(interaction, state);
    }

    // 指针离开整块输入面：选中跟着走。菜单弹出引起的那一次离开由 Core 按 IsSuspended 认出并忽略。
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

        var interaction = LinkInteraction.For(viewModel);
        interaction.Publish(new PointerEvent(PointerPhase.Exited, new Anchor()));
        FocusHoveredLink(interaction, state);
    }

    // 按下哪条线由 Core 判（它拿到的是同一空间里的指针），这里只把左右中键翻译过去，不置 Handled ——
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

        var button = e.ChangedButton switch
        {
            MouseButton.Left => PointerButtonKind.Left,
            MouseButton.Right => PointerButtonKind.Right,
            MouseButton.Middle => PointerButtonKind.Middle,
            _ => PointerButtonKind.None,
        };
        if (button == PointerButtonKind.None)
        {
            return;
        }

        var point = e.GetPosition(state.Canvas);
        var interaction = LinkInteraction.For(viewModel);
        interaction.Publish(new PointerEvent(
            PointerPhase.Pressed,
            WorkflowSurfaceMath.ToWorldAnchor(point.X, point.Y, 0, viewModel.Layout),
            button));
        FocusHoveredLink(interaction, state);
    }

    // 悬停把键盘焦点交给能接住它的东西：Delete 才能沿焦点所在子树冒泡到宿主的键路由。
    // 优先交给画出那条线的控件；它不可聚焦时（**模板与 Trimmed 的连线视图默认就是**）退回宿主本身 ——
    // 否则「悬停 + Delete」在生成出来的工程里根本没有路由，而且不报错。
    // 节点卡里的输入框自己处理 Delete 时事件已被标记、冒泡到宿主前就被吃掉，编辑文本不受影响。
    private static void FocusHoveredLink(LinkInteraction interaction, SurfaceState state)
    {
        var hovered = interaction.HoveredLink?.HitTarget()?.Visual as UIElement;
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
    private static void OnLinkKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || sender is not UserControl host
            || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        var interaction = LinkInteraction.For(viewModel);
        if (interaction.HoveredLink is null)
        {
            return;
        }

        interaction.Publish(new KeyEvent(InputKey.Delete));
        e.Handled = true;
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

        if (e.ChangedButton != MouseButton.Left)
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

        // Canonical overscroll clamp: expands the canvas via Negative/PositiveOffset when panning past
        // the content edge, then returns the clamped scroll offset (WorkflowSurfaceMath.ClampScrollOffset).
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

        // Persist the viewport position so it survives serialization round-trip.
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

            // Keep the virtualization visible-region correction in sync with the decorator's
            // floating ruler band so nodes beneath it are not culled a ruler-thickness early.
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

        // Do not start canvas panning when clicking the ScrollViewer's scroll bar (Thumb/Track/RepeatButton/ScrollBar).
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