using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// 补充无状态（Push/广播）执行模式的覆盖：
/// 1) ReverseBroadcastAsync 反向单跳传播（沿 Sources 触发上游 ReceiveCommand，不自动级联）；
/// 2) 广播运行期 AccessAsync 边门禁（校验失败视为「未连接」，跳过该接收端）；
/// 3) 扇出广播（一个 MultipleTargets 输出口 → 多个下游目标全部收到）；
/// 4) AutoBroadcast=false 时级联停止，需手动 BroadcastCommand 才继续推进。
/// 编译（Pull/引擎驱动）模式已由 CompilerExTests / ParallelFanOutTests / RedirectTests 覆盖。
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

        // 反向广播：Sink 沿其 InputSlot.Sources（即 Mid.OutputSlot）反向触发 Mid 的 ReceiveCommand。
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

        // 运行期门禁：Gate 拒绝流向 Denied 的那条边（AccessAsync 返回 false → 视为未连接）。
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

        // 手动 Forward：触发 BroadcastCommand 显式广播后，级联恢复直至 Sink。
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

/// <summary>测试节点：运行期 AccessAsync 门禁按 Receiver 拒绝指定下游（校验失败视为未连接）。</summary>
[WorkflowBuilder.Node<DenyGateHelper>(workSemaphore: 1)]
public partial class DenyGateNodeViewModel : NodeViewModel { }

public class DenyGateHelper : HttpHelper<NodeViewModel>
{
    /// <summary>被拒绝的下游节点：指向它的那条边 AccessAsync 返回 false。</summary>
    public IWorkflowNodeViewModel? DeniedReceiver { get; set; }

    public override Task<bool> AccessAsync(IAccessContext context, CancellationToken ct)
    {
        if (context.Receiver?.Parent is not null &&
            ReferenceEquals(context.Receiver.Parent, DeniedReceiver))
            return Task.FromResult(false);
        return base.AccessAsync(context, ct);
    }
}
