using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSlotConnectionBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotConnectionBehavior, Control, bool>("IsEnabled");

    static WorkflowSlotConnectionBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.PointerPressed -= OnPointerPressed;
        control.PointerReleased -= OnPointerReleased;

        if (e.NewValue is true)
        {
            control.PointerPressed += OnPointerPressed;
            control.PointerReleased += OnPointerReleased;
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot })
            return;

        slot.SendConnectionCommand.Execute(null);
        e.Pointer.Capture(null);
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot })
            return;

        slot.ReceiveConnectionCommand.Execute(null);
    }
}
