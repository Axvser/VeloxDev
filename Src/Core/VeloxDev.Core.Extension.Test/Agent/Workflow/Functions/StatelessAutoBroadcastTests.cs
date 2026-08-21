using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies the automatic downstream cascade of AutoBroadcast in stateless mode:
/// triggering the chain head's ReceiveCommand → each node auto-broadcasts after processing → the
/// cascade reaches Sink (no manual Forward needed). Uses a self-built linear chain, independent of
/// the demo session.
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

        // Equivalent to the card's Run: trigger the chain head's ReceiveCommand (data is ITaskContext.Data).
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
