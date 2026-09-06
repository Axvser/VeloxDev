using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// Runtime engine contract: drives nodes along compiled segments; the engine owns downstream dispatch (never
/// triggers ReceiveCommand/BroadcastCommand). ReceiveAsync return values are written back into the session Data
/// for downstream reads; fan-out is "structurally parallel, executed in order + source payload restore";
/// multi-input joins aggregate each upstream's output into an IGroupData; terminal/error decide the final state.
/// </summary>
[TestClass]
public class RuntimeEngineRunTests
{
    [TestMethod]
    public async Task LinearChain_DrivesEachNodeOnceInOrder_DataFlowsThroughSession()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        var c = new ProbeNode("c") { Handler = (_, _) => "C" };
        ProbeGraph.Wire(a, b);
        ProbeGraph.Wire(b, c);
        var graph = ProbeGraph.Compile(a);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual(1, session.Attempt, "no redirects happened");
        Assert.HasCount(1, a.Calls);
        Assert.HasCount(1, b.Calls);
        Assert.HasCount(1, c.Calls);

        // 数据链:每个节点收到的是上一个节点的返回值。
        Assert.IsNull(a.Calls[0].Data, "first node receives the seed (null)");
        Assert.AreEqual("A", b.Calls[0].Data, "B must receive A's output");
        Assert.AreEqual("B", c.Calls[0].Data, "C must receive B's output");
        Assert.IsTrue(a.Calls[0].Compiled && b.Calls[0].Compiled && c.Calls[0].Compiled,
            "compiled driving passes an IRuntimeContext (IsCompilePhase == false)");
        Assert.AreEqual("C", session.Data, "session Data ends as the last node's output");

        // IRuntimeAware 注入的是同一个运行会话。
        Assert.IsTrue(a.AttachedContexts.Count == 1 && ReferenceEquals(a.AttachedContexts[0], session));
    }

    [TestMethod]
    public async Task DynamicRouter_AtRuntimeSelectsBranchByResolvedKey_OtherBranchNotDriven()
    {
        var router = new RouterNode("router") { CompileMode = RouterCompileMode.Dynamic };
        var a = new ProbeNode("a") { Handler = (_, _) => "A-run" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B-run" };
        router.RouteTable["A"] = [a];
        router.RouteTable["B"] = [b];
        ProbeGraph.Wire(router, a);
        ProbeGraph.Wire(router, b);

        // 编译期不可判(动态,payload=null);运行期把选中键改为 B。
        router.Selection = "B";
        var graph = ProbeGraph.Compile(router);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual("B", session.BranchKey, "runtime route key should be recorded on the session");
        Assert.IsEmpty(a.Calls, "unselected branch A must not be driven");
        Assert.HasCount(1, b.Calls, "selected branch B must run");
    }

    [TestMethod]
    public async Task FanOutParallel_RestoresSourcePayload_BeforeEachBranch()
    {
        var p = new ProbeNode("p") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(p, a);
        ProbeGraph.Wire(p, b);
        var graph = ProbeGraph.Compile(p);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status);
        Assert.HasCount(1, p.Calls);
        Assert.HasCount(1, a.Calls);
        Assert.HasCount(1, b.Calls);

        // 两条分支都必须读到同一个源负载,而不是前一条分支的输出。
        Assert.AreEqual("SRC", a.Calls[0].Data, "first branch receives the fan-out source payload");
        Assert.AreEqual("SRC", b.Calls[0].Data, "second branch must NOT see the first branch's output");
    }

    [TestMethod]
    public async Task JoinWithTwoUpstreams_ReceivesGroupDataKeyedBySourceNode()
    {
        object? joinPayload = "unset";
        var p = new ProbeNode("p") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a") { Handler = (_, _) => "AV" };
        var b = new ProbeNode("b") { Handler = (_, _) => "BV" };
        var join = new ProbeNode("join")
        {
            Handler = (ctx, _) =>
            {
                joinPayload = ctx.Data;
                return ctx.Data;
            },
        };
        ProbeGraph.Wire(p, a);
        ProbeGraph.Wire(p, b);
        ProbeGraph.Wire(a, join);
        ProbeGraph.Wire(b, join);
        var graph = ProbeGraph.Compile(p);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status);
        Assert.IsInstanceOfType(joinPayload, typeof(IGroupData), "join node should receive an IGroupData");
        var group = Assert.IsInstanceOfType<IGroupData>(joinPayload);
        Assert.AreEqual(2, group.Count);
        Assert.IsTrue(group.TryGetValue(a, out var va) && Equals(va, "AV"), "group must carry upstream A's output");
        Assert.IsTrue(group.TryGetValue(b, out var vb) && Equals(vb, "BV"), "group must carry upstream B's output");
    }

    [TestMethod]
    public async Task TerminalBranch_NoDownstream_EndsRunAsCompleted()
    {
        var router = new RouterNode("router") { CompileMode = RouterCompileMode.Dynamic, Selection = "stop" };
        router.RouteTable["stop"] = []; // 有路由键但没有下游 → terminal
        var graph = ProbeGraph.Compile(router);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status, "a terminal branch ends the flow normally");
        Assert.AreEqual(1, session.Attempt);
    }

    [TestMethod]
    public async Task NodeError_WithoutIRedirectable_EndsWithStatusMinusOne()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b")
        {
            Handler = (_, _) => throw new InvalidOperationException("boom"),
        };
        ProbeGraph.Wire(a, b);
        var graph = ProbeGraph.Compile(a);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.IsTrue(session.EndedWithError, "a non-redirectable error must mark the run as ended-with-error");
        Assert.AreEqual("Stopped", session.Status);
        Assert.AreEqual(-1, session.CurrentOrder, "status code should drop to -1 (absolute stop)");
        Assert.HasCount(1, a.Calls);
        Assert.HasCount(1, b.Calls, "the failing node itself is driven once");
        Assert.IsTrue(session.Logs.Any(l => l.Contains("[Error]", StringComparison.Ordinal) && l.Contains("boom")),
            "the error must be surfaced on the session log");
    }
}
