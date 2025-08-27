using Avalonia.Controls;
using Avalonia.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace Demo.Views;

public partial class SlotView : UserControl
{
    public SlotView()
    {
        InitializeComponent();
    }

    // 当触点按下，将 输入/输入口 设定为 输出口
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not IWorkflowSlot context) return;

        // Avalonia 必须这么做，否则 Released 无法作用在其它 SlotView
        if (e.Pointer.Captured != null)
        {
            e.Pointer.Capture(null);
        }

        context.ConnectingCommand.Execute(null);

        e.Handled = true;
    }

    // 当触点按下，将 输入/输入口 设定为 输入口
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not IWorkflowSlot context) return;

        context.ConnectedCommand.Execute(null);

        // Avalonia 建议这么做，否则可能影响工作流树中的交互，当然，目前似乎没有出现任何问题
        if (e.Pointer.Captured != null)
        {
            e.Pointer.Capture(null);
        }

        e.Handled = true;
    }
}