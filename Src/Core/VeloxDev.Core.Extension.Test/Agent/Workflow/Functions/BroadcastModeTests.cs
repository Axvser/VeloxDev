using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Additional coverage for the stateless (Push/broadcast) execution mode:
/// 1) ReverseBroadcastAsync reverse single-hop propagation (walks Sources to trigger the upstream
///    ReceiveCommand; no automatic cascade);
/// 2) The broadcast runtime AccessAsync edge gate (a failed check is treated as "not connected" —
///    that receiver is skipped);
/// 3) Fan-out broadcast (one MultipleTargets output slot → every downstream target receives);
/// 4) With AutoBroadcast=false the cascade stops and requires a manual BroadcastCommand to proceed.
/// The compile (Pull/engine-driven) mode is already covered by CompilerExTests / ParallelFanOutTests / RedirectTests.
/// </summary>
[TestClass]
public class BroadcastModeTests
{
    [TestMethod]
    public async Task ReverseBroadcast_PropagatesUpstreamOneHop()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1, AutoBroadcast = false };
        var mid = new NodeViewModel { Title = "Mid", DelayMilliseconds = 1, AutoBroadcast = false };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1, AutoBroadcast = false };
        helper.CreateNode(start); helper.CreateNode(mid); helper.CreateNode(sink);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        mid.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        mid.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, start.OutputSlot!, mid.InputSlot!);
        Connect(tree, mid.OutputSlot!, sink.InputSlot!);

        // Reverse broadcast: Sink walks its InputSlot.Sources (i.e. Mid.OutputSlot) to trigger Mid's ReceiveCommand.
        sink.ReverseBroadcastCommand.Execute(new TaskContext(data: "pull"));

        Assert.IsTrue(await WaitUntilAsync(() => mid.LastStatus == "Completed"),
            "reverse broadcast ran the upstream neighbor (Mid)");
        Assert.AreEqual("Idle", start.LastStatus, "reverse broadcast is single-hop — Start never ran");
        Assert.AreEqual("Idle", sink.LastStatus, "Mid has AutoBroadcast=false — no forward re-cascade to Sink");
    }

    [TestMethod]
    public async Task Broadcast_SkipsReceiverDeniedByAccessGate()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1 };
        var gate = new DenyGateNodeViewModel { Title = "Gate", DelayMilliseconds = 1 };
        var allowed = new NodeViewModel { Title = "Allowed", DelayMilliseconds = 1 };
        var denied = new NodeViewModel { Title = "Denied", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(gate); helper.CreateNode(allowed); helper.CreateNode(denied);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        gate.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        gate.OutputSlot.SetChannelCommand.Execute(SlotChannel.MultipleTargets);
        allowed.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        denied.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, start.OutputSlot!, gate.InputSlot!);
        Connect(tree, gate.OutputSlot!, allowed.InputSlot!);
        Connect(tree, gate.OutputSlot!, denied.InputSlot!);

        // Runtime gate: Gate rejects the edge toward Denied (AccessAsync returns false → treated as not connected).
        ((DenyGateHelper)gate.GetHelper()).DeniedReceiver = denied;

        start.ReceiveCommand.Execute(new TaskContext(data: "seed"));

        Assert.IsTrue(await WaitUntilAsync(() => allowed.LastStatus == "Completed"),
            "allowed receiver ran via broadcast");
        Assert.AreEqual("Completed", gate.LastStatus, "gate itself ran");
        Assert.AreEqual("Idle", denied.LastStatus,
            "edge denied by AccessAsync is treated as not connected — receiver skipped");
    }

    [TestMethod]
    public async Task Broadcast_FansOutToMultipleTargets()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1 };
        var a = new NodeViewModel { Title = "A", DelayMilliseconds = 1 };
        var b = new NodeViewModel { Title = "B", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(a); helper.CreateNode(b);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.MultipleTargets);
        a.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        b.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, start.OutputSlot!, a.InputSlot!);
        Connect(tree, start.OutputSlot!, b.InputSlot!);

        Assert.AreEqual(2, start.OutputSlot!.Targets.Count,
            "MultipleTargets holds both outgoing connections");

        start.ReceiveCommand.Execute(new TaskContext(data: "seed"));

        Assert.IsTrue(await WaitUntilAsync(() => a.LastStatus == "Completed" && b.LastStatus == "Completed"),
            "both downstream targets received the broadcast");
        Assert.AreEqual("Completed", start.LastStatus, "start itself ran");
    }

    [TestMethod]
    public async Task AutoBroadcastOff_CascadeStopsUntilManualForward()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var start = new NodeViewModel { Title = "Start", DelayMilliseconds = 1, AutoBroadcast = false };
        var mid = new NodeViewModel { Title = "Mid", DelayMilliseconds = 1 };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(start); helper.CreateNode(mid); helper.CreateNode(sink);

        start.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        mid.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        mid.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, start.OutputSlot!, mid.InputSlot!);
        Connect(tree, mid.OutputSlot!, sink.InputSlot!);

        start.ReceiveCommand.Execute(new TaskContext(data: "seed"));

        Assert.IsTrue(await WaitUntilAsync(() => start.LastStatus == "Completed"),
            "start ran on manual trigger");
        await Task.Delay(150);
        Assert.AreEqual("Idle", mid.LastStatus,
            "AutoBroadcast=false stops the cascade — downstream nodes stay idle");

        // Manual forward: after explicitly broadcasting via BroadcastCommand, the cascade resumes to Sink.
        start.BroadcastCommand.Execute(new TaskContext(data: "manual"));

        Assert.IsTrue(await WaitUntilAsync(() => sink.LastStatus == "Completed"),
            "manual forward resumes the cascade to the sink");
        Assert.AreEqual("Completed", mid.LastStatus, "mid ran via manual forward");
    }

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(50);
        }
        return predicate();
    }
}

/// <summary>Test node: the runtime AccessAsync gate rejects a specified downstream receiver (a failed check is treated as not connected).</summary>
[WorkflowBuilder.Node<DenyGateHelper>(workSemaphore: 1)]
public partial class DenyGateNodeViewModel : NodeViewModel { }

public class DenyGateHelper : HttpHelper<NodeViewModel>
{
    /// <summary>The rejected downstream node: the edge pointing to it returns false from AccessAsync.</summary>
    public IWorkflowNodeViewModel? DeniedReceiver { get; set; }

    public override Task<bool> AccessAsync(IAccessContext context, CancellationToken ct)
    {
        if (context.Receiver?.Parent is not null &&
            ReferenceEquals(context.Receiver.Parent, DeniedReceiver))
            return Task.FromResult(false);
        return base.AccessAsync(context, ct);
    }
}
