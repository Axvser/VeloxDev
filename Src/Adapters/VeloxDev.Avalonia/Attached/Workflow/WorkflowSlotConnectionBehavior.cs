using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Linq;
using PlatformInput = Avalonia.Input;
using Wf = VeloxDev.WorkflowSystem;
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

    private static void OnPointerPressed(object? sender, PlatformInput.PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot } control)
            return;

        // 这一笔按下已经由表面在隧道相里路由过 —— 它比这里更早，句柄就存在表面里。订阅者在插槽自己的
        // InputRelay 上置 PreventDefault 就是「这一次别连」；不读它，插槽上就没有任何可定制的地方。
        if (WorkflowSurfaceBehavior.GetPressHandle(control)?.PreventDefault == true)
            return;

        slot.SendConnectionCommand.Execute(null);
        e.Pointer.Capture(null);
    }

    private static void OnPointerReleased(object? sender, PlatformInput.PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot })
            return;

        slot.ReceiveConnectionCommand.Execute(null);
    }
}
