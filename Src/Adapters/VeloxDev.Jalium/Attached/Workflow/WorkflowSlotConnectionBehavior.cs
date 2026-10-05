using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Attached behavior that turns a slot view into the two ends of a connection gesture: a left-press starts one
/// (<see cref="IWorkflowSlotViewModel.SendConnectionCommand"/>) and the matching release finishes one
/// (<see cref="IWorkflowSlotViewModel.ReceiveConnectionCommand"/>).
/// </summary>
/// <remarks>
/// <para>
/// The behavior reads the slot from the element's <see cref="FrameworkElement.DataContext"/>, so a pooled slot view
/// is always wired to whichever slot it is currently showing.
/// </para>
/// <para>
/// Whether the gesture is legal — channels, direction, whether a link already exists — is the model's business.
/// This behavior only translates the pointer, and deliberately does not look at either end.
/// </para>
/// </remarks>
public sealed class WorkflowSlotConnectionBehavior : DependencyObject
{
    /// <summary>The attached property that turns slot-connection dragging on for a control.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotConnectionBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Reads the <c>IsEnabled</c> attached property from <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(DependencyObject element) => element.GetValue(IsEnabledProperty) is true;

    /// <summary>Sets the <c>IsEnabled</c> attached property on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control)
        {
            return;
        }

        control.PreviewMouseLeftButtonDown -= OnPointerPressed;
        control.PreviewMouseLeftButtonUp -= OnPointerReleased;

        if (e.NewValue is true)
        {
            control.PreviewMouseLeftButtonDown += OnPointerPressed;
            control.PreviewMouseLeftButtonUp += OnPointerReleased;
        }
    }

    private static void OnPointerPressed(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot })
        {
            return;
        }

        slot.SendConnectionCommand.Execute(null);
        e.Handled = true;
    }

    private static void OnPointerReleased(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot })
        {
            return;
        }

        slot.ReceiveConnectionCommand.Execute(null);
        e.Handled = true;
    }
}
