using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace TemplateSimulator.Views;

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

        // WPF中不需要显式释放捕获，系统会自动管理
        // 但为了保持逻辑一致，可以检查并释放捕获
        if (Mouse.Captured != null)
        {
            Mouse.Capture(null);
        }

        context.PressCommand.Execute(null);

        e.Handled = true;
    }

    // 当鼠标按下，将 输入/输入口 设定为 输入口
    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowSlotViewModel context) return;

        context.ReleaseCommand.Execute(null);

        // WPF中建议释放鼠标捕获以确保交互正常
        if (Mouse.Captured != null)
        {
            Mouse.Capture(null);
        }

        e.Handled = true;
    }
}