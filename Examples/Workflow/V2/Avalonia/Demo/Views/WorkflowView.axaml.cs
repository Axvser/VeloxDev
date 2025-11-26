using Avalonia.Controls;
using Avalonia.Input;
using Demo.ViewModels;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views;

public partial class WorkflowView : UserControl
{
    private readonly WorkflowViewModel _workflowViewModel = new WorkflowViewModel();

    public WorkflowView()
    {
        InitializeComponent();

        // 先模拟一些数据
        var slot1 = new SlotViewModel()
        {
            Offset = new Anchor(170, 120),
            Size = new Size(20, 20)
        };
        var slot2 = new SlotViewModel()
        {
            Offset = new Anchor(10, 200),
            Size = new Size(20, 20)
        };
        var node1 = new NodeViewModel()
        {
            Size = new Size(200, 200),
            Anchor = new Anchor(50, 50, 1),
            Name = "任务"
        };
        var node2 = new NodeViewModel()
        {
            Size = new Size(300, 300),
            Anchor = new Anchor(250, 250, 1),
            Name = "任务"
        };
        node1.Slots.Add(slot1);
        node2.Slots.Add(slot2);

        // 此处为 Nodes 集合增加了了两个 Node 上下文
        _workflowViewModel.Nodes.Add(node1);
        _workflowViewModel.Nodes.Add(node2);
        // 使用数据上下文
        DataContext = _workflowViewModel;

        // 执行并在 3s 内用拖拽建立连接即可看到传播效果 （ 取决于 NodeViewMolde 中的 Work 实现 ）
        // 你可同时启用多个 Work 并观察 NodeViewModel 使用不同信号量时的效果差异
        node1.WorkCommand.Execute(null);
    }

    // 当触点移动时，更新 工作流Tree 中记录的鼠标位点
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not IWorkflowTree tree) return;
        var point = e.GetPosition(this);
        tree.SetPointerCommand.Execute(new Anchor(point.X, point.Y, 0));
    }

    // 当触点离开时，清除 工作流Tree 中记录的起始 输入/输出口
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not IWorkflowTree tree) return;
        tree.VirtualLink.Sender = null;
    }
}