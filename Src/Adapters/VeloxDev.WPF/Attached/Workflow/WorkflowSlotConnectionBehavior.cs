using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors
{
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

        control.PreviewMouseLeftButtonDown -= OnPointerPressed;
        control.PreviewMouseLeftButtonUp -= OnPointerReleased;

        if (Equals(e.NewValue, true))
        {
            control.PreviewMouseLeftButtonDown += OnPointerPressed;
            control.PreviewMouseLeftButtonUp += OnPointerReleased;
        }
    }

    private static void OnPointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot })
        {
            return;
        }

        slot.SendConnectionCommand.Execute(null);
        e.Handled = true;
    }

    private static void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot })
        {
            return;
        }

        slot.ReceiveConnectionCommand.Execute(null);
        e.Handled = true;
    }
}
}
