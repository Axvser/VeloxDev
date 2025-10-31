using Demo.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;
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
        SimulateData();
    }

    private void SimulateData()
    {
        // 先模拟一些数据
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
        DataContext = _workflowViewModel;
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

    // 模拟500个节点
    //private void SimulateData()
    //{
    //    var random = new Random();
    //    _workflowViewModel.GetHelper().ClearHistory();

    //    // 1. 先创建500个网格节点
    //    const int cols = 25, rows = 20;
    //    const double canvasWidth = 3000, canvasHeight = 2500;
    //    double cellWidth = canvasWidth / cols, cellHeight = canvasHeight / rows;

    //    var allNodes = new List<object>();
    //    var slotPairs = new List<(IWorkflowSlotViewModel output, IWorkflowSlotViewModel input)>();

    //    for (int i = 0; i < 500; i++)
    //    {
    //        int row = i / cols, col = i % cols;
    //        double x = col * cellWidth + 50;
    //        double y = row * cellHeight + 50;

    //        var node = new NodeViewModel()
    //        {
    //            Size = new Size(120, 90),
    //            Anchor = new Anchor(x, y, 1)
    //        };

    //        _workflowViewModel.GetHelper().CreateNode(node);
    //        allNodes.Add(node);

    //        // 为每个节点创建输出和输入Slot
    //        var outputSlot = new SlotViewModel()
    //        {
    //            Offset = new Offset(30, 35),
    //            Size = new Size(20, 20),
    //            Channel = SlotChannel.MultipleTargets, // 输出类型
    //        };

    //        var inputSlot = new SlotViewModel()
    //        {
    //            Offset = new Offset(70, 35),
    //            Size = new Size(20, 20),
    //            Channel = SlotChannel.MultipleSources, // 输入类型
    //        };

    //        node.GetHelper().CreateSlot(outputSlot);
    //        node.GetHelper().CreateSlot(inputSlot);

    //        // 记录Slot对用于后续连接
    //        slotPairs.Add((outputSlot, inputSlot));
    //    }

    //    DataContext = _workflowViewModel;

    //    // 2. 使用命令模拟连接过程
    //    SimulateConnectionProcess(slotPairs, random);
    //}
    //private void SimulateConnectionProcess(List<(IWorkflowSlotViewModel output, IWorkflowSlotViewModel input)> slotPairs, Random random)
    //{
    //    int connectionCount = random.Next(50, 151); // 创建50-150个连接

    //    for (int i = 0; i < connectionCount; i++)
    //    {
    //        if (slotPairs.Count < 2) break;

    //        // 随机选择两个不同的节点对
    //        int index1 = random.Next(slotPairs.Count);
    //        int index2;
    //        do
    //        {
    //            index2 = random.Next(slotPairs.Count);
    //        } while (index2 == index1);

    //        var pair1 = slotPairs[index1];
    //        var pair2 = slotPairs[index2];

    //        // 模拟连接过程：pair1的输出 -> pair2的输入
    //        Console.WriteLine($"连接 {index1} 的输出口到 {index2} 的输入口");

    //        // 模拟鼠标操作序列
    //        // 1. 在输出Slot上按下（设定为输出口）
    //        pair1.output.ApplyConnectionCommand.Execute(null);

    //        // 2. 在输入Slot上释放（建立连接）
    //        pair2.input.ReceiveConnectionCommand.Execute(null);

    //        // 可选：添加延迟模拟真实操作
    //        if (i % 10 == 0) System.Threading.Thread.Sleep(10);
    //    }
    //}
}