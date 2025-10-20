using System.Windows.Controls;
using System.Windows.Input;
using Demo.ViewModels;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;
using Size = VeloxDev.Core.WorkflowSystem.Size;

namespace Demo.Views;

public partial class WorkflowView : UserControl
{
    private readonly TreeViewModel _workflowViewModel = new();

    public WorkflowView()
    {
        InitializeComponent();

        // 先模拟一些数据
        var slot1 = new SlotViewModel()
        {
            Offset = new Offset(170, 60),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneTarget,
        };
        var slot2 = new SlotViewModel()
        {
            Offset = new Offset(170, 120),
            Size = new Size(20, 20),
            Channel = SlotChannel.MultipleTargets,
        };
        var slot3 = new SlotViewModel()
        {
            Offset = new Offset(10, 100),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneSource,
        };
        var slot4 = new SlotViewModel()
        {
            Offset = new Offset(10, 200),
            Size = new Size(20, 20),
            Channel = SlotChannel.MultipleSources,
        };
        var node1 = new NodeViewModel()
        {
            Size = new Size(200, 200),
            Anchor = new Anchor(50, 50, 1)
        };
        var node2 = new NodeViewModel()
        {
            Size = new Size(300, 300),
            Anchor = new Anchor(250, 250, 1)
        };

        // 此处为 Nodes 集合增加了了两个 Node 上下文
        _workflowViewModel.GetHelper().CreateNode(node1);
        _workflowViewModel.GetHelper().CreateNode(node2);
        node1.GetHelper().CreateSlot(slot1);
        node1.GetHelper().CreateSlot(slot2);
        node2.GetHelper().CreateSlot(slot3);
        node2.GetHelper().CreateSlot(slot4);

        // 使用数据上下文
        DataContext = _workflowViewModel;

        // 执行并在 3s 内用拖拽建立连接即可看到传播效果 （ 取决于 NodeViewMolde 中的 Work 实现 ）
        // 你可同时启用多个 Work 并观察 NodeViewModel 使用不同信号量时的效果差异
        node1.WorkCommand.Execute(null);
    }

    // 当鼠标移动时，更新 工作流Tree 中记录的鼠标位点
    private void OnPointerMoved(object sender, MouseEventArgs e)
    {
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        var point = e.GetPosition(this);
        tree.SetPointerCommand.Execute(new Anchor(point.X, point.Y, 0));
    }

    // 当鼠标离开时，清除 工作流Tree 中记录的起始 输入/输出口
    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        tree.GetHelper().ResetVirtualLink();
    }

    private void Button_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        tree.UndoCommand.Execute(null);
    }

    private void Button_Click_1(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not IWorkflowTreeViewModel tree) return;
        tree.RedoCommand.Execute(null);
    }
}