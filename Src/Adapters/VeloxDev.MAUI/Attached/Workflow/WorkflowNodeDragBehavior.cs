using VeloxDev.WorkflowSystem;

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
        public PointerGestureRecognizer? PointerGesture { get; set; }
        public PanGestureRecognizer? PanGesture { get; set; }
        public ButtonsMask ActiveButton { get; set; }
        public double LastX { get; set; }
        public double LastY { get; set; }
        public double LastPanX { get; set; }
        public double LastPanY { get; set; }
        public VisualElement? CoordinateHost { get; set; }
        public View? OwnerView { get; set; }
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
        var pointer = new PointerGestureRecognizer();
        pointer.Buttons = ButtonsMask.Primary | ButtonsMask.Secondary;
        pointer.PointerPressed += OnPointerPressed;
        pointer.PointerMoved += OnPointerMoved;
        pointer.PointerReleased += OnPointerReleased;
        pointer.PointerExited += OnPointerReleased;

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;

        view.GestureRecognizers.Add(pointer);
        view.GestureRecognizers.Add(pan);

        view.SetValue(StateProperty, new DragState { PointerGesture = pointer, PanGesture = pan, OwnerView = view });
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
            if (state.PointerGesture is not null)
            {
                state.PointerGesture.PointerPressed -= OnPointerPressed;
                state.PointerGesture.PointerMoved -= OnPointerMoved;
                state.PointerGesture.PointerReleased -= OnPointerReleased;
                state.PointerGesture.PointerExited -= OnPointerReleased;
                view.GestureRecognizers.Remove(state.PointerGesture);
            }

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

        state.LastX = point.Position.X;
        state.LastY = point.Position.Y;
        state.IsDragging = true;
        IsDraggingNode = true;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
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

    private static void OnPointerPressed(object? sender, PointerEventArgs e)
    {
#if WINDOWS
        return;
#else
        if (sender is View view && view.GetValue(StateProperty) is DragState state)
        {
            state.ActiveButton = e.Button;
            if (state.ActiveButton != ButtonsMask.Primary)
            {
                state.IsDragging = false;
                state.CoordinateHost = null;
                return;
            }

            var coordinateHost = ResolveCoordinateHost(view);
            var position = e.GetPosition(coordinateHost);
            if (coordinateHost is null || position is null)
            {
                state.IsDragging = false;
                state.CoordinateHost = null;
                return;
            }

            state.CoordinateHost = coordinateHost;
            state.LastX = position.Value.X;
            state.LastY = position.Value.Y;
            state.LastPanX = 0d;
            state.LastPanY = 0d;
            state.IsDragging = true;
            IsDraggingNode = true;
            TryCapturePointer(view, e);
        }
#endif
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
#if WINDOWS
        return;
#else
        if (sender is not View view || view.GetValue(StateProperty) is not DragState state || !state.IsDragging || state.CoordinateHost is null)
        {
            return;
        }

        var position = e.GetPosition(state.CoordinateHost);
        if (position is not null)
        {
            state.LastX = position.Value.X;
            state.LastY = position.Value.Y;
        }
#endif
    }

    private static void OnPointerReleased(object? sender, PointerEventArgs e)
    {
#if WINDOWS
        return;
#else
        if (sender is View view && view.GetValue(StateProperty) is DragState state)
        {
            TryReleasePointer(view, e);
            ResetDragState(state);
        }
#endif
    }

    private static void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
#if WINDOWS
        return;
#else
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
                state.IsDragging = true;
                IsDraggingNode = true;
                break;
            case GestureStatus.Running:
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
#endif
    }

#if WINDOWS
    private static void TryCapturePointer(View view, PointerEventArgs e)
    {
        if (view.Handler?.PlatformView is UIElement element && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.CapturePointer(pointer);
        }
    }

    private static void TryReleasePointer(View view, PointerEventArgs e)
    {
        if (view.Handler?.PlatformView is UIElement element && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.ReleasePointerCapture(pointer);
        }
    }
#else
    private static void TryCapturePointer(View view, PointerEventArgs e)
    {
    }

    private static void TryReleasePointer(View view, PointerEventArgs e)
    {
    }
#endif

    private static void ResetDragState(DragState state)
    {
        state.ActiveButton = default;
        state.IsDragging = false;
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
