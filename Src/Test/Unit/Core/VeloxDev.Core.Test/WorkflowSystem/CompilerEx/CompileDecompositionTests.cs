using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// Compile-phase contract: CompilerViewModel decomposes the reachable sub-graph into Chain/Branch/Parallel
/// segments and injects the compile identity. Verified on self-contained probe nodes + manual wiring: segment
/// shapes, global Order, static pruning (Order = -1), dynamic keep-all branches, plain-node fan-out →
/// ParallelSegment + join registration (JoinInputs), and pruning of AccessAsync-rejected edges.
/// </summary>
[TestClass]
public class CompileDecompositionTests
{
    private static int Order(ProbeNode n) => n.CompileContext?.Order ?? -1000;

    [TestMethod]
    public void LinearThreeNodes_CompilesIntoSingleChainSegment_WithConsecutiveOrders()
    {
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        var c = new ProbeNode("c");
        ProbeGraph.Wire(a, b);
        ProbeGraph.Wire(b, c);

        var graph = ProbeGraph.Compile(a);

        Assert.HasCount(1, graph.Entries, "linear topology should produce exactly one entry");
        var chain = ProbeGraph.AsChain(graph.Entries[0]);
        Assert.IsNotNull(chain, "the single entry should be a ChainSegment");
        CollectionAssert.AreEqual(
            new IWorkflowNodeViewModel[] { a, b, c },
            chain!.Nodes.ToArray(),
            "chain should contain the three nodes in topological order");

        Assert.AreEqual(0, Order(a), "first node Order should start at 0");
        Assert.AreEqual(1, Order(b));
        Assert.AreEqual(2, Order(c));
    }

    [TestMethod]
    public void DynamicRouter_TwoBranches_KeepsBothOptions_IsDynamic_CompileKeyNull()
    {
        var router = new RouterNode("router") { CompileMode = RouterCompileMode.Dynamic, Selection = "A" };
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        router.RouteTable["A"] = [a];
        router.RouteTable["B"] = [b];
        ProbeGraph.Wire(router, a);
        ProbeGraph.Wire(router, b);

        var graph = ProbeGraph.Compile(router);

        Assert.HasCount(1, graph.Entries);
        var branch = ProbeGraph.AsBranch(graph.Entries[0]);
        Assert.IsNotNull(branch, "router should compile into a BranchSegment");
        Assert.IsTrue(branch!.IsDynamic, "ResolveRouteKey(null) returning null means dynamic");
        Assert.IsNull(branch.CompileKey, "dynamic branch has no compile-time locked key");
        Assert.IsTrue(ReferenceEquals(branch.Router, router));
        Assert.HasCount(2, branch.Options, "both branches must stay alive in dynamic mode");

        var optionA = branch.Options.Single(o => Equals(o.Key, "A"));
        var optionB = branch.Options.Single(o => Equals(o.Key, "B"));
        Assert.IsNotNull(optionA.Graph, "branch A should carry a sub-graph");
        Assert.IsNotNull(optionB.Graph);
        CollectionAssert.AreEqual(
            new IWorkflowNodeViewModel[] { a },
            ProbeGraph.AsChain(optionA.Graph!.Entries[0])!.Nodes.ToArray());
        CollectionAssert.AreEqual(
            new IWorkflowNodeViewModel[] { b },
            ProbeGraph.AsChain(optionB.Graph!.Entries[0])!.Nodes.ToArray());

        // 全局 Order 单调连续:router=0, a=1, b=2。
        Assert.AreEqual(0, Order(router));
        Assert.AreEqual(1, Order(a));
        Assert.AreEqual(2, Order(b));
    }

    [TestMethod]
    public void StaticRouter_SelectionALocksCompileKey_UnselectedBranchDownstreamGetsMinusOne()
    {
        var router = new RouterNode("router") { CompileMode = RouterCompileMode.Static, Selection = "A" };
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        router.RouteTable["A"] = [a];
        router.RouteTable["B"] = [b];
        ProbeGraph.Wire(router, a);
        ProbeGraph.Wire(router, b);

        var graph = ProbeGraph.Compile(router);

        Assert.HasCount(1, graph.Entries);
        var branch = ProbeGraph.AsBranch(graph.Entries[0]);
        Assert.IsNotNull(branch);
        Assert.IsFalse(branch!.IsDynamic, "static mode locks the key at compile time");
        Assert.AreEqual("A", branch.CompileKey, "CompileKey = compile-time selected branch");
        Assert.HasCount(1, branch.Options, "static compile keeps only the selected branch as a live option");

        // 未选中分支的下游节点收到绝对停止信号(Order = -1),且不出现在任何编译段里。
        Assert.AreEqual(-1, Order(b), "unselected branch downstream must be Order = -1 (absolute stop)");
        Assert.AreEqual(1, Order(a), "selected branch downstream gets a normal order");

        var reached = AllNodes(graph).ToHashSet();
        Assert.IsFalse(reached.Contains(b), "stopped node must not appear in any compiled segment");
    }

    [TestMethod]
    public void PlainNodeFanOut_CompilesToParallelSegment_ThenJoinChain_RegistersJoinInputs()
    {
        var p = new ProbeNode("p");
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        var join = new ProbeNode("join");
        ProbeGraph.Wire(p, a);
        ProbeGraph.Wire(p, b);
        ProbeGraph.Wire(a, join);
        ProbeGraph.Wire(b, join);

        var graph = ProbeGraph.Compile(p);

        Assert.AreEqual(3, graph.Entries.Count, "expected [Chain(p), Parallel, Chain(join)]");

        var headChain = ProbeGraph.AsChain(graph.Entries[0]);
        Assert.IsNotNull(headChain);
        CollectionAssert.AreEqual(new IWorkflowNodeViewModel[] { p }, headChain!.Nodes.ToArray());

        var parallel = ProbeGraph.AsParallel(graph.Entries[1]);
        Assert.IsNotNull(parallel, "plain multi-target fan-out must compile into a ParallelSegment");
        Assert.HasCount(2, parallel!.Branches, "two downstream branches");
        CollectionAssert.AreEqual(new IWorkflowNodeViewModel[] { a },
            ProbeGraph.AsChain(parallel.Branches[0].Entries[0])!.Nodes.ToArray());
        CollectionAssert.AreEqual(new IWorkflowNodeViewModel[] { b },
            ProbeGraph.AsChain(parallel.Branches[1].Entries[0])!.Nodes.ToArray());

        var tailChain = ProbeGraph.AsChain(graph.Entries[2]);
        Assert.IsNotNull(tailChain, "the common downstream of both fan-out branches continues as a chain");
        CollectionAssert.AreEqual(new IWorkflowNodeViewModel[] { join }, tailChain!.Nodes.ToArray());

        // 汇合点编译身份登记了多分支出口(运行期据此聚合 GroupData)。
        Assert.IsNotNull(join.CompileContext, "join node must receive a compile identity");
        Assert.IsNotNull(join.CompileContext!.InputNodes, "join should be registered with its input sources");
        Assert.AreEqual(2, join.CompileContext.InputNodes!.Count);
        CollectionAssert.AreEqual(
            new ProbeNode[] { a, b },
            join.CompileContext.InputNodes.Cast<ProbeNode>().ToArray());
    }

    [TestMethod]
    public void InvalidOutputEdge_AccessFalse_IsPrunedFromGraph_DegradesToSinglePath()
    {
        var p = new ProbeNode("p");
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        ProbeGraph.Wire(p, a);
        ProbeGraph.Wire(p, b);

        // 只有发往 b 的边判定无效 → 编译器按"未连接"剪掉它。
        p.AccessGate = acc => acc.Receiver is null || !ReferenceEquals(acc.Receiver.Parent, b);

        var graph = ProbeGraph.Compile(p);

        Assert.HasCount(1, graph.Entries, "with one invalid edge the graph degrades to a single linear chain");
        var chain = ProbeGraph.AsChain(graph.Entries[0]);
        Assert.IsNotNull(chain);
        CollectionAssert.AreEqual(new IWorkflowNodeViewModel[] { p, a }, chain!.Nodes.ToArray());

        Assert.IsNull(b.CompileContext, "pruned node should not even be reached by the compiler");
    }

    private static IEnumerable<IWorkflowNodeViewModel> AllNodes(CompiledGraph graph)
    {
        foreach (var e in graph.Entries)
            foreach (var n in SegmentNodes(e))
                yield return n;
    }

    private static IEnumerable<IWorkflowNodeViewModel> SegmentNodes(CompileSegment entry) => entry switch
    {
        ChainSegment c => c.Nodes,
        BranchSegment b => b.Options.SelectMany(o => o.Graph is null ? [] : AllNodes(o.Graph)),
        ParallelSegment p => p.Branches.SelectMany(AllNodes),
        _ => [],
    };
}
