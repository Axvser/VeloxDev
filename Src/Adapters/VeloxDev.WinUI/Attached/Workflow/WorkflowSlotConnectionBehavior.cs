using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSlotConnectionBehavior : DependencyObject
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotConnectionBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control)
        {
            return;
        }

        control.PointerPressed -= OnPointerPressed;
        control.PointerReleased -= OnPointerReleased;

        if (e.NewValue is true)
        {
            control.PointerPressed += OnPointerPressed;
            control.PointerReleased += OnPointerReleased;
        }
    }

    private static void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot })
        {
            return;
        }

        slot.SendConnectionCommand.Execute(null);
        if (sender is UIElement element)
        {
            element.ReleasePointerCaptures();
        }
    }

    private static void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot })
        {
            return;
        }

        slot.ReceiveConnectionCommand.Execute(null);
    }
}
