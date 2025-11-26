using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views;

public partial class SlotView : UserControl
{
    private Canvas? _parentCanvas;

    public SlotView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _parentCanvas = this.FindAncestorOfType<Canvas>().FindAncestorOfType<Canvas>();
    }

    // 当触点按下，将 输入/输入口 设定为 输出口
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not IWorkflowSlot context || context.Parent?.Parent is not { } tree) return;

        // Avalonia 必须这么做，否则 Released 无法作用在其它 SlotView
        if (e.Pointer.Captured != null)
        {
            e.Pointer.Capture(null);
        }

        // 无鼠标设备需要在此处更新一次当前位置，否则连接线可能有一帧异常数据
        var point = e.GetPosition(_parentCanvas);
        tree.SetPointerCommand.Execute(new Anchor(point.X, point.Y, 0));

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