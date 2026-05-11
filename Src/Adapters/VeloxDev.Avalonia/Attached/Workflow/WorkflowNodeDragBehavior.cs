using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowNodeDragBehavior : AvaloniaObject
{
    private sealed class DragState
    {
        public bool IsDragging { get; set; }
        public Point LastPosition { get; set; }
        public Control? CoordinateHost { get; set; }
    }

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkflowNodeDragBehavior, InputElement, bool>("IsEnabled");

    public static readonly AttachedProperty<string?> CoordinateHostNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowNodeDragBehavior, InputElement, string?>("CoordinateHostName");

    public static readonly AttachedProperty<Type?> CoordinateHostTypeProperty =
        AvaloniaProperty.RegisterAttached<WorkflowNodeDragBehavior, InputElement, Type?>("CoordinateHostType");

    private static readonly AttachedProperty<DragState?> StateProperty =
        AvaloniaProperty.RegisterAttached<WorkflowNodeDragBehavior, InputElement, DragState?>("State");

    static WorkflowNodeDragBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<InputElement>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetCoordinateHostName(AvaloniaObject element) => element.GetValue(CoordinateHostNameProperty);

    public static void SetCoordinateHostName(AvaloniaObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    public static Type? GetCoordinateHostType(AvaloniaObject element) => element.GetValue(CoordinateHostTypeProperty);

    public static void SetCoordinateHostType(AvaloniaObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    private static void OnIsEnabledChanged(InputElement element, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Attach(element);
            return;
        }

        Detach(element);
    }

    private static void Attach(InputElement element)
    {
        Detach(element);

        element.SetValue(StateProperty, new DragState());
        element.PointerPressed += OnPointerPressed;
        element.PointerMoved += OnPointerMoved;
        element.PointerReleased += OnPointerReleased;
        element.PointerCaptureLost += OnPointerCaptureLost;
    }

    private static void Detach(InputElement element)
    {
        element.PointerPressed -= OnPointerPressed;
        element.PointerMoved -= OnPointerMoved;
        element.PointerReleased -= OnPointerReleased;
        element.PointerCaptureLost -= OnPointerCaptureLost;
        element.ClearValue(StateProperty);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.GetValue(StateProperty) is not DragState state)
            return;

        var pointer = e.GetCurrentPoint(control);
        if (!pointer.Properties.IsLeftButtonPressed)
            return;

        state.CoordinateHost = ResolveCoordinateHost(control);
        if (state.CoordinateHost is null)
            return;

        state.IsDragging = true;
        state.LastPosition = e.GetPosition(state.CoordinateHost);
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control || control.GetValue(StateProperty) is not DragState state || !state.IsDragging || state.CoordinateHost is null)
            return;

        var node = ResolveNode(control);
        if (node is null)
            return;

        var current = e.GetPosition(state.CoordinateHost);
        node.MoveCommand.Execute(new Offset(current.X - state.LastPosition.X, current.Y - state.LastPosition.Y));
        state.LastPosition = current;
        e.Handled = true;
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not InputElement element || element.GetValue(StateProperty) is not DragState state || !state.IsDragging)
            return;

        state.IsDragging = false;
        state.CoordinateHost = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private static void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is not InputElement element || element.GetValue(StateProperty) is not DragState state)
            return;

        state.IsDragging = false;
        state.CoordinateHost = null;
    }

    private static Control? ResolveCoordinateHost(Control control)
    {
        var hostName = GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = ResolveNamedHost(control, hostName);
            if (namedHost is not null)
                return namedHost;
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Canvas);
        return control.GetVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(x => hostType.IsAssignableFrom(x.GetType()));
    }

    private static Control? ResolveNamedHost(Control control, string hostName)
    {
        if (control.Name == hostName)
            return control;

        return control.GetVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(x => x.Name == hostName);
    }

    private static IWorkflowNodeViewModel? ResolveNode(Control control)
    {
        if (control.DataContext is IWorkflowNodeViewModel node)
            return node;

        return control.GetVisualAncestors()
            .OfType<StyledElement>()
            .Select(static x => x.DataContext)
            .OfType<IWorkflowNodeViewModel>()
            .FirstOrDefault();
    }
}
