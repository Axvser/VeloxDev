using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// 验证无状态模式下 AutoBroadcast 的自动向下游级联：
/// 触发链头 ReceiveCommand → 每个节点处理完自动广播 → 级联到达 Sink（无需手动 Forward）。
/// 用自建线性链，不依赖 demo 会话。
/// </summary>
[TestClass]
public class StatelessAutoBroadcastTests
{
    [TestMethod]
    public async Task StatelessAutoBroadcast_CascadesFromHeadToSink()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1 };
        var mid = new NodeViewModel { Title = "Mid", DelayMilliseconds = 1 };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(mid); helper.CreateNode(sink);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        mid.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        mid.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, start.OutputSlot!, mid.InputSlot!);
        Connect(tree, mid.OutputSlot!, sink.InputSlot!);

        Assert.IsTrue(start.AutoBroadcast, "AutoBroadcast defaults to true");

        // 等价于卡片 Run：触发链头 ReceiveCommand（data 即 ITaskContext.Data）。
        start.ReceiveCommand.Execute(new TaskContext(data: "seed"));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && sink.LastStatus != "Completed")
            await Task.Delay(50);

        Assert.AreEqual("Completed", mid.LastStatus, "mid node ran via cascade");
        Assert.AreEqual("Completed", sink.LastStatus,
            "auto-broadcast cascade reached the sink without manual Forward");
    }

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}
