using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace Demo.Views;

public partial class SlotView : UserControl
{
    public SlotView()
    {
        InitializeComponent();
    }

    // 当鼠标按下，将 输入/输入口 设定为 输出口
    private void OnPointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowSlotViewModel context) return;

        context.ApplyConnectionCommand.Execute(null);

        e.Handled = true;
    }

    // 当鼠标按下，将 输入/输入口 设定为 输入口
    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowSlotViewModel context) return;

        context.ReceiveConnectionCommand.Execute(null);

        e.Handled = true;
    }
}