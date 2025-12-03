using Demo.ViewModels;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;
using Size = VeloxDev.Core.WorkflowSystem.Size;

namespace Demo.Views.Workflow;

public partial class WorkflowView : ContentView
{
    private TreeViewModel _workflowViewModel = new();

    public WorkflowView()
    {
        InitializeComponent();
    }

    private void SaveWorkflow(object sender, EventArgs e)
    {

    }

    private void SelectWorkflow(object sender, EventArgs e)
    {

    }

    private void SimulateData(object sender, EventArgs e)
    {
        var slot1 = new SlotViewModel()
        {
            Offset = new Offset(170, 60),
            Size = new Size(20, 20),
            Channel = SlotChannel.OneBoth,
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
            Channel = SlotChannel.OneBoth,
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

        // 控制器节点，仅用于启动、终结
        var node3 = new ControllerViewModel()
        {
            Size = new Size(400, 200),
            Anchor = new Anchor(400, 400, 1)
        };
        var slot5 = new SlotViewModel()
        {
            Offset = new Offset(335, 55),
            Size = new Size(30, 30),
            Channel = SlotChannel.MultipleTargets,
        };

        // 给 Tree 挂载 Node
        _workflowViewModel.GetHelper().CreateNode(node1);
        _workflowViewModel.GetHelper().CreateNode(node2);
        _workflowViewModel.GetHelper().CreateNode(node3);

        // 给 Node 挂载 Slot
        node1.GetHelper().CreateSlot(slot1);
        node1.GetHelper().CreateSlot(slot2);
        node2.GetHelper().CreateSlot(slot3);
        node2.GetHelper().CreateSlot(slot4);
        node3.GetHelper().CreateSlot(slot5);

        // 清理历史栈，避免非法的重做与撤销
        _workflowViewModel.GetHelper().ClearHistory();

        // 使用数据上下文
        BindingContext = _workflowViewModel;
    }

}