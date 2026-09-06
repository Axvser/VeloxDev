using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// 三种执行入口的语义分界:
///  ① 单节点任务(ReceiveCommand + ITaskContext)；
///  ② 边级广播(StandardBroadcastAsync 逐边投递,AccessAsync 门,失败按未连接跳过)；
///  ③ 链级编译运行(引擎只经 Helper.ReceiveAsync 驱动,传 IRuntimeContext,从不触发节点命令 —— 引擎持有下游派发权)。
/// </summary>
[TestClass]
public class EntrySemanticsTests
{
    [TestMethod]
    public async Task CompiledRun_DrivesOnlyThroughHelper_NeverExecutesNodeCommands()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(a, b);
        var graph = ProbeGraph.Compile(a);

        await ProbeGraph.RunAsync(graph);

        Assert.HasCount(1, a.Calls);
        Assert.HasCount(1, b.Calls);
        Assert.IsTrue(a.Calls[0].Compiled && b.Calls[0].Compiled,
            "chain-level driving hands each node an IRuntimeContext (IsCompilePhase == false)");

        // 引擎自己派发下游:它不得触发节点的 ReceiveCommand / BroadcastCommand。
        Assert.IsEmpty(a.ReceivedDeliveries, "engine must not run ReceiveCommand on nodes");
        Assert.IsEmpty(b.ReceivedDeliveries);
        Assert.AreEqual(0, ((TestCommand)a.BroadcastCommand).ExecuteCount, "engine must not auto-broadcast");
        Assert.AreEqual(0, ((TestCommand)b.BroadcastCommand).ExecuteCount);
        Assert.AreEqual(0, ((TestCommand)a.ReverseBroadcastCommand).ExecuteCount);
    }

    [TestMethod]
    public async Task StandardBroadcast_DeliversTaskContextPerValidEdge_SkipsAccessRejectedEdge()
    {
        var p = new ProbeNode("p");
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        ProbeGraph.Wire(p, a);
        ProbeGraph.Wire(p, b);

        await p.StandardBroadcastAsync("payload", CancellationToken.None);

        Assert.HasCount(1, a.ReceivedDeliveries, "valid edge p→a must deliver");
        Assert.HasCount(1, b.ReceivedDeliveries, "valid edge p→b must deliver");

        var toA = a.ReceivedDeliveries[0];
        Assert.AreEqual("payload", toA.Data);
        Assert.IsTrue(ReferenceEquals(toA.Sender, p.Output), "delivery carries the concrete sender slot");
        Assert.IsTrue(ReferenceEquals(toA.Receiver, a.Input), "delivery carries the concrete receiver slot");

        // 运行时 AccessAsync 失败 → 该边按"未连接"跳过。
        var gated = new ProbeNode("gated");
        var keep = new ProbeNode("keep");
        var skip = new ProbeNode("skip");
        gated.AccessGate = acc => acc.Receiver is null || !ReferenceEquals(acc.Receiver.Parent, skip);
        ProbeGraph.Wire(gated, keep);
        ProbeGraph.Wire(gated, skip);

        await gated.StandardBroadcastAsync("x", CancellationToken.None);

        Assert.HasCount(1, keep.ReceivedDeliveries, "edge passing AccessAsync must deliver");
        Assert.IsEmpty(skip.ReceivedDeliveries, "edge rejected by AccessAsync must be treated as unconnected");
    }

    [TestMethod]
    public async Task OneRunSession_IsInjectedAsTheSameInstance_ToEveryNode()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        var c = new ProbeNode("c") { Handler = (_, _) => "C" };
        ProbeGraph.Wire(a, b);
        ProbeGraph.Wire(b, c);
        var graph = ProbeGraph.Compile(a);

        var session = await ProbeGraph.RunAsync(graph);

        foreach (var n in new[] { a, b, c })
        {
            Assert.HasCount(1, n.AttachedContexts, $"{n.Name} must receive the runtime session once");
            Assert.IsTrue(ReferenceEquals(n.AttachedContexts[0], session),
                $"{n.Name} must see the SAME session object the whole run shares");
        }
    }
}
