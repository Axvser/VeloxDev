using Demo.ViewModels;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Covers the connection-validation layer at connection-build (design) time —
/// <see cref="IWorkflowTreeViewModelHelper.ValidateConnection"/>. Unlike the runtime
/// <see cref="IAccessContext"/> (the AccessAsync edge gate, see BroadcastModeTests): the tree
/// helper vetoes an edge when the user drags a link on the canvas (returns false → no link is
/// created, the virtual connection resets). This is the first gate before data enters any node.
/// The default implementation always returns true, and the demo does not override it — a local
/// helper is needed to exercise this.
/// </summary>
[TestClass]
public class TreeConnectionValidationTests
{
    [TestMethod]
    public void ValidateConnection_RejectsEdge_NoLinkIsCreated()
    {
        var tree = new TreeDefaultViewModel();
        tree.SetHelper(new RejectingTreeHelper());
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1 };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(sink);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        // The tree helper vetoes this edge: reject connecting start.OutputSlot → sink.InputSlot.
        ((RejectingTreeHelper)helper).RejectSender = start.OutputSlot!;

        helper.SendConnection(start.OutputSlot!);
        helper.ReceiveConnection(sink.InputSlot!);

        Assert.AreEqual(0, tree.Links.Count, "vetoed edge never materializes as a link");
        Assert.AreEqual(0, start.OutputSlot!.Targets.Count, "sender slot gained no target");
        Assert.AreEqual(0, sink.InputSlot!.Sources.Count, "receiver slot gained no source");
        Assert.IsFalse(tree.VirtualLink.IsVisible, "virtual connection reset after the veto");
        Assert.AreEqual(SlotState.StandBy, start.OutputSlot.State,
            "sender slot returns to standby after the veto");
    }

    [TestMethod]
    public void ValidateConnection_AllowsEdge_LinkIsCreated()
    {
        var tree = new TreeDefaultViewModel();
        tree.SetHelper(new RejectingTreeHelper());
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1 };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(sink);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        // Control group: no veto — the same edge connects normally.
        helper.SendConnection(start.OutputSlot!);
        helper.ReceiveConnection(sink.InputSlot!);

        Assert.AreEqual(1, tree.Links.Count, "non-vetoed edge connects normally");
        Assert.AreEqual(1, start.OutputSlot!.Targets.Count, "sender slot gained its target");
        Assert.AreEqual(1, sink.InputSlot!.Sources.Count, "receiver slot gained its source");
    }

    [TestMethod]
    public void ValidateConnection_RejectsSecondEdge_KeepsFirstAndResetsVirtualLink()
    {
        var tree = new TreeDefaultViewModel();
        tree.SetHelper(new RejectingTreeHelper());
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1 };
        var a = new NodeViewModel { Title = "A", DelayMilliseconds = 1 };
        var b = new NodeViewModel { Title = "B", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(a); helper.CreateNode(b);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.MultipleTargets);
        a.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        b.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        // Reject only the start → b edge.
        ((RejectingTreeHelper)helper).RejectSender = start.OutputSlot!;
        ((RejectingTreeHelper)helper).RejectReceiver = b.InputSlot!;

        helper.SendConnection(start.OutputSlot!);
        helper.ReceiveConnection(a.InputSlot!);   // allowed → connects
        Assert.AreEqual(1, tree.Links.Count, "first edge connected");

        helper.SendConnection(start.OutputSlot!);
        helper.ReceiveConnection(b.InputSlot!);   // vetoed → does not connect

        Assert.AreEqual(1, tree.Links.Count, "second edge vetoed — only the first link remains");
        Assert.IsFalse(tree.VirtualLink.IsVisible, "virtual connection reset after the veto");
    }
}

/// <summary>
/// Test tree helper: vetoes connections by (Sender, Receiver) combination.
/// Allows by default; only rejects <see cref="RejectSender"/> (any receiver) or the combination of
/// <see cref="RejectSender"/> + <see cref="RejectReceiver"/>.
/// </summary>
public class RejectingTreeHelper : TreeHelper
{
    public IWorkflowSlotViewModel? RejectSender { get; set; }
    public IWorkflowSlotViewModel? RejectReceiver { get; set; }

    public override bool ValidateConnection(
        IWorkflowSlotViewModel sender,
        IWorkflowSlotViewModel receiver)
    {
        if (RejectSender is null || !ReferenceEquals(sender, RejectSender))
            return true;
        // Rejected sender matched: with no RejectReceiver, reject all targets; otherwise reject only the exact pair.
        return RejectReceiver is not null && !ReferenceEquals(receiver, RejectReceiver);
    }
}
