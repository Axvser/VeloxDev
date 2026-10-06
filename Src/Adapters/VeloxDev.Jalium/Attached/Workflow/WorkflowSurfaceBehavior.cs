using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.StandardEx;
using PlatformInput = Jalium.UI.Input;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The Jalium workflow surface: it finds the template's named parts, keeps the view pool bound to the tree's
/// visible set, drives the viewport and the pan, and forwards pointer and keyboard input to Core's shared input router.
/// </summary>
/// <remarks>
/// <para>
/// It is a <b>host of named parts</b>, not a control you derive from. Put it on the tree template's root and name
/// the parts it should drive:
/// <code>
/// behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
/// behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
/// behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
/// </code>
/// The tree itself comes from the host's <see cref="FrameworkElement.DataContext"/>.
/// </para>
/// <para>
/// Cards, ports and links are <b>not</b> handled here: those are markup, and each view's own behavior
/// (<c>WorkflowNodeDragBehavior</c>, <c>WorkflowSlotConnectionBehavior</c>, <c>WorkflowSlotLayoutBehavior</c>,
/// <c>WorkflowLinkBounds</c>) owns its gestures. This class only knows about the surface — the world transform,
/// the viewport, and the pointer stream that hover, link deletion and the context menu are decided from.
/// </para>
/// </remarks>
public static class WorkflowSurfaceBehavior
{
    /// <summary>The resource key the view pool's template selector is looked up under.</summary>
    /// <remarks>One name across the seven adapters, so a host that moves platform does not relearn it.</remarks>
    public const string TemplateSelectorKey = "WorkflowTemplateSelector";

    private const double ZoomPinLifetimeMs = 250d;

    /// <summary>Lower bound for the canvas size, so a nearly-empty tree still has a pannable surface.</summary>
    public const double CanvasWidth = 2000d;

    /// <summary>The vertical counterpart of <see cref="CanvasWidth"/>.</summary>
    public const double CanvasHeight = 2000d;

    private sealed class SurfaceState
    {
        public FrameworkElement Host = null!;

        public ScrollViewer? ScrollViewer;

        public Panel? Canvas;

        public IWorkflowGridDecorator? GridDecorator;

        public FrameworkElement? GridDecoratorElement;

        public IWorkflowMinimapOverlay? MinimapOverlay;

        public FrameworkElement? PointerPressSource;

        public IWorkflowTreeViewModel? Tree;

        public WorkflowInput? Input;

        public ContextMenu? LinkMenu;

        public IWorkflowLinkViewModel? MenuLink;

        public bool IsPanning;

        public Point PanStart;

        public double PanStartHorizontal;

        public double PanStartVertical;

        // 提交的缩放滚动目标。Jalium 的 ScrollTo 可能异步落地：落地前发来的旧偏移不能改写视口，
        // 否则下一次 Virtualize 会把刚物化的连线剔掉，而节点（按自己的矩形进池）留下 —— 深缩放闪断。
        public (double X, double Y, long Ticks)? ZoomPin;

        public PropertyChangedEventHandler? LayoutChangedHandler;

        public INotifyPropertyChanged? LayoutSource;

        // 解订要用同一个委托实例，所以每一处订阅都留一份。
        public MouseButtonEventHandler? MouseDownHandler;

        public MouseButtonEventHandler? PressSourceDownHandler;

        public MouseEventHandler? MouseMoveHandler;

        public MouseButtonEventHandler? MouseUpHandler;

        public MouseWheelEventHandler? MouseWheelHandler;

        public MouseWheelEventHandler? ZoomWheelHandler;

        public PlatformInput.KeyEventHandler? KeyDownHandler;

        public PlatformInput.KeyEventHandler? KeyUpHandler;

        public MouseEventHandler? LostCaptureHandler;

        public RequestBringIntoViewEventHandler? BringIntoViewHandler;

        public MouseEventHandler? MouseLeaveHandler;

        public EventHandler<Wf.PointerPressedEventArgs>? InputPressedHandler;

        public EventHandler<IWorkflowLinkViewModel>? LinkRemovedHandler;
    }

    /// <summary>The attached property that turns the surface on for a host.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowSurfaceBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>The attached property naming the <see cref="ScrollViewer"/> whose offsets define the viewport.</summary>
    public static readonly DependencyProperty ScrollViewerNameProperty = DependencyProperty.RegisterAttached(
        "ScrollViewerName", typeof(string), typeof(WorkflowSurfaceBehavior), new PropertyMetadata(null, OnPartNameChanged));

    /// <summary>The attached property naming the panel the view pool fills.</summary>
    public static readonly DependencyProperty CanvasNameProperty = DependencyProperty.RegisterAttached(
        "CanvasName", typeof(string), typeof(WorkflowSurfaceBehavior), new PropertyMetadata(null, OnPartNameChanged));

    /// <summary>The attached property naming the grid-and-ruler decorator.</summary>
    public static readonly DependencyProperty GridDecoratorNameProperty = DependencyProperty.RegisterAttached(
        "GridDecoratorName", typeof(string), typeof(WorkflowSurfaceBehavior), new PropertyMetadata(null, OnPartNameChanged));

    /// <summary>The attached property naming the element a press must land on to start a pan.</summary>
    public static readonly DependencyProperty PointerPressSourceNameProperty = DependencyProperty.RegisterAttached(
        "PointerPressSourceName", typeof(string), typeof(WorkflowSurfaceBehavior), new PropertyMetadata(null, OnPartNameChanged));

    /// <summary>The attached property naming the minimap overlay.</summary>
    public static readonly DependencyProperty MinimapOverlayNameProperty = DependencyProperty.RegisterAttached(
        "MinimapOverlayName", typeof(string), typeof(WorkflowSurfaceBehavior), new PropertyMetadata(null, OnPartNameChanged));

    /// <summary>The attached property that lets the surface drive a zoom gesture of its own.</summary>
    public static readonly DependencyProperty ZoomEnabledProperty = DependencyProperty.RegisterAttached(
        "ZoomEnabled", typeof(bool), typeof(WorkflowSurfaceBehavior), new PropertyMetadata(false, OnZoomEnabledChanged));

    /// <summary>
    /// The transform that carries the world-to-view translation, published on the surface host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node and link templates bind their <c>RenderTransform</c> to this, which is how every pooled view
    /// follows the world translate. WPF ships the same value as <c>WorkflowCanvasTransformBehavior.Transform</c>;
    /// the difference here is only that this platform's bindings cannot read an attached property, so the value
    /// is <b>attached here and mirrored onto a plain property by the tree view's own class</b> (see the
    /// <c>workflow-tree-view</c> template) — the templates then bind to that plain property.
    /// </para>
    /// <para>
    /// Setting it on the host rather than the canvas is what lets a slot's anchor be measured with the other six
    /// adapters' contract (<c>WorkflowSurfaceMath.SlotAnchorFromVisualCenter</c>): the translation is inside the
    /// measured subtree, so the measurement must subtract it.
    /// </para>
    /// </remarks>
    public static readonly DependencyProperty CanvasTransformProperty = DependencyProperty.RegisterAttached(
        "CanvasTransform", typeof(Transform), typeof(WorkflowSurfaceBehavior), new PropertyMetadata(null));

    /// <summary>Reads the <c>CanvasTransform</c> attached property from <paramref name="element"/>.</summary>
    public static Transform? GetCanvasTransform(DependencyObject element) => (Transform?)element.GetValue(CanvasTransformProperty);

    /// <summary>Sets the <c>CanvasTransform</c> attached property on <paramref name="element"/>.</summary>
    public static void SetCanvasTransform(DependencyObject element, Transform? value) => element.SetValue(CanvasTransformProperty, value);

    /// <summary>The attached property naming the resource that holds the context menu shown for a link.</summary>
    /// <remarks>
    /// A key rather than the menu itself: this property sits on the surface's own root, where a
    /// <c>{StaticResource}</c> would be evaluated before the dictionary that defines it. The key is resolved by
    /// this behavior once the host is loaded, which sidesteps that ordering.
    /// </remarks>
    public static readonly DependencyProperty LinkMenuKeyProperty = DependencyProperty.RegisterAttached(
        "LinkMenuKey", typeof(string), typeof(WorkflowSurfaceBehavior), new PropertyMetadata(null, OnPartNameChanged));

    private static readonly ConditionalWeakTable<FrameworkElement, SurfaceState> States = new();

    /// <summary>Reads the <c>IsEnabled</c> attached property from <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(DependencyObject element) => element.GetValue(IsEnabledProperty) is true;

    /// <summary>Sets the <c>IsEnabled</c> attached property on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Reads the <c>ScrollViewerName</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetScrollViewerName(DependencyObject element) => (string?)element.GetValue(ScrollViewerNameProperty);

    /// <summary>Sets the <c>ScrollViewerName</c> attached property on <paramref name="element"/>.</summary>
    public static void SetScrollViewerName(DependencyObject element, string? value) => element.SetValue(ScrollViewerNameProperty, value);

    /// <summary>Reads the <c>CanvasName</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetCanvasName(DependencyObject element) => (string?)element.GetValue(CanvasNameProperty);

    /// <summary>Sets the <c>CanvasName</c> attached property on <paramref name="element"/>.</summary>
    public static void SetCanvasName(DependencyObject element, string? value) => element.SetValue(CanvasNameProperty, value);

    /// <summary>Reads the <c>GridDecoratorName</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetGridDecoratorName(DependencyObject element) => (string?)element.GetValue(GridDecoratorNameProperty);

    /// <summary>Sets the <c>GridDecoratorName</c> attached property on <paramref name="element"/>.</summary>
    public static void SetGridDecoratorName(DependencyObject element, string? value) => element.SetValue(GridDecoratorNameProperty, value);

    /// <summary>Reads the <c>PointerPressSourceName</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetPointerPressSourceName(DependencyObject element) => (string?)element.GetValue(PointerPressSourceNameProperty);

    /// <summary>Sets the <c>PointerPressSourceName</c> attached property on <paramref name="element"/>.</summary>
    public static void SetPointerPressSourceName(DependencyObject element, string? value) => element.SetValue(PointerPressSourceNameProperty, value);

    /// <summary>Reads the <c>MinimapOverlayName</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetMinimapOverlayName(DependencyObject element) => (string?)element.GetValue(MinimapOverlayNameProperty);

    /// <summary>Sets the <c>MinimapOverlayName</c> attached property on <paramref name="element"/>.</summary>
    public static void SetMinimapOverlayName(DependencyObject element, string? value) => element.SetValue(MinimapOverlayNameProperty, value);

    /// <summary>Reads the <c>ZoomEnabled</c> attached property from <paramref name="element"/>.</summary>
    public static bool GetZoomEnabled(DependencyObject element) => element.GetValue(ZoomEnabledProperty) is true;

    /// <summary>Sets the <c>ZoomEnabled</c> attached property on <paramref name="element"/>.</summary>
    public static void SetZoomEnabled(DependencyObject element, bool value) => element.SetValue(ZoomEnabledProperty, value);

    /// <summary>Reads the <c>LinkMenuKey</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetLinkMenuKey(DependencyObject element) => (string?)element.GetValue(LinkMenuKeyProperty);

    /// <summary>Sets the <c>LinkMenuKey</c> attached property on <paramref name="element"/>.</summary>
    public static void SetLinkMenuKey(DependencyObject element, string? value) => element.SetValue(LinkMenuKeyProperty, value);

    /// <summary>Re-reads the host's named parts and re-syncs the canvas, the viewport and the overlays.</summary>
    /// <param name="host">The element the behavior is enabled on.</param>
    public static void Refresh(FrameworkElement host)
    {
        if (host.GetValue(IsEnabledProperty) is not true || !States.TryGetValue(host, out var state))
        {
            return;
        }

        ResolveNamedParts(host, state);
        UpdateCanvasSize(state);
        ApplyLayout(state);
        UpdateViewport(state);
        UpdateOverlays(state);
    }

    /// <summary>Centers the view on a world point, growing the canvas if the target scroll runs past an edge.</summary>
    /// <param name="host">The element the behavior is enabled on.</param>
    /// <param name="worldX">The world X.</param>
    /// <param name="worldY">The world Y.</param>
    /// <remarks>Shared by the pan and the minimap's drag-to-pan.</remarks>
    public static void NavigateToWorld(FrameworkElement host, double worldX, double worldY)
    {
        if (!States.TryGetValue(host, out var state) || state.ScrollViewer is not { } viewer || state.Tree is null)
        {
            return;
        }

        var targetHorizontal = worldX - viewer.ViewportWidth / 2d + ContentOriginX(state);
        var targetVertical = worldY - viewer.ViewportHeight / 2d + ContentOriginY(state);

        if (targetHorizontal < 0d)
        {
            Grow(state, -targetHorizontal, 0d);
            targetHorizontal = 0d;
        }
        else if (targetHorizontal > viewer.ScrollableWidth)
        {
            Grow(state, targetHorizontal - viewer.ScrollableWidth, 0d);
        }

        if (targetVertical < 0d)
        {
            Grow(state, 0d, -targetVertical);
            targetVertical = 0d;
        }
        else if (targetVertical > viewer.ScrollableHeight)
        {
            Grow(state, 0d, targetVertical - viewer.ScrollableHeight);
        }

        viewer.ScrollToHorizontalOffset(targetHorizontal);
        viewer.ScrollToVerticalOffset(targetVertical);
    }

    /// <summary>
    /// Re-runs viewport virtualization against the current scroll offsets.
    /// </summary>
    /// <param name="host">The element the behavior is enabled on.</param>
    /// <remarks>
    /// The helper otherwise virtualizes on its ~10 fps dirty timer, so after a zoom burst the pooled views lag
    /// the freshly collapsed anchors by up to ~100 ms. Call this once the scale and the offsets have settled.
    /// </remarks>
    public static void NotifyZoomCommitted(FrameworkElement host)
        => NotifyZoomCommitted(host, double.NaN, double.NaN);

    /// <summary>Re-runs viewport virtualization against a committed scroll target.</summary>
    /// <param name="host">The element the behavior is enabled on.</param>
    /// <param name="horizontalOffset">The committed horizontal offset, or <see cref="double.NaN"/> to read the viewer's.</param>
    /// <param name="verticalOffset">The committed vertical offset, or <see cref="double.NaN"/> to read the viewer's.</param>
    public static void NotifyZoomCommitted(FrameworkElement host, double horizontalOffset, double verticalOffset)
    {
        if (!States.TryGetValue(host, out var state) || state.Tree is not { } tree)
        {
            return;
        }

        if (double.IsNaN(horizontalOffset))
        {
            horizontalOffset = state.ScrollViewer?.HorizontalOffset ?? 0d;
        }

        if (double.IsNaN(verticalOffset))
        {
            verticalOffset = state.ScrollViewer?.VerticalOffset ?? 0d;
        }

        // 钉住提交的目标：Jalium 可能在落地前发一次带旧偏移的 ScrollChanged，那会覆盖掉下面要写的窗口。
        state.ZoomPin = (horizontalOffset, verticalOffset, DateTime.UtcNow.Ticks);
        UpdateViewport(state, horizontalOffset, verticalOffset);
        tree.GetHelper().Virtualize(tree.GetHelper().Viewport);
        UpdateOverlays(state);
        host.InvalidateVisual();
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host)
        {
            return;
        }

        if (e.NewValue is true)
        {
            Attach(host);
            return;
        }

        Detach(host);
    }

    private static void OnPartNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement host && host.GetValue(IsEnabledProperty) is true)
        {
            Refresh(host);
        }
    }

    private static void Attach(FrameworkElement host)
    {
        Detach(host);

        var state = new SurfaceState { Host = host };
        States.Add(host, state);

        // 表面必须能拿焦点：Delete 只有这一条到达本层的路。
        host.Focusable = true;

        state.MouseDownHandler = OnMouseDown;
        state.MouseMoveHandler = OnMouseMove;
        state.MouseUpHandler = OnMouseUp;
        state.MouseWheelHandler = OnMouseWheel;
        state.KeyDownHandler = OnKeyDown;
        state.KeyUpHandler = OnKeyUp;
        state.LostCaptureHandler = OnLostMouseCapture;
        state.BringIntoViewHandler = OnRequestBringIntoView;
        state.MouseLeaveHandler = OnMouseLeave;

        host.Loaded += OnLoaded;
        host.Unloaded += OnUnloaded;
        host.DataContextChanged += OnDataContextChanged;
        host.AddHandler(UIElement.PreviewMouseDownEvent, state.MouseDownHandler);
        host.AddHandler(UIElement.PreviewMouseMoveEvent, state.MouseMoveHandler);
        host.AddHandler(UIElement.PreviewMouseUpEvent, state.MouseUpHandler);
        host.AddHandler(Mouse.MouseWheelEvent, state.MouseWheelHandler);
        host.AddHandler(UIElement.KeyDownEvent, state.KeyDownHandler);
        host.AddHandler(UIElement.KeyUpEvent, state.KeyUpHandler);
        host.AddHandler(UIElement.LostMouseCaptureEvent, state.LostCaptureHandler);
        host.AddHandler(FrameworkElement.RequestBringIntoViewEvent, state.BringIntoViewHandler);
        host.MouseLeave += state.MouseLeaveHandler;

        ResolveNamedParts(host, state);

        if (host.IsLoaded)
        {
            BindTree(host, state);
        }
    }

    private static void Detach(FrameworkElement host)
    {
        if (!States.TryGetValue(host, out var state))
        {
            return;
        }

        UnbindTree(state);

        host.Loaded -= OnLoaded;
        host.Unloaded -= OnUnloaded;
        host.DataContextChanged -= OnDataContextChanged;
        RemoveHandler(host, UIElement.PreviewMouseDownEvent, state.MouseDownHandler);
        RemoveHandler(host, UIElement.PreviewMouseMoveEvent, state.MouseMoveHandler);
        RemoveHandler(host, UIElement.PreviewMouseUpEvent, state.MouseUpHandler);
        RemoveHandler(host, Mouse.MouseWheelEvent, state.MouseWheelHandler);
        RemoveHandler(host, UIElement.KeyDownEvent, state.KeyDownHandler);
        RemoveHandler(host, UIElement.KeyUpEvent, state.KeyUpHandler);
        RemoveHandler(host, UIElement.LostMouseCaptureEvent, state.LostCaptureHandler);
        RemoveHandler(host, FrameworkElement.RequestBringIntoViewEvent, state.BringIntoViewHandler);

        if (state.MouseLeaveHandler is not null)
        {
            host.MouseLeave -= state.MouseLeaveHandler;
        }

        States.Remove(host);
    }

    // 订阅用的是委托实例，解订必须给同一个实例；字段可能为空（Attach 中途失败），所以在这里挡一下。
    private static void RemoveHandler(UIElement element, RoutedEvent routedEvent, Delegate? handler)
    {
        if (handler is not null)
        {
            element.RemoveHandler(routedEvent, handler);
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement host && States.TryGetValue(host, out var state))
        {
            BindTree(host, state);
            Refresh(host);
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement host && States.TryGetValue(host, out var state))
        {
            state.ZoomPin = null;
            state.IsPanning = false;
        }
    }

    private static void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement host || !States.TryGetValue(host, out var state))
        {
            return;
        }

        UnbindTree(state);
        if (host.IsLoaded)
        {
            BindTree(host, state);
        }

        Refresh(host);
    }

    // ── 部件解析 ──────────────────────────────────────────────────────────

    private static void ResolveNamedParts(FrameworkElement host, SurfaceState state)
    {
        var scrollViewer = ResolvePart<ScrollViewer>(host, GetScrollViewerName(host));
        if (!ReferenceEquals(scrollViewer, state.ScrollViewer))
        {
            if (state.ScrollViewer is { } previous)
            {
                previous.ScrollChanged -= OnScrollViewerChanged;
                previous.SizeChanged -= OnScrollViewerSizeChanged;
            }

            state.ScrollViewer = scrollViewer;

            if (scrollViewer is not null)
            {
                scrollViewer.ScrollChanged += OnScrollViewerChanged;
                scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
            }

            // 视口可能晚于 ZoomEnabled 解析出来，所以两处都挂一次（幂等）。
            HookZoom(host, state);
        }

        state.Canvas = ResolvePart<Panel>(host, GetCanvasName(host));

        // 「起平移」挂在**具名按下源**的预览相上，与 WPF 同形。挂在宿主的冒泡相上收不到：
        // 滚动视口那一层会把 MouseDown 标成已处理，宿主的处理器于是整场不触发（这里是实测踩到的）。
        var pressSource = ResolvePart<FrameworkElement>(host, GetPointerPressSourceName(host));
        if (!ReferenceEquals(pressSource, state.PointerPressSource))
        {
            if (state.PointerPressSource is { } previous && state.PressSourceDownHandler is not null)
            {
                previous.PreviewMouseDown -= state.PressSourceDownHandler;
            }

            state.PointerPressSource = pressSource;

            if (pressSource is not null)
            {
                state.PressSourceDownHandler ??= OnPointerPressSourceDown;
                pressSource.PreviewMouseDown += state.PressSourceDownHandler;
            }
        }

        state.GridDecoratorElement = ResolvePart<FrameworkElement>(host, GetGridDecoratorName(host));
        state.GridDecorator = state.GridDecoratorElement as IWorkflowGridDecorator;
        state.MinimapOverlay = ResolvePart<FrameworkElement>(host, GetMinimapOverlayName(host)) as IWorkflowMinimapOverlay;
    }

    private static T? ResolvePart<T>(FrameworkElement host, string? name) where T : class
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return host.FindName(name!) as T;
    }

    private static void OnZoomEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement host && States.TryGetValue(host, out var state))
        {
            HookZoom(host, state);
        }
    }

    // 缩放归表面：Ctrl + 滚轮。挂在 ScrollViewer 的**预览**上，否则滚轮会先被它拿去滚动。
    private static void HookZoom(FrameworkElement host, SurfaceState state)
    {
        state.ZoomWheelHandler ??= OnZoomPreviewMouseWheel;

        if (state.ScrollViewer is { } viewer)
        {
            viewer.PreviewMouseWheel -= state.ZoomWheelHandler;
            if (GetZoomEnabled(host))
            {
                viewer.PreviewMouseWheel += state.ZoomWheelHandler;
            }
        }
    }

    private static void OnZoomPreviewMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer
            || FindHost(viewer) is not { } host
            || !States.TryGetValue(host, out var state)
            || state.Tree is not { } tree)
        {
            return;
        }

        if (e.KeyboardModifiers != ModifierKeys.Control)
        {
            return;
        }

        // 滚轮向上（增量为正）放大。Scale 是**折叠因子** —— 数值越大节点越小，所以放大要除以 1.1。
        var factor = e.Delta > 0 ? 1d / 1.1 : 1.1;
        var layout = tree.Layout;
        var next = Math.Max(0.1, Math.Min(10d, layout.Scale.Horizontal * factor));

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter)
        {
            // 钉住视口中心下方的世界点，绕它折叠，再滚动让那个点不动。
            var (worldX, worldY) = WorkflowSurfaceMath.WorldAtViewportCenter(
                viewer.HorizontalOffset, viewer.VerticalOffset, viewer.ViewportWidth, viewer.ViewportHeight, layout);
            layout.CollapsePivot = new Anchor(worldX, worldY, 0);
            layout.Scale = new Scale(next, next);

            // 深度放大把负向内容折叠过固定画布位移；必须在采纳新偏移前扩大覆盖。
            WorkflowSurfaceMath.EnsureNegativeCover(tree);

            // 先让 ScrollViewer 采纳（可能自动延伸的）范围，再读最大值 —— 否则夹取落在陈旧范围上，
            // 枢轴偏离中心，下一格又重捕一次，读起来就是缩放抖动。
            UpdateCanvasSize(state);
            ApplyLayout(state);
            host.UpdateLayout();
            viewer.UpdateLayout();

            var (targetX, targetY) = WorkflowSurfaceMath.PivotCenterScroll(
                worldX, worldY, layout, viewer.ViewportWidth, viewer.ViewportHeight);
            var maxH = viewer.ScrollableWidth;
            var maxV = viewer.ScrollableHeight;

            // 越界扩展画布，让枢轴总能到达；单纯夹取会把枢轴推离中心并逐格漂移。
            var newX = WorkflowSurfaceMath.ClampScrollOffset(targetX, maxH, layout, horizontal: true);
            var newY = WorkflowSurfaceMath.ClampScrollOffset(targetY, maxV, layout, horizontal: false);
            if (Math.Abs(newX - targetX) > double.Epsilon || Math.Abs(newY - targetY) > double.Epsilon)
            {
                UpdateCanvasSize(state);
                ApplyLayout(state);
                host.UpdateLayout();
                viewer.UpdateLayout();
                maxH = viewer.ScrollableWidth;
                maxV = viewer.ScrollableHeight;
            }

            var committedX = WorkflowSurfaceMath.ClampValue(targetX, 0, maxH);
            var committedY = WorkflowSurfaceMath.ClampValue(targetY, 0, maxV);
            viewer.ScrollToHorizontalOffset(committedX);
            viewer.ScrollToVerticalOffset(committedY);

            // 立刻按提交后的几何重跑一次虚拟化：本家的 ScrollTo 异步落地，等它的脏 tick 会让刚物化的
            // 连线被陈旧视口剔掉约 100ms（深缩放闪断）。
            NotifyZoomCommitted(host, committedX, committedY);
        }
        else
        {
            layout.Scale = new Scale(next, next);
            if (WorkflowSurfaceMath.EnsureNegativeCover(tree))
            {
                UpdateCanvasSize(state);
                ApplyLayout(state);
                host.UpdateLayout();
                viewer.UpdateLayout();
            }

            NotifyZoomCommitted(host);
        }

        e.Handled = true;
    }

    private static void OnScrollViewerChanged(object? sender, ScrollChangedEventArgs e) => RefreshFor(sender);

    private static void OnScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e) => RefreshFor(sender);

    private static void RefreshFor(object? source)
    {
        if (source is DependencyObject node && FindHost(node) is { } host)
        {
            Refresh(host);
        }
    }

    /// <summary>
    /// The tree the surface at or above <paramref name="element"/> is showing, if any.
    /// </summary>
    /// <param name="element">Any element inside the surface — a slot view, a node card, the canvas.</param>
    /// <returns>The tree, or <see langword="null"/> when the element is not inside an enabled surface.</returns>
    /// <remarks>
    /// A slot's own <see cref="FrameworkElement.DataContext"/> is the slot, not the tree, so anything that needs
    /// the tree (cancelling a connection, for one) has to reach the host first.
    /// </remarks>
    public static IWorkflowTreeViewModel? TreeOf(DependencyObject element)
        => FindHost(element)?.DataContext as IWorkflowTreeViewModel;

    private static FrameworkElement? FindHost(DependencyObject source)
    {
        foreach (var ancestor in EnumerateSelfAndVisualAncestors(source))
        {
            if (ancestor is FrameworkElement element && element.GetValue(IsEnabledProperty) is true)
            {
                return element;
            }
        }

        return null;
    }

    // ── 树绑定 ────────────────────────────────────────────────────────────

    private static void BindTree(FrameworkElement host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel tree || ReferenceEquals(tree, state.Tree))
        {
            return;
        }

        state.Tree = tree;
        AttachInteraction(state);

        if (state.Canvas is not null)
        {
            if (host.FindResource(TemplateSelectorKey) is DataTemplateSelector selector)
            {
                ViewPool.SetTemplateSelector(state.Canvas, selector);
            }

            ViewPool.SetItemsSource(state.Canvas, tree.GetHelper().VisibleItems);
        }

        if (tree.Layout is INotifyPropertyChanged layout)
        {
            state.LayoutSource = layout;
            state.LayoutChangedHandler = (_, e) => OnLayoutPropertyChanged(host, state, e);
            layout.PropertyChanged += state.LayoutChangedHandler;
        }

        UpdateCanvasSize(state);
        ApplyLayout(state);
        UpdateViewport(state);
        UpdateOverlays(state);
    }

    private static void UnbindTree(SurfaceState state)
    {
        DetachInteraction(state);

        if (state.LayoutSource is not null && state.LayoutChangedHandler is not null)
        {
            state.LayoutSource.PropertyChanged -= state.LayoutChangedHandler;
            state.LayoutSource = null;
            state.LayoutChangedHandler = null;
        }

        if (state.Canvas is not null)
        {
            ViewPool.SetItemsSource(state.Canvas, null);
            ViewPool.SetTemplateSelector(state.Canvas, null);
        }

        state.Tree = null;
    }

    private static void OnLayoutPropertyChanged(FrameworkElement host, SurfaceState state, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "ActualSize" or "ActualOffset" or "Scale")
        {
            UpdateCanvasSize(state);
            ApplyLayout(state);
            UpdateViewport(state);
            UpdateOverlays(state);
            host.InvalidateVisual();
        }
    }

    // ── 画布、视口、叠加层 ────────────────────────────────────────────────

    private static double ContentOriginX(SurfaceState state) => state.Tree?.Layout.ActualOffset.Horizontal ?? 0d;

    private static double ContentOriginY(SurfaceState state) => state.Tree?.Layout.ActualOffset.Vertical ?? 0d;

    private static double RulerBand(SurfaceState state) => state.GridDecorator?.RulerBand ?? 36d;

    private static void UpdateCanvasSize(SurfaceState state)
    {
        if (state.Canvas is null || state.Tree is null)
        {
            return;
        }

        state.Canvas.Width = Math.Max(CanvasWidth, state.Tree.Layout.ActualSize.Width);
        state.Canvas.Height = Math.Max(CanvasHeight, state.Tree.Layout.ActualSize.Height);
        state.Canvas.InvalidateMeasure();
    }

    private static void ApplyLayout(SurfaceState state)
    {
        if (state.Canvas is null || state.Tree is null)
        {
            return;
        }

        // 世界位移**发布在宿主上**（附着属性），由节点/连线模板绑到各自的 RenderTransform —— 与 WPF 同一条路。
        // 挂在宿主而不是画布上，是槽锚点能用其余六家那个契约的前提：位移在被测量子树之内，测量时就该减掉它。
        SetCanvasTransform(state.Host, new TranslateTransform(
            state.Tree.Layout.ActualOffset.Horizontal,
            state.Tree.Layout.ActualOffset.Vertical));
    }

    private static void UpdateViewport(SurfaceState state)
    {
        if (state.ScrollViewer is { } viewer && state.ZoomPin is { } pin)
        {
            var landed = Math.Abs(viewer.HorizontalOffset - pin.X) < 0.5
                      && Math.Abs(viewer.VerticalOffset - pin.Y) < 0.5;
            var ageMs = (DateTime.UtcNow.Ticks - pin.Ticks) / TimeSpan.TicksPerMillisecond;
            if (landed || ageMs > ZoomPinLifetimeMs)
            {
                state.ZoomPin = null;
            }
            else
            {
                UpdateViewport(state, pin.X, pin.Y);
                return;
            }
        }

        UpdateViewport(state, state.ScrollViewer?.HorizontalOffset ?? 0d, state.ScrollViewer?.VerticalOffset ?? 0d);
    }

    private static void UpdateViewport(SurfaceState state, double horizontalOffset, double verticalOffset)
    {
        if (state.Tree is not { } tree)
        {
            return;
        }

        var layout = tree.Layout;
        var viewportWidth = state.ScrollViewer?.ViewportWidth ?? 0d;
        var viewportHeight = state.ScrollViewer?.ViewportHeight ?? 0d;

        if (viewportWidth <= 0d || viewportHeight <= 0d)
        {
            // 视口还没测量（树可能早于窗口布局绑上，而 Jalium 的视口在初次布局时可能不发 ScrollChanged）。
            // 退回整块画布，让第一次 Virtualize 立刻物化出初始的节点与连线，而不是在 0 尺寸视口上空转。
            horizontalOffset = layout.ActualOffset.Horizontal;
            verticalOffset = layout.ActualOffset.Vertical;
            viewportWidth = Math.Max(CanvasWidth, layout.ActualSize.Width);
            viewportHeight = Math.Max(CanvasHeight, layout.ActualSize.Height);
        }

        // 把浮动标尺带算进虚拟化，免得靠它内侧那条边的节点提前一个标尺厚度被剔除。
        tree.SetVirtualizeInset(left: RulerBand(state), top: RulerBand(state));
        tree.GetHelper().Viewport = new Viewport(
            horizontalOffset - layout.ActualOffset.Horizontal,
            verticalOffset - layout.ActualOffset.Vertical,
            viewportWidth,
            viewportHeight);
    }

    private static void UpdateOverlays(SurfaceState state)
    {
        if (state.Tree is not { } tree)
        {
            return;
        }

        var horizontalScroll = state.ScrollViewer?.HorizontalOffset ?? 0d;
        var verticalScroll = state.ScrollViewer?.VerticalOffset ?? 0d;
        var horizontalContent = ContentOriginX(state);
        var verticalContent = ContentOriginY(state);

        if (state.GridDecorator is { } decorator)
        {
            decorator.ScrollOffsetX = horizontalScroll;
            decorator.ScrollOffsetY = verticalScroll;
            decorator.ContentOffsetX = horizontalContent;
            decorator.ContentOffsetY = verticalContent;
        }

        if (state.MinimapOverlay is { } minimap)
        {
            minimap.ScrollOffsetX = horizontalScroll;
            minimap.ScrollOffsetY = verticalScroll;
            minimap.ContentOffsetX = horizontalContent;
            minimap.ContentOffsetY = verticalContent;
            minimap.ViewportWidth = state.ScrollViewer?.ViewportWidth ?? 0d;
            minimap.ViewportHeight = state.ScrollViewer?.ViewportHeight ?? 0d;
            minimap.WorkflowTree = tree;
        }
    }

    private static void Grow(SurfaceState state, double horizontal, double vertical)
    {
        if (state.Tree is not { } tree)
        {
            return;
        }

        var layout = tree.Layout;
        if (horizontal > 0d) layout.PositiveOffset += new Offset(horizontal, 0d);
        else if (horizontal < 0d) layout.NegativeOffset += new Offset(-horizontal, 0d);
        if (vertical > 0d) layout.PositiveOffset += new Offset(0d, vertical);
        else if (vertical < 0d) layout.NegativeOffset += new Offset(0d, -vertical);

        UpdateCanvasSize(state);
        ApplyLayout(state);
    }

    // ── 输入 ──────────────────────────────────────────────────────────────

    private static void AttachInteraction(SurfaceState state)
    {
        if (state.Tree is not { } tree)
        {
            return;
        }

        var input = WorkflowInput.For(tree);
        state.Input = input;

        if (tree.GetHelper() is IInputEvents events)
        {
            state.InputPressedHandler = (_, e) => OnLinkPointerPressed(state, e);
            events.Input.PointerPressed += state.InputPressedHandler;
        }

        state.LinkRemovedHandler = (_, link) => OnLinkRemoved(state, link);
        tree.GetHelper().LinkRemoved += state.LinkRemovedHandler;
    }

    private static void DetachInteraction(SurfaceState state)
    {
        if (state.Tree is not { } tree)
        {
            state.Input = null;
            return;
        }

        var helper = tree.GetHelper();
        if (helper is IInputEvents events && state.InputPressedHandler is not null)
        {
            events.Input.PointerPressed -= state.InputPressedHandler;
        }

        if (state.LinkRemovedHandler is not null)
        {
            helper.LinkRemoved -= state.LinkRemovedHandler;
        }

        // 解绑时菜单还开着就先收：Closed 会顺手把挂起放开。
        state.LinkMenu?.Close();
        state.InputPressedHandler = null;
        state.LinkRemovedHandler = null;
        state.Input = null;
    }

    private static void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        // 只拦目标是自己（整块画布）的那一次：卡片里的控件发起的请求照旧往上冒，
        // 否则平台会把整块画布卷进视口、画布跳原点。
        if (sender is FrameworkElement host && ReferenceEquals(e.TargetObject, host))
        {
            e.Handled = true;
        }
    }

    private static void OnMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement host || !States.TryGetValue(host, out var state) || state.Tree is null)
        {
            return;
        }

        RoutePointer(state, host, e.GetPosition(host), WorldPoint(state, e), (p, t, h) => new Wf.PointerPressedEventArgs(
            p, Modifiers(e.KeyboardModifiers), host, t, ButtonOf(e.ChangedButton), 1, h));
    }

    // 起平移。挂在具名按下源的预览相上（见 ResolveNamedParts），所以一定收得到。
    private static void OnPointerPressSourceDown(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement source
            || FindHost(source) is not { } host
            || !States.TryGetValue(host, out var state)
            || state.ScrollViewer is null)
        {
            return;
        }

        if (e.ChangedButton != PlatformInput.MouseButton.Left)
        {
            return;
        }

        // 按在卡片/端口上的那一按是它们自己的手势 —— 具名源可能包着它们，所以这一道仍要有。
        if (e.OriginalSource is DependencyObject originalSource && !IsBlankSurfaceInteraction(originalSource, state))
        {
            return;
        }

        state.IsPanning = true;
        state.PanStart = e.GetPosition(host);
        state.PanStartHorizontal = state.ScrollViewer.HorizontalOffset;
        state.PanStartVertical = state.ScrollViewer.VerticalOffset;
        host.CaptureMouse();
        e.Handled = true;
    }

    private static void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement host || !States.TryGetValue(host, out var state))
        {
            return;
        }

        if (state.IsPanning)
        {
            PanMoved(host, state, e);
            return;
        }

        if (state.Tree is not { } tree)
        {
            return;
        }

        // 拉线时指针下挂着橡皮筋：把指针的世界坐标喂给树，虚拟连线那一端就跟着走，
        // 池化出来的连线视图按绑定重画 —— 预览是这么来的，表面不自己画一笔。
        var world = WorldPoint(state, e);
        tree.SetPointerCommand.Execute(new Anchor(world.X, world.Y, 0));

        // 橡皮筋挂着的那段时间不转发 PointerMoved，否则沿途经过的实连线会一路亮起。
        if (!tree.VirtualLink.IsVisible)
        {
            RoutePointer(state, host, e.GetPosition(host), world, (p, t, h) => new Wf.PointerMovedEventArgs(
                p, Modifiers(e.KeyboardModifiers), host, t, h));
        }
    }

    private static void PanMoved(FrameworkElement host, SurfaceState state, MouseEventArgs e)
    {
        if (state.ScrollViewer is not { } viewer || state.Tree is not { } tree)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            state.IsPanning = false;
            host.ReleaseMouseCapture();
            return;
        }

        var current = e.GetPosition(host);
        var desiredHorizontal = state.PanStartHorizontal + (state.PanStart.X - current.X);
        var desiredVertical = state.PanStartVertical + (state.PanStart.Y - current.Y);

        // 越界延伸的规范做法（与其余六家同一策略）：按轴长的一个离散增量长大，而不是每越界一帧长一个像素。
        var newHorizontal = WorkflowSurfaceMath.ClampScrollOffset(
            desiredHorizontal, viewer.ScrollableWidth, tree.Layout, horizontal: true,
            extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
        var newVertical = WorkflowSurfaceMath.ClampScrollOffset(
            desiredVertical, viewer.ScrollableHeight, tree.Layout, horizontal: false,
            extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);

        if (newHorizontal != desiredHorizontal || newVertical != desiredVertical)
        {
            UpdateCanvasSize(state);
            ApplyLayout(state);
            viewer.UpdateLayout();
            newHorizontal = Math.Min(newHorizontal, viewer.ScrollableWidth);
            newVertical = Math.Min(newVertical, viewer.ScrollableHeight);
        }

        var appliedHorizontal = WorkflowSurfaceMath.ClampValue(newHorizontal, 0d, viewer.ScrollableWidth);
        var appliedVertical = WorkflowSurfaceMath.ClampValue(newVertical, 0d, viewer.ScrollableHeight);
        viewer.ScrollToHorizontalOffset(appliedHorizontal);
        viewer.ScrollToVerticalOffset(appliedVertical);

        state.PanStart = current;
        state.PanStartHorizontal = appliedHorizontal;
        state.PanStartVertical = appliedVertical;

        UpdateViewport(state, appliedHorizontal, appliedVertical);
        UpdateOverlays(state);
        e.Handled = true;
    }

    private static void OnMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement host || !States.TryGetValue(host, out var state))
        {
            return;
        }

        if (state.Tree is not null)
        {
            RoutePointer(state, host, e.GetPosition(host), WorldPoint(state, e), (p, t, h) => new Wf.PointerReleasedEventArgs(
                p, Modifiers(e.KeyboardModifiers), host, t, ButtonOf(e.ChangedButton), 1, h));
        }

        if (state.IsPanning)
        {
            state.IsPanning = false;
            host.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        // 松手后橡皮筋还挂着 —— 兜底把它收掉，但这**只在松手没落到端口上时**才做：
        // 宿主的处理器在预览相，排在槽自己那个预览处理器**之前**，若不加这一道，就会在槽有机会完成
        // 连接之前先把虚拟连线收掉（实测：所有连线都连不成）。
        // 落在端口上的那三次出口（连成 / 松回自己 / 落在接不了的口）由槽自己收尾。
        if (e.ChangedButton == PlatformInput.MouseButton.Left
            && state.Tree is { } tree
            && tree.VirtualLink.IsVisible
            && (e.OriginalSource is not DependencyObject source || !IsSlotVisual(source)))
        {
            tree.ResetVirtualLinkCommand.Execute(null);
        }
    }

    private static void OnLostMouseCapture(object? sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement host && States.TryGetValue(host, out var state))
        {
            state.IsPanning = false;
        }
    }

    private static void OnMouseLeave(object? sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement host && States.TryGetValue(host, out var state) && state.Tree is { } tree)
        {
            WorkflowInput.For(tree).Route(new Wf.PointerExitedEventArgs(
                new Anchor(), Wf.InputModifiers.None, host, null, new WorkflowEventHandle()));
        }
    }

    private static void OnMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement host || !States.TryGetValue(host, out var state) || state.Input is null)
        {
            return;
        }

        // Ctrl + 滚轮是表面的缩放（预览相已经吃掉并标记 handled）；到这里的都不该再有 Ctrl。
        if (e.KeyboardModifiers == ModifierKeys.Control)
        {
            return;
        }

        RoutePointer(state, host, e.GetPosition(host), WorldPoint(state, e), (p, t, h) => new Wf.PointerWheelEventArgs(
            p, Modifiers(e.KeyboardModifiers), host, t, 0d, e.Delta / 120d, h));
    }

    private static void OnKeyDown(object? sender, PlatformInput.KeyEventArgs e)
    {
        if (sender is not FrameworkElement host || !States.TryGetValue(host, out var state))
        {
            return;
        }

        if (state.Input is not { } input || input.HoveredLink is null)
        {
            return;
        }

        // Delete 只在指针下有连线时才转发：键从卡片里的控件冒泡上来也一样，路由会据指针目标裁决删哪条。
        input.Route(new Wf.KeyDownEventArgs(
            ToKey(e.Key), (int)e.Key, Modifiers(e.KeyboardModifiers), false, host, input.HoveredLink, new WorkflowEventHandle()));

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
        }
    }

    private static void OnKeyUp(object? sender, PlatformInput.KeyEventArgs e)
    {
        if (sender is not FrameworkElement host || !States.TryGetValue(host, out var state) || state.Input is not { } input)
        {
            return;
        }

        input.Route(new Wf.KeyUpEventArgs(
            ToKey(e.Key), (int)e.Key, Modifiers(e.KeyboardModifiers), false, host, input.HoveredLink, new WorkflowEventHandle()));
    }

    // 两个系各取一次：anchor 是宿主系（菜单定位按宿主坐标算屏幕坐标），world 是画布系。
    // 命中判定在 Core 里拿指针去比 node.Anchor / node.Size（模型系），画布自己带着世界位移，
    // 所以画布系坐标**就是**模型系 —— 不需要再减什么，减了就是那个静默的系统性偏移。
    private static void RoutePointer(
        SurfaceState state,
        FrameworkElement host,
        Point anchorPoint,
        Point worldPoint,
        Func<Anchor, IWorkflowViewModel?, WorkflowEventHandle, Wf.PointerEventArgs> args)
    {
        if (state.Input is not { } input)
        {
            return;
        }

        var anchor = new Anchor(anchorPoint.X, anchorPoint.Y, 0);
        var target = input.Tree.HitTestVisibleLinks(worldPoint.X, worldPoint.Y, input.HitRadius);

        input.Route(args(anchor, target, new WorkflowEventHandle()));

        // 命中一条线就把键盘焦点收到表面：Delete 才进得来（与其余六家一致）。
        if (input.HoveredLink is not null)
        {
            host.Focus();
        }
    }

    // 画布自己带着世界位移，所以它的局部系就是模型系。
    private static Point WorldPoint(SurfaceState state, MouseEventArgs e)
        => state.Canvas is null ? e.GetPosition(state.Host) : e.GetPosition(state.Canvas);

    // 「空白」= 按下的东西是表面自己的部件之一，而不是落在卡片或端口上。
    // 判据与 WPF 同形：先排除节点与端口的视觉，再排除滚动条，最后要求命中链上出现
    // 画布 / 滚动视口 / 指定的按下源 / 网格装饰器之一 —— `PointerPressSourceName` 就是在这里被消费的。
    private static bool IsBlankSurfaceInteraction(DependencyObject source, SurfaceState state)
    {
        if (IsNodeOrSlotVisual(source))
        {
            return false;
        }

        var ancestors = EnumerateSelfAndVisualAncestors(source).ToArray();
        if (ancestors.Any(IsNodeOrSlotVisual))
        {
            return false;
        }

        // 按在滚动条上是要滚，不是要平移画布。
        if (source is ScrollBar || ancestors.Any(x => x is ScrollBar))
        {
            return false;
        }

        return IsSurfacePart(source, state)
            || ancestors.Any(x => IsSurfacePart(x, state) || x is ScrollContentPresenter);
    }

    private static bool IsSurfacePart(DependencyObject candidate, SurfaceState state)
        => ReferenceEquals(candidate, state.Canvas)
        || ReferenceEquals(candidate, state.ScrollViewer)
        || ReferenceEquals(candidate, state.PointerPressSource)
        || ReferenceEquals(candidate, state.GridDecoratorElement);

    private static bool IsNodeOrSlotVisual(DependencyObject source)
        => source is FrameworkElement { DataContext: IWorkflowNodeViewModel or IWorkflowSlotViewModel };

    private static bool IsSlotVisual(DependencyObject source)
        => EnumerateSelfAndVisualAncestors(source)
            .OfType<FrameworkElement>()
            .Any(x => x.DataContext is IWorkflowSlotViewModel);

    // 右键菜单归表面：右键落在表面上，而弹出要根视觉坐标、模型给的是画布坐标 —— 只有表面同时知道这两件事。
    // 条目由宿主在资源里声明（LinkMenuKey），库只负责订阅、定位、弹出与开合上报。
    private static void OnLinkPointerPressed(SurfaceState state, Wf.PointerPressedEventArgs e)
    {
        if (e.Button != Wf.MouseButton.Right || e.Target is not IWorkflowLinkViewModel link)
        {
            return;
        }

        // 链上更靠前的一级（连线自己）可以否决这次按下 —— 它说不给菜单，这里就不给。
        if (e.Handle.PreventDefault || state.LinkMenu?.IsOpen == true)
        {
            return;
        }

        var host = state.Host;
        var menuKey = GetLinkMenuKey(host);
        if (string.IsNullOrWhiteSpace(menuKey) || host.FindResource(menuKey!) is not ContextMenu menu)
        {
            return;
        }

        state.LinkMenu = menu;
        state.MenuLink = link;

        // 菜单的 DataContext 就是这条连线 —— 条目据此绑定命令（`Command="{Binding DeleteCommand}"`）。
        // 菜单不在视觉树里，继承不到宿主的 DataContext，所以必须在这里显式给。
        menu.DataContext = link;

        menu.Closed += (_, _) =>
        {
            // 收起即放开挂起：宿主自己记这一笔账，输入路由只管照做。
            if (state.Input is { } input)
            {
                input.IsSuspended = false;
            }

            if (ReferenceEquals(state.LinkMenu, menu))
            {
                state.LinkMenu = null;
            }

            if (ReferenceEquals(state.MenuLink, link))
            {
                state.MenuLink = null;
            }
        };

        // 菜单一开指针就飞到弹层上：先挂起指针跟踪，那之后的移动不会清掉这次选中的线。
        if (state.Input is { } opening)
        {
            opening.IsSuspended = true;
        }

        menu.Open(ToMenuPosition(host, e.Position));
    }

    private static void OnLinkRemoved(SurfaceState state, IWorkflowLinkViewModel link)
    {
        // 树报的是离场的那条线，本家只认自己这份菜单指着的那条。
        if (ReferenceEquals(state.MenuLink, link))
        {
            state.LinkMenu?.Close();
        }
    }

    // 报给输入面的位置是表面坐标，换成屏幕坐标、再回到根视觉坐标 —— 菜单最终由平台的 Popup 换算成屏幕位置。
    private static Point ToMenuPosition(FrameworkElement host, Anchor position)
    {
        var screen = host.PointToScreen(new Point(position.Horizontal, position.Vertical));
        return VisualTreeHelper.GetRoot(host) is Visual root ? root.PointFromScreen(screen) : screen;
    }

    private static Wf.MouseButton ButtonOf(PlatformInput.MouseButton button) => button switch
    {
        PlatformInput.MouseButton.Left => Wf.MouseButton.Left,
        PlatformInput.MouseButton.Right => Wf.MouseButton.Right,
        PlatformInput.MouseButton.Middle => Wf.MouseButton.Middle,
        PlatformInput.MouseButton.XButton1 => Wf.MouseButton.XButton1,
        PlatformInput.MouseButton.XButton2 => Wf.MouseButton.XButton2,
        _ => Wf.MouseButton.None,
    };

    private static Wf.InputModifiers Modifiers(ModifierKeys keys)
    {
        var modifiers = Wf.InputModifiers.None;
        if (keys.HasFlag(ModifierKeys.Alt)) modifiers |= Wf.InputModifiers.Alt;
        if (keys.HasFlag(ModifierKeys.Control)) modifiers |= Wf.InputModifiers.Control;
        if (keys.HasFlag(ModifierKeys.Shift)) modifiers |= Wf.InputModifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Windows)) modifiers |= Wf.InputModifiers.Meta;
        return modifiers;
    }

    // 键按字母/数字/功能键三段连续区间做算术映射（两边枚举的这几段都是连续的），其余逐个点名。
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

    private static IEnumerable<DependencyObject> EnumerateSelfAndVisualAncestors(DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }
}
