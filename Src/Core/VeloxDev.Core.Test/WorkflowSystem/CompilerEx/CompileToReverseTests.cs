using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// 反向编译(CompileAsync + CompileRole.Terminal):给定目标节点,只编译它的祖先锥(Sources 反向、沿有效边)并自动从锥的
/// 入口前沿执行,算出目标的结果 —— 不需要指定启动节点。锥内路由器被当作普通数据流节点(锥已编码
/// 唯一通往目标的支路),因此结果 = 一次恰好走那条支路的正向运行。
/// </summary>
[TestClass]
public class CompileToReverseTests
{
    private static async Task<CompiledGraph> CompileTerminalAsync(ProbeNode target)
        => (await new CompilerViewModel().CompileAsync(target, CompileRole.Terminal)).Single();

    private static async Task<CompiledGraph> CompileRootAsync(ProbeNode start)
        => (await new CompilerViewModel().CompileAsync(start, CompileRole.Root)).Single();

    [TestMethod]
    public async Task LinearChain_TargetMidCone_CompilesFromOwnEntry_MatchesForwardRun()
    {
        var s = new ProbeNode("s") { Handler = (_, _) => "S" };
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(s, a);
        ProbeGraph.Wire(a, b);

        var reverse = await CompileTerminalAsync(b);
        var reverseSession = await ProbeGraph.RunAsync(reverse);

        // 锥 {s,a,b} 的入口 = s → 编译产物与从 s 正向跑完全一致。
        var forward = await CompileRootAsync(s);
        var forwardSession = await ProbeGraph.RunAsync(forward);

        Assert.AreEqual("Completed", reverseSession.Status);
        Assert.AreEqual("B", reverseSession.Data, "reverse run must end with the target's own output");
        Assert.AreEqual("B", forwardSession.Data, "must equal the forward run's result for the same node");
        CollectionAssert.AreEqual(
            forward.Entries.SelectMany(e => e is ChainSegment c ? c.Nodes : []).Cast<ProbeNode>().ToArray(),
            reverse.Entries.SelectMany(e => e is ChainSegment c ? c.Nodes : []).Cast<ProbeNode>().ToArray());
    }

    [TestMethod]
    public async Task RouterOnConePath_FlattenedAndLocksToTargetBranch_IgnoringRuntimeSelection()
    {
        // 动态 Router,但 UI 选的是 B;目标在 A 分支深处 → 反向编译必须沿 A,不看 Selection。
        var router = new RouterNode("router") { CompileMode = RouterCompileMode.Dynamic, Selection = "B" };
        var a1 = new ProbeNode("a1") { Handler = (_, _) => "a1" };
        var target = new ProbeNode("target") { Handler = (_, _) => "A-result" };
        var b1 = new ProbeNode("b1") { Handler = (_, _) => "b1" };
        router.RouteTable["A"] = [a1];
        router.RouteTable["B"] = [b1];
        ProbeGraph.Wire(router, a1);
        ProbeGraph.Wire(router, b1);
        ProbeGraph.Wire(a1, target);

        var graph = await CompileTerminalAsync(target);
        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual("A-result", session.Data, "target output along branch A");
        Assert.IsEmpty(b1.Calls, "branch B is not an ancestor of the target and must not run");
        Assert.IsTrue(graph.Entries.All(e => e is ChainSegment), "routers on the cone must compile flat (no BranchSegment)");
        Assert.HasCount(1, router.Calls, "router itself is still driven once");
        Assert.HasCount(1, a1.Calls);
        Assert.HasCount(1, target.Calls);
    }

    [TestMethod]
    public async Task FanOutJoin_TargetAfterJoin_CompilesConeFunnel_GroupDataAtJoin()
    {
        object? joinPayload = "unset";
        var p = new ProbeNode("p") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a") { Handler = (_, _) => "AV" };
        var b = new ProbeNode("b") { Handler = (_, _) => "BV" };
        var join = new ProbeNode("join")
        {
            Handler = (ctx, _) => { joinPayload = ctx.Data; return "J"; },
        };
        var target = new ProbeNode("target") { Handler = (_, _) => "T" };
        ProbeGraph.Wire(p, a);
        ProbeGraph.Wire(p, b);
        ProbeGraph.Wire(a, join);
        ProbeGraph.Wire(b, join);
        ProbeGraph.Wire(join, target);

        var graph = await CompileTerminalAsync(target);
        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual("T", session.Data);
        Assert.IsInstanceOfType<IGroupData>(joinPayload);
        var group = Assert.IsInstanceOfType<IGroupData>(joinPayload);
        Assert.IsTrue(group.TryGetValue(a, out var va) && Equals(va, "AV"), "join aggregates branch a");
        Assert.IsTrue(group.TryGetValue(b, out var vb) && Equals(vb, "BV"), "join aggregates branch b");
        // 两条平行分支读到的都是同一个源负载(扇出语义在锥内保留)。
        Assert.AreEqual("SRC", a.Calls[0].Data);
        Assert.AreEqual("SRC", b.Calls[0].Data);
    }

    [TestMethod]
    public async Task TwoIndependentSources_FunnelingIntoTarget_CompilesFanOutBranchesPlusTargetJoin()
    {
        object? targetPayload = "unset";
        var e1 = new ProbeNode("e1") { Handler = (_, _) => "E1" };
        var e2 = new ProbeNode("e2") { Handler = (_, _) => "E2" };
        var target = new ProbeNode("target")
        {
            Handler = (ctx, _) => { targetPayload = ctx.Data; return "N"; },
        };
        ProbeGraph.Wire(e1, target);
        ProbeGraph.Wire(e2, target);

        var graph = await CompileTerminalAsync(target);
        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual("N", session.Data);
        Assert.IsInstanceOfType<IGroupData>(targetPayload);
        var group = Assert.IsInstanceOfType<IGroupData>(targetPayload);
        Assert.IsTrue(group.TryGetValue(e1, out var v1) && Equals(v1, "E1"), "target sees source e1 output");
        Assert.IsTrue(group.TryGetValue(e2, out var v2) && Equals(v2, "E2"), "target sees source e2 output");

        // 顶层是一条入口扇出(ParallelSegment)再汇合到 target。
        var parallel = graph.Entries.OfType<ParallelSegment>().SingleOrDefault();
        Assert.IsNotNull(parallel, "independent producers should compile as fan-out branches");
        Assert.HasCount(2, parallel!.Branches);
    }

    [TestMethod]
    public void NoStartNodeProvided_EntryFrontierIsDerivedFromTheCone()
    {
        // 目标没有任何输入 → 锥 = {target},入口 = target 自己,一样能跑。
        var leaf = new ProbeNode("leaf") { Handler = (_, _) => "leaf" };

        var graph = CompileTerminalAsync(leaf).GetAwaiter().GetResult();
        var session = ProbeGraph.RunAsync(graph).GetAwaiter().GetResult();

        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual("leaf", session.Data);
        Assert.HasCount(1, leaf.Calls);
    }

    [TestMethod]
    public void MultiLevelFanInAcrossIndependentEntries_ThrowsInformative()
    {
        // E1,E2 先汇到 J,J 再与 E3 汇到 target —— 两层漏斗,当前模型无法用单层入口扇出表达。
        var e1 = new ProbeNode("e1");
        var e2 = new ProbeNode("e2");
        var e3 = new ProbeNode("e3");
        var j = new ProbeNode("j");
        var target = new ProbeNode("target");
        ProbeGraph.Wire(e1, j);
        ProbeGraph.Wire(e2, j);
        ProbeGraph.Wire(j, target);
        ProbeGraph.Wire(e3, target);

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => CompileTerminalAsync(target).GetAwaiter().GetResult());
        StringAssert.Contains(ex.Message, "funnel", "the error must explain the unsupported cone shape");
    }
}
