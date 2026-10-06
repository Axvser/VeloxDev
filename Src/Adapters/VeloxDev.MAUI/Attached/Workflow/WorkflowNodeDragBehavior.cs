using VeloxDev.WorkflowSystem;
using PlatformInput = Microsoft.Maui.Controls;
using Wf = VeloxDev.WorkflowSystem;

#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
#endif

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowNodeDragBehavior
{
    internal static bool IsDraggingNode { get; private set; }

#if WINDOWS
    private static readonly Dictionary<UIElement, DragState> PlatformStates = [];
#endif

    private sealed class DragState
    {
        public bool IsDragging { get; set; }
        public PanGestureRecognizer? PanGesture { get; set; }
        public ButtonsMask ActiveButton { get; set; }
        public double LastX { get; set; }
        public double LastY { get; set; }
        public double LastPanX { get; set; }
        public double LastPanY { get; set; }
        public VisualElement? CoordinateHost { get; set; }
        public View? OwnerView { get; set; }

        // 这一笔按下被订阅者否决了（非 Windows 在 Pan 的 Started 上读到）：整笔不拖，Started 之后
        // 的 Running 也不再启用拖拽。
        public bool IsPressPrevented { get; set; }
#if WINDOWS
        public UIElement? PlatformElement { get; set; }
#endif
    }

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowNodeDragBehavior),
        false,
        propertyChanged: OnIsEnabledChanged);

    public static readonly BindableProperty CoordinateHostNameProperty = BindableProperty.CreateAttached(
        "CoordinateHostName",
        typeof(string),
        typeof(WorkflowNodeDragBehavior),
        null);

    public static readonly BindableProperty CoordinateHostTypeProperty = BindableProperty.CreateAttached(
        "CoordinateHostType",
        typeof(Type),
        typeof(WorkflowNodeDragBehavior),
        null);

    private static readonly BindableProperty StateProperty = BindableProperty.CreateAttached(
        "State",
        typeof(DragState),
        typeof(WorkflowNodeDragBehavior),
        null);

    public static bool GetIsEnabled(BindableObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(BindableObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetCoordinateHostName(BindableObject element) => (string?)element.GetValue(CoordinateHostNameProperty);

    public static void SetCoordinateHostName(BindableObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    public static Type? GetCoordinateHostType(BindableObject element) => (Type?)element.GetValue(CoordinateHostTypeProperty);

    public static void SetCoordinateHostType(BindableObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not View view)
        {
            return;
        }

        Detach(view);
        if (newValue is true)
        {
            Attach(view);
        }
    }

    private static void Attach(View view)
    {
#if WINDOWS
        var state = new DragState { OwnerView = view };
        view.SetValue(StateProperty, state);
        view.HandlerChanged += OnHandlerChanged;
        HookPlatformEvents(view, state);
#else
        // 非 Windows 平台以 PanGestureRecognizer 为主：它跨 Android/iOS/MacCatalyst 稳定跟踪 TotalX/TotalY；
        // 不用 PointerGestureRecognizer —— 它的 PointerMoved/Released 与 Pan 生命周期相互竞争、没有收益，Pan 本身把 start/running/end 处理得很干净。
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;

        view.GestureRecognizers.Add(pan);

        view.SetValue(StateProperty, new DragState { PanGesture = pan, OwnerView = view });
#endif
    }

    private static void Detach(View view)
    {
#if WINDOWS
        view.HandlerChanged -= OnHandlerChanged;
        if (view.GetValue(StateProperty) is DragState state)
        {
            UnhookPlatformEvents(state);
        }
#else
        if (view.GetValue(StateProperty) is DragState state)
        {
            if (state.PanGesture is not null)
            {
                state.PanGesture.PanUpdated -= OnPanUpdated;
                view.GestureRecognizers.Remove(state.PanGesture);
            }
        }
#endif

        view.ClearValue(StateProperty);
    }

#if WINDOWS
    private static void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is View view && view.GetValue(StateProperty) is DragState state)
        {
            HookPlatformEvents(view, state);
        }
    }

    private static void HookPlatformEvents(View view, DragState state)
    {
        var element = view.Handler?.PlatformView as UIElement;
        if (ReferenceEquals(state.PlatformElement, element))
        {
            return;
        }

        UnhookPlatformEvents(state);
        if (element is null)
        {
            return;
        }

        element.PointerPressed += OnPlatformPointerPressed;
        element.PointerMoved += OnPlatformPointerMoved;
        element.PointerReleased += OnPlatformPointerReleased;
        element.PointerCaptureLost += OnPlatformPointerCaptureLost;
        state.PlatformElement = element;
        PlatformStates[element] = state;
    }

    private static void UnhookPlatformEvents(DragState state)
    {
        if (state.PlatformElement is null)
        {
            return;
        }

        state.PlatformElement.PointerPressed -= OnPlatformPointerPressed;
        state.PlatformElement.PointerMoved -= OnPlatformPointerMoved;
        state.PlatformElement.PointerReleased -= OnPlatformPointerReleased;
        state.PlatformElement.PointerCaptureLost -= OnPlatformPointerCaptureLost;
        PlatformStates.Remove(state.PlatformElement);
        state.PlatformElement = null;
    }

    private static void OnPlatformPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element || !PlatformStates.TryGetValue(element, out var state))
        {
            return;
        }

        var point = e.GetCurrentPoint(null);
        if (!point.Properties.IsLeftButtonPressed)
        {
            state.IsDragging = false;
            return;
        }

        // 这一笔按下由节点自己转发（本家没有隧道路由相：链接层挂在交互源上的转发比这里晚，而这里的
        // 句柄必须先有）。订阅者在节点自己的 InputRelay 上置 PreventDefault，就是「这一次别拖」——
        // 否决了连指针捕获也不做，那一笔要走的定制才拿得到指针。
        if (state.OwnerView is { } owner
            && WorkflowSurfaceBehavior.RouteComponentPress(owner, CanvasPoint(owner, e))?.PreventDefault == true)
        {
            state.IsDragging = false;
            return;
        }

        state.LastX = point.Position.X;
        state.LastY = point.Position.Y;
        state.IsDragging = true;
        IsDraggingNode = true;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    // 指针在画布元素里的坐标：转发那一笔按下的落点，与适配器各处（插槽中心、平移锚）同一条坐标系。
    private static Point? CanvasPoint(View view, PointerRoutedEventArgs e)
    {
        if (WorkflowSurfaceBehavior.ResolveCanvasForRouting(view)?.Handler?.PlatformView is not UIElement canvas)
        {
            return null;
        }

        var point = e.GetCurrentPoint(canvas).Position;
        return new Point(point.X, point.Y);
    }

    private static void OnPlatformPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element || !PlatformStates.TryGetValue(element, out var state) || !state.IsDragging)
        {
            return;
        }

        if (state.OwnerView is null || ResolveNode(state.OwnerView) is not IWorkflowNodeViewModel node)
        {
            return;
        }

        var point = e.GetCurrentPoint(null);
        if (!point.Properties.IsLeftButtonPressed)
        {
            state.IsDragging = false;
            element.ReleasePointerCapture(e.Pointer);
            return;
        }

        var deltaX = point.Position.X - state.LastX;
        var deltaY = point.Position.Y - state.LastY;
        if (Math.Abs(deltaX) <= double.Epsilon && Math.Abs(deltaY) <= double.Epsilon)
        {
            return;
        }

        node.MoveCommand.Execute(new Offset(deltaX, deltaY));
        state.LastX = point.Position.X;
        state.LastY = point.Position.Y;

        if (FindAncestorContentView(state.OwnerView) is { } owner)
        {
            WorkflowSlotLayoutBehavior.Refresh(owner);
        }

        e.Handled = true;
    }

    private static void OnPlatformPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element || !PlatformStates.TryGetValue(element, out var state))
        {
            return;
        }

        state.IsDragging = false;
        IsDraggingNode = false;
        element.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private static void OnPlatformPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element && PlatformStates.TryGetValue(element, out var state))
        {
            state.IsDragging = false;
            IsDraggingNode = false;
        }
    }
#endif

    private static void OnPointerPressed(object? sender, PlatformInput.PointerEventArgs e)
    {
        // 空操作：PointerGestureRecognizer 只在 Windows 经平台钩子使用；非 Windows 由 PanGestureRecognizer 覆盖整个拖拽生命周期。
    }

    private static void OnPointerMoved(object? sender, PlatformInput.PointerEventArgs e)
    {
        // 空操作：见 OnPointerPressed。
    }

    private static void OnPointerReleased(object? sender, PlatformInput.PointerEventArgs e)
    {
        // 空操作：见 OnPointerPressed。
    }

    private static void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        // 这个处理器只在非 Windows 平台订阅；Windows 上由原生 PointerRoutedEvents 负责节点拖拽。
        if (sender is not View view || view.GetValue(StateProperty) is not DragState state)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                state.CoordinateHost ??= ResolveCoordinateHost(view);
                state.LastPanX = 0d;
                state.LastPanY = 0d;
                // 非 Windows 没有可用的按下事件，Started 是这一笔的第一个点 —— 由节点自己转发它。
                // 转发没有指针位置（PanUpdated 不给），锚点退回节点自己的锚点。订阅者在节点自己的
                // InputRelay 上置 PreventDefault，就是「这一次别拖」。
                state.IsPressPrevented =
                    WorkflowSurfaceBehavior.RouteComponentPress(view, null)?.PreventDefault == true;
                state.IsDragging = !state.IsPressPrevented;
                IsDraggingNode = state.IsDragging;
                break;
            case GestureStatus.Running:
                if (state.IsPressPrevented)
                {
                    break;
                }

                // PanGestureRecognizer 总是先发 Started，下面的 !state.IsDragging 分支只是防御 —— 针对某些 MAUI 平台可能不发 Started 就发 Running 的理论情况。
                if (!state.IsDragging)
                {
                    state.CoordinateHost ??= ResolveCoordinateHost(view);
                    state.IsDragging = true;
                    IsDraggingNode = true;
                }

                if (ResolveNode(view) is not IWorkflowNodeViewModel node)
                {
                    return;
                }

                var deltaX = e.TotalX - state.LastPanX;
                var deltaY = e.TotalY - state.LastPanY;
                if (Math.Abs(deltaX) <= double.Epsilon && Math.Abs(deltaY) <= double.Epsilon)
                {
                    return;
                }

                node.MoveCommand.Execute(new Offset(deltaX, deltaY));
                state.LastPanX = e.TotalX;
                state.LastPanY = e.TotalY;

                if (FindAncestorContentView(view) is { } owner)
                {
                    WorkflowSlotLayoutBehavior.Refresh(owner);
                }
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                ResetDragState(state);
                break;
        }
    }

#if WINDOWS
    private static void TryCapturePointer(View view, PlatformInput.PointerEventArgs e)
    {
        if (view.Handler?.PlatformView is UIElement element && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.CapturePointer(pointer);
        }
    }

    private static void TryReleasePointer(View view, PlatformInput.PointerEventArgs e)
    {
        if (view.Handler?.PlatformView is UIElement element && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.ReleasePointerCapture(pointer);
        }
    }
#else
    private static void TryCapturePointer(View view, PlatformInput.PointerEventArgs e)
    {
    }

    private static void TryReleasePointer(View view, PlatformInput.PointerEventArgs e)
    {
    }
#endif

    private static void ResetDragState(DragState state)
    {
        state.ActiveButton = default;
        state.IsDragging = false;
        state.IsPressPrevented = false;
        state.LastPanX = 0d;
        state.LastPanY = 0d;
        IsDraggingNode = false;
        state.CoordinateHost = null;
    }

    private static VisualElement? ResolveCoordinateHost(View view)
    {
        var hostName = GetCoordinateHostName(view);
        var hostType = GetCoordinateHostType(view) ?? typeof(AbsoluteLayout);
        Element? current = view;
        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(hostName) && current is Element namedScope)
            {
                var namedHost = namedScope.FindByName<VisualElement>(hostName);
                if (namedHost is not null)
                {
                    return namedHost;
                }
            }

            if (current is VisualElement visual && hostType.IsAssignableFrom(visual.GetType()))
            {
                return visual;
            }

            current = current.Parent;
        }

        return null;
    }

    private static ContentView? FindAncestorContentView(Element element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is ContentView contentView)
            {
                return contentView;
            }

            current = current.Parent;
        }

        return null;
    }

    private static IWorkflowNodeViewModel? ResolveNode(Element element)
    {
        var current = element;
        while (current is not null)
        {
            if (current.BindingContext is IWorkflowNodeViewModel node)
            {
                return node;
            }

            current = current.Parent;
        }

        return null;
    }
}
