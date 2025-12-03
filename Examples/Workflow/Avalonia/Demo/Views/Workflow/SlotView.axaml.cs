using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace Demo;

public partial class SlotView : UserControl
{
    public SlotView()
    {
        InitializeComponent();
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

        // 标记事件为已处理，防止事件冒泡干扰
        e.Handled = true;
    }
}