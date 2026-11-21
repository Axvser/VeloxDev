using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// Compile identity belongs to the compiled graph, not to the node. <see cref="PlainNode"/> implements neither
/// <see cref="ICompileTimeAware"/> nor <see cref="IRuntimeAware"/>; these tests pin that it still takes part in join
/// aggregation, redirect re-runs, status codes and error records, and that the notification copy a node may hold is
/// never read back by the engine.
/// </summary>
[TestClass]
public class CompileIdentityOwnershipTests
{
    private static bool FirstPass(ITaskContext ctx) => ctx is IRuntimeContext { Attempt: 1 };

    [TestMethod]
    public async Task PlainNode_RunsWithoutNotifications_AndGetsTheSessionAsTheReceiveParameter()
    {
        var a = new PlainNode("a") { Handler = (_, _) => "A" };
        var graph = ProbeGraph.Compile(a);

        Assert.IsTrue(graph.TryGetCompileInfo(a, out var info), "identity is recorded on the graph");
        Assert.AreEqual(0, info!.Order);

        var session = await ProbeGraph.RunAsync(graph, seed: "seed");

        Assert.HasCount(1, a.Calls);
        Assert.IsTrue(a.Calls[0].Compiled, "ReceiveAsync still receives the runtime session as its argument");
        Assert.AreEqual("seed", a.Calls[0].Data);
        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual(0, session.CurrentOrder);
    }

    [TestMethod]
    public async Task PlainJoin_ReceivesGroupData_FromTheGraphsBindingTable()
    {
        var p = new PlainNode("p") { Handler = (_, _) => "P" };
        var left = new PlainNode("left") { Handler = (_, _) => "L" };
        var right = new PlainNode("right") { Handler = (_, _) => "R" };
        var join = new PlainNode("join");
        ProbeGraph.Wire(p, left);
        ProbeGraph.Wire(p, right);
        ProbeGraph.Wire(left, join);
        ProbeGraph.Wire(right, join);

        var graph = ProbeGraph.Compile(p);

        Assert.IsTrue(graph.TryGetCompileInfo(join, out var joinInfo), "the join node has an identity without implementing anything");
        Assert.IsNotNull(joinInfo!.InputNodes, "the join's upstream sources live in the graph's binding");
        Assert.HasCount(2, joinInfo.InputNodes!);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.HasCount(1, join.Calls);
        Assert.IsInstanceOfType<IGroupData>(join.Calls[0].Data,
            "a plain node at a join still receives the aggregated GroupData");
        var group = (IGroupData)join.Calls[0].Data!;
        Assert.HasCount(2, group);
        Assert.AreEqual("L", group[left]);
        Assert.AreEqual("R", group[right]);
        Assert.AreEqual("Completed", session.Status);
    }

    [TestMethod]
    public async Task RedirectTarget_ThatIsAPlainNode_IsReExecuted()
    {
        var s = new PlainNode("s") { Handler = (_, _) => "S" };
        var a = new RedirectableNode("a")
        {
            Handler = (ctx, _) =>
            {
                if (FirstPass(ctx)) throw new InvalidOperationException("first pass fails");
                return "A";
            },
            Resolve = _ => s,
        };
        var b = new PlainNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(s, a);
        ProbeGraph.Wire(a, b);

        var graph = ProbeGraph.Compile(s);
        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual(2, session.Attempt, "the redirect was accepted and the graph re-ran");
        Assert.HasCount(2, s.Calls, "the plain target is driven again: its Order came from the graph");
        Assert.HasCount(2, a.Calls);
        Assert.HasCount(1, b.Calls);
        Assert.IsFalse(session.EndedWithError);
    }

    [TestMethod]
    public async Task EngineReadsTheGraphBinding_NotTheNodesNotificationCopy()
    {
        var s = new ProbeNode("s") { Handler = (_, _) => "S" };
        var a = new RedirectableNode("a")
        {
            Handler = (ctx, _) =>
            {
                if (FirstPass(ctx)) throw new InvalidOperationException("first pass fails");
                return "A";
            },
            Resolve = _ => s,
        };
        ProbeGraph.Wire(s, a);

        var graph = ProbeGraph.Compile(s);

        // 编译后把节点上的通知副本改乱：引擎若回读节点，回退目标(99)会被当成「不进上游」而拒绝。
        var bogus = new CompileContext { Order = 99, ChainIndex = 99, Offset = 0 };
        s.AttachCompileTimeContext(bogus);
        a.AttachCompileTimeContext(bogus);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual(2, session.Attempt, "rewriting the node's notification copy must not change the run");
        Assert.HasCount(2, s.Calls);
        Assert.AreEqual(2, s.Calls[1].Attempt, "the target was re-driven on the second pass");
    }

    [TestMethod]
    public async Task NodeInitiatedReport_CarriesTheCompiledOrder_WithoutAnyNotificationInterface()
    {
        var a = new PlainNode("a") { Handler = (_, _) => "A" };
        var b = new PlainNode("b")
        {
            AsyncHandler = async (ctx, _) =>
            {
                await ((IRuntimeContext)ctx).WarnAsync("a note");
                return "B";
            },
        };
        ProbeGraph.Wire(a, b);

        var graph = ProbeGraph.Compile(a);
        var records = new List<ExecutionError>();
        var session = new RuntimeContext { ErrorSink = new DelegateExecutionErrorSink(records.Add) };

        await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);

        Assert.HasCount(1, records);
        Assert.AreEqual(1, records[0].Order, "the structured record carries the graph's identity for b");
        Assert.AreEqual(ExecutionReportLevel.Warning, records[0].Level);
    }

    [TestMethod]
    public void ACompileNotificationThatThrows_DoesNotFailCompilation()
    {
        var a = new ProbeNode("a")
        {
            CompileAttachGate = _ => throw new InvalidOperationException("attach boom"),
        };

        var graph = ProbeGraph.Compile(a);

        Assert.HasCount(1, graph.Entries);
        Assert.IsTrue(graph.TryGetCompileInfo(a, out var info), "the binding was written before the notification");
        Assert.AreEqual(0, info!.Order);
        Assert.IsNull(a.CompileContext, "a failed notification leaves no partial copy");
    }

    [TestMethod]
    public void StaticPrunedPlainNode_StillGetsAnAbsoluteStopBinding()
    {
        var router = new RouterNode("router") { CompileMode = RouterCompileMode.Static, Selection = "A" };
        var live = new PlainNode("live");
        var pruned = new PlainNode("pruned");
        router.RouteTable["A"] = [live];
        router.RouteTable["B"] = [pruned];
        ProbeGraph.Wire(router, live);
        ProbeGraph.Wire(router, pruned);

        var graph = ProbeGraph.Compile(router);

        Assert.IsTrue(graph.TryGetCompileInfo(pruned, out var info), "the reset signal is recorded on the graph too");
        Assert.AreEqual(-1, info!.Order, "-1 is the absolute stop state");
        Assert.IsTrue(graph.TryGetCompileInfo(live, out var liveInfo));
        Assert.AreEqual(1, liveInfo!.Order);
    }
}
