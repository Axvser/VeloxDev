using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace Demo;

public partial class SlotView : UserControl
{
    public SlotView()
    {
        InitializeComponent();
    }

    public static readonly StyledProperty<SlotState> SlotStateProperty =
        AvaloniaProperty.Register<SlotView, SlotState>(nameof(SlotState));

    public SlotState SlotState
    {
        get => this.GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }

    private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not IWorkflowSlotViewModel slot) return;

        slot.ReceiveConnectionCommand.Execute(null);
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (DataContext is not IWorkflowSlotViewModel slot) return;

        slot.ApplyConnectionCommand.Execute(null);
        e.Pointer.Capture(null);
    }
}