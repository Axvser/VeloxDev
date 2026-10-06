using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors
{
public sealed class WorkflowSlotConnectionBehavior : DependencyObject
{
    /// <summary>Identifies the <c>IsEnabled</c> attached property that turns on slot-connection dragging for a control.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotConnectionBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Reads the <c>IsEnabled</c> attached property from <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
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

        if (Equals(e.NewValue, true))
        {
            control.PreviewMouseLeftButtonDown += OnPointerPressed;
            control.PreviewMouseLeftButtonUp += OnPointerReleased;
        }
    }

    private static void OnPointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Control { DataContext: IWorkflowSlotViewModel slot } control)
        {
            return;
        }

        // 这一笔按下已经由表面路由过了 —— 它的隧道处理器在更外层，比这里先跑，句柄就存在表面里。
        // 订阅者在插槽自己的 InputRelay 上置 PreventDefault 就是「这一次别连」；不读它，
        // 插槽上就没有任何可定制的地方。
        //
        // 传的是控件而不是 slot：slot 是 IWorkflowSlotViewModel，一个不派生自 DependencyObject 的
        // 模型对象，`slot as DependencyObject` 恒为 null —— 那样写这道检查会静默地永不生效。
        if (WorkflowSurfaceBehavior.GetPressHandle(control)?.PreventDefault == true)
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
