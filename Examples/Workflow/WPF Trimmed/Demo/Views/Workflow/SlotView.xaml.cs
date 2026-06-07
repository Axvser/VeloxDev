using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class SlotView : UserControl
{
    public static readonly DependencyProperty SlotStateProperty = DependencyProperty.Register(
        nameof(SlotState),
        typeof(SlotState),
        typeof(SlotView),
        new PropertyMetadata(SlotState.StandBy, OnSlotStateChanged));

    public SlotView()
    {
        InitializeComponent();
        UpdateForeground();
    }

    public SlotState SlotState
    {
        get => (SlotState)GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }

    private static void OnSlotStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SlotView)d).UpdateForeground();

    private void UpdateForeground()
    {
        Foreground = SlotState switch
        {
            var state when state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver) => Brushes.Violet,
            var state when state.HasFlag(SlotState.Sender) => Brushes.Tomato,
            var state when state.HasFlag(SlotState.Receiver) => Brushes.Lime,
            _ => Brushes.White,
        };
    }

    private void OnPointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowSlotViewModel context) return;

        context.SendConnectionCommand.Execute(null);

        e.Handled = true;
    }

    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowSlotViewModel context) return;

        context.ReceiveConnectionCommand.Execute(null);

        e.Handled = true;
    }
}