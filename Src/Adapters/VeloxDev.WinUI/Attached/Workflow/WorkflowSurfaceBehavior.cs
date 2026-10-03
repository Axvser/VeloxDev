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

        // 连线右键菜单：菜单由模板声明（条目归用户，见 LinkMenuKeyProperty），订阅、定位、弹出、开合上报都在这里。
        public MenuFlyout? LinkMenu { get; set; }
        public IWorkflowLinkViewModel? MenuLink { get; set; }
        public Anchor MenuPosition { get; set; } = new();
        public LinkInteraction? MenuHub { get; set; }
        public EventHandler<ContextMenuRequestedEventArgs>? MenuRequested { get; set; }
        public EventHandler<ContextMenuDismissRequestedEventArgs>? MenuDismissed { get; set; }
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
    /// resolves the menu by key, feeds the pressed link to each item, positions it, opens it, and reports
    /// open/close to the interaction hub.
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

    // 连线右键菜单：**条目由模板声明**（挂在 LinkMenuKey 上），**接线在这里** —— 订中枢、定位、弹出、
    // 把开合报回去，模板因此没有一行交互代码。菜单指着的那条线离树时中枢发 DismissRequested，这里收自己那份。
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
            state.LinkMenu?.Hide();
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

    // 右键落在表面上，而弹出要相对某个元素；表面同时知道画布与自己的位置，所以菜单由表面弹。
    private static void ShowLinkMenu(UserControl host, SurfaceState state, ContextMenuRequestedEventArgs e)
    {
        // 空白画布没有可操作的对象，不给菜单。
        if (e.Link is null || state.LinkMenu is null || state.Canvas is null) return;
        if (host.DataContext is not IWorkflowTreeViewModel) return;

        var anchor = state.PointerPressSource ?? (FrameworkElement)host;

        // 画布坐标 → 锚点坐标：画布自带渲染平移（刻度带）与合成平移，TransformToVisual 一并算进去。
        var point = state.Canvas.TransformToVisual(anchor)
            .TransformPoint(new Windows.Foundation.Point(e.Position.Horizontal, e.Position.Vertical));

        state.MenuLink = e.Link;
        state.MenuPosition = e.Position;

        state.LinkMenu.XamlRoot = host.XamlRoot;

        // MenuFlyout 继承 FlyoutBase → DependencyObject，不在可视树上也没有 DataContext，逐条把这条连线喂给条目。
        foreach (var item in state.LinkMenu.Items)
        {
            if (item is FrameworkElement element)
            {
                element.DataContext = e.Link;
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
        control.PointerExited += OnPointerExited;
        control.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        // 连线交互要看到整个 surface 上的按下与按键，包括被其它处理器标记为 Handled 的那些
        control.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLinkPointerPressed), true);
        control.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnLinkKeyDown), false);
        ResolveNamedControls(control, state);
        Refresh(control);
    }

    private static void Detach(UserControl control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.DataContextChanged -= OnDataContextChanged;
        control.PointerMoved -= OnPointerMoved;
        control.PointerExited -= OnPointerExited;
        control.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased));
        control.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLinkPointerPressed));
        control.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnLinkKeyDown));

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

    // WinUI has no PreviewMouseWheel, so the wheel is handled on the SCROLLVIEWER (always in the bubble
    // path over its whole content). Hooked with handledEventsToo:true so it still fires when a node
    // handled the wheel first. A Ctrl+wheel notch may also scroll a hair before the handler runs — the
    // zoom still applies everywhere (hooking the canvas instead only reached its hit-testable area).
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
        // Wheel up (positive delta) zooms in: Scale is a collapse factor, so zoom-in divides it by 1/1.1.
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
            // Deep zoom-in collapses negative-world content past the fixed canvas translate (ActualOffset
            // == NegativeOffset); grow the cover BEFORE ApplyLayout adopts the new offset. PivotCenterScroll
            // below reads the grown offset, so the extra cover is absorbed by the scroll target and the
            // pivot stays centered — no manual delta needed. See WorkflowSurfaceMath.EnsureNegativeCover.
            WorkflowSurfaceMath.EnsureNegativeCover(viewModel);

            // Force a layout pass so the ScrollViewer adopts the new extent BEFORE we land the scroll.
            // Otherwise ChangeView clamps against the stale extent, the pivot lands off-center, and the
            // next wheel notch re-captures from that error — the compounding drift reads as zoom jitter.
            ApplyLayout(host, state);
            state.Canvas?.UpdateLayout();
            sv.UpdateLayout();
            host.UpdateLayout();

            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, sv.ViewportWidth, sv.ViewportHeight);
            var maxH = GetHorizontalScrollMaximum(sv);
            var maxV = GetVerticalScrollMaximum(sv);

            // Overscroll-expand the canvas so the pivot is always reachable; a plain clamp would push
            // the pivot off-center and drift on each notch. The canvas geometry is untouched by the
            // zoom (ActualOffset == NegativeOffset, fixed) — only the scroll moves.
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

            // Synchronous re-virtualize at the COMMITTED scroll target (mirrors Jalium
            // TreeView.NotifyZoomCommitted). The helper otherwise re-virtualizes on its ~10fps dirty
            // tick / Low-priority viewport queue, so during a zoom burst the pooled node/link views lag
            // the freshly collapsed anchors by ~100ms — links vanish/pop for that window. Use the
            // committed offset, not sv.HorizontalOffset, which ChangeView may not have applied yet.
            VirtualizeAtScroll(viewModel, committedX, committedY, sv.ViewportWidth, sv.ViewportHeight);
        }
        else
        {
            layout.Scale = new Scale(next, next);
            // World-origin zoom: the canvas translate only changes when the layout is re-applied, so if
            // the cover grew, push the new offset through the same layout pass the viewport-center branch
            // does (the trim demo is viewport-center, so this path normally stays dormant).
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

            // Re-virtualize even when the cover did not grow: collapse can shrink a box that straddled
            // the window edge out of it, and pooled views otherwise only catch up on the ~10fps dirty
            // tick / Low-priority queue. Scroll is unchanged here, so the current offsets are valid.
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

        if (!ShouldStartPan(e, state))
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

        // 悬停归 Core：把同一份 canvas-local 坐标转发进去，命中的曲线就是各连线视图发布的那条。
        var interaction = LinkInteraction.For(viewModel);
        interaction.Publish(new PointerEvent(PointerPhase.Moved, anchor));

        // 悬停到连线上就把焦点收到宿主：Delete 要的按键事件经过它，而「悬停（不点）就能删」是契约。
        // 只在按下时取焦点的话，生成出来的工程得先点一下连线才删得掉 —— 与其余六家的手感不一致。
        if (interaction.HoveredLink is not null)
        {
            host.Focus(FocusState.Pointer);
        }
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

        var interaction = LinkInteraction.For(viewModel);

        var position = e.GetCurrentPoint(host).Position;
        if (position.X >= 0 && position.Y >= 0
            && position.X <= host.ActualWidth && position.Y <= host.ActualHeight)
        {
            return;
        }

        interaction.Publish(new PointerEvent(PointerPhase.Exited, new Anchor()));
    }

    // 按下：转发给 Core 裁决（是否落在某条连线上、哪个键）。不置 Handled —— 画布手势照旧。
    private static void OnLinkPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.ScrollViewer is null)
        {
            return;
        }

        var properties = e.GetCurrentPoint(host).Properties;
        var button = properties.IsRightButtonPressed ? PointerButtonKind.Right
            : properties.IsMiddleButtonPressed ? PointerButtonKind.Middle
            : properties.IsLeftButtonPressed ? PointerButtonKind.Left
            : PointerButtonKind.None;
        if (button == PointerButtonKind.None)
        {
            return;
        }

        var interaction = LinkInteraction.For(viewModel);
        interaction.Publish(new PointerEvent(
            PointerPhase.Pressed, ToCanvasLocalAnchor(state, e, viewModel.Layout), button));

        // 命中一条线就把焦点收到本宿主：Delete 要的按键事件经过它，悬停高亮才删得掉。
        if (interaction.HoveredLink is not null)
        {
            host.Focus(FocusState.Pointer);
        }
    }

    // Delete 归 Core：本层只把按键翻译过去，由它决定「现在悬停的哪条线」要删。
    private static void OnLinkKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not UserControl host || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        if (e.Key != VirtualKey.Delete)
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

        // WinUI children (unlike WPF) do not bind RenderTransform to
        // CanvasTransformBehavior.Transform; apply the offset directly to
        // the canvas via composition transform (the layout-aware approach).
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

        // Persist the viewport position so it survives serialization round-trip.
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

        // Keep the virtualization visible-region correction in sync with the decorator's
        // floating ruler band so nodes beneath it are not culled a ruler-thickness early.
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
        // Push the ACTUAL visible area (the ScrollViewer's rendered size). ViewportWidth can report
        // an effective/larger value on window shrink; the minimap's draggable block must track the
        // real on-screen region (matches the minimap's own OnScrollViewerResized, which uses the
        // settled ActualWidth after the layout pass).
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

    // Link identification for the pan-blank heuristic only: which link is under the pointer is decided by
    // the hub (LinkInteraction). DataContext is enough here — a link view's content inherits it — and the
    // former class-name fallback was redundant with it and made this path stringly typed.
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
