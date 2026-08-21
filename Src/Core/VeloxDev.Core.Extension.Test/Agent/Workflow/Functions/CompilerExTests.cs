using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies the new CompilerEx decomposition algorithm: linear segments → ExecuteEntry, selectors →
/// BranchEntry (nested sub-graphs), branch downstream orders carry an offset (not reset to zero); in
/// both modes every node is visited by the compiler — under Static, skipped branch nodes receive a
/// reset signal (Order = -1, absolute stop), under Dynamic all branches get live orders.
/// </summary>
[TestClass]
public class CompilerExTests
{
    [TestMethod]
    public async Task CompileBaseGraph_ProducesBranchTree_WithOffsetOrders()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var compiler = new CompilerViewModel();

        var graphs = await compiler.CompileAsync(session.Controller);
        Assert.AreEqual(1, graphs.Count, "single start node → single compiled graph");
        var graph = graphs[0];

        // ── Structure: linear segment → Bool branch → join linear segment → Enum branch → tail ──
        Assert.IsInstanceOfType<ExecuteEntry>(graph.Entries[0], "first entry is a linear chain");
        Assert.IsInstanceOfType<BranchEntry>(graph.Entries[1], "second entry is a branch (bool selector)");
        Assert.IsInstanceOfType<ExecuteEntry>(graph.Entries[2], "third entry is the post-branch join chain");
        Assert.IsInstanceOfType<BranchEntry>(graph.Entries[3], "fourth entry is a branch (enum selector)");
        Assert.IsInstanceOfType<ExecuteEntry>(graph.Entries[4], "fifth entry is the tail chain");

        // Demo defaults to Dynamic: both branches get live orders (no pruning).
        var boolBranch = (BranchEntry)graph.Entries[1];
        Assert.AreEqual(2, boolBranch.Options.Count, "bool selector has True/False options");
        Assert.IsTrue(boolBranch.IsDynamic, "demo default dynamic → both branches alive");
        Assert.IsNotNull(boolBranch.Router, "branch carries its router node");

        // ── Compile identity: orders assigned contiguously with an offset ──
        Assert.AreEqual(0, ControllerOf(session).CompileContext?.Order, "controller is order 0");
        Assert.AreEqual(1, LoadSeedOf(session).CompileContext?.Order, "load seed is order 1");
        Assert.AreEqual(2, BoolSelectorOf(session).CompileContext?.Order, "bool selector is order 2");

        var trueOpt = boolBranch.Options.First(o => Equals(o.Key, true));
        var falseOpt = boolBranch.Options.First(o => Equals(o.Key, false));

        // True-branch downstream continues from the offset (not reset): Hot Path is 3rd, Cold Path 4th (both alive).
        var hot = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Hot Path");
        var cold = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Cold Path");
        Assert.AreEqual(3, hot.CompileContext?.Order, "Hot Path continues numbering from the offset (not 0)");
        Assert.AreEqual(4, cold.CompileContext?.Order, "dynamic mode gives Cold Path a live order");
        Assert.IsFalse(cold.IsCompileStopped, "dynamic Cold Path is not stopped");
        Assert.IsFalse(hot.IsCompileStopped, "chosen branch node is not stopped");

        // The join point Aggregate's order continues after the branches (the offset has passed both branches' slots)
        var aggregate = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Aggregate");
        Assert.AreEqual(5, aggregate.CompileContext?.Order, "join node continues after branch slots");

        // ── Branch sub-graph nesting: True → [Hot Path], False → [Cold Path] ──
        var trueSub = trueOpt.Graph!;
        Assert.IsInstanceOfType<ExecuteEntry>(trueSub.Entries[0]);
        var hotNode = (NodeViewModel)((ExecuteEntry)trueSub.Entries[0]).Nodes[0];
        Assert.AreEqual("Hot Path", hotNode.Title,
            "True branch sub-graph is the Hot Path chain");
    }

    [TestMethod]
    public async Task StaticMode_SendsResetSignalToSkippedBranch()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var boolSelector = session.Tree.Nodes.OfType<BoolSelectorNodeViewModel>().Single();
        boolSelector.CompileMode = RouterCompileMode.Static;   // Condition=true → False is skipped

        var compiler = new CompilerViewModel();
        await compiler.CompileAsync(session.Controller);

        var cold = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Cold Path");
        var hot = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Hot Path");
        Assert.AreEqual(-1, cold.CompileContext?.Order,
            "static-skipped branch node gets the reset signal (Order = -1)");
        Assert.IsTrue(cold.IsCompileStopped, "static-skipped node is stopped");
        Assert.AreEqual("⊘", cold.ExecutionOrderText, "static-skipped node badge shows ⊘");
        Assert.IsTrue(hot.CompileContext?.Order >= 0, "live branch node keeps a real order");
        Assert.IsFalse(hot.IsCompileStopped, "live branch node is not stopped");
    }

    [TestMethod]
    public async Task CompileThenRun_EngineDrivesGraph_WithRuntimeContext()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var compiler = new CompilerViewModel();
        await compiler.CompileAsync(session.Controller);

        var graph = compiler.Graphs[0];
        var context = new RuntimeContext();
        var engine = new CompilerEngine();
        await engine.RunAsync(graph, context, CancellationToken.None);

        Assert.AreEqual("Completed", context.Status, "engine completes the run");
        Assert.IsTrue(context.Sequence > 0, "nodes were driven and got sequence numbers");
        Assert.IsTrue(context.Logs.Count > 0, "engine captured an execution log per driven node");
        // The selected branch (True → Hot Path) is executed; the pruned False branch is not.
        Assert.IsTrue(context.Logs.Any(l => l.Contains("NodeViewModel")), "worker nodes were driven");

        // Numbers are compile-time fixed: after the run the badge keeps the compile order (Hot = #4), not rewritten to #1 at runtime.
        var hot2 = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Hot Path");
        Assert.AreEqual(4, hot2.LastExecutionOrder, "badge stays at the compile-fixed number after run");
        // The execution state code jumps to the compile-time fixed number: the run ends on the tail node (Finalize)'s number.
        var finalize = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Finalize");
        Assert.AreEqual(finalize.CompileContext?.Order, context.CurrentOrder,
            "execution state code ends on the last node's compile-fixed order");
    }

    [TestMethod]
    public async Task MethodRouter_CompileMode_ChangesRouteTable()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var selector = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();

        // Dynamic: the route table contains all branches
        selector.CompileMode = RouterCompileMode.Dynamic;
        var dynTable = await selector.GetRouteTable();
        Assert.IsTrue(dynTable.Count > 1, "dynamic mode returns all branches");

        // Static: only the currently selected branch
        selector.CompileMode = RouterCompileMode.Static;
        var staticTable = await selector.GetRouteTable();
        Assert.AreEqual(1, staticTable.Count, "static mode returns only the selected branch");
        var selectedKey = selector.OutputSlots!.NormalizeSelectorValue(selector.SelectedValue);
        Assert.IsTrue(staticTable.ContainsKey(selectedKey!),
            "static table keeps only the currently selected branch");
    }

    private static ControllerViewModel ControllerOf(WorkflowDemoSession session)
        => session.Controller;

    private static NodeViewModel LoadSeedOf(WorkflowDemoSession session)
        => session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Load Seed");

    private static BoolSelectorNodeViewModel BoolSelectorOf(WorkflowDemoSession session)
        => session.Tree.Nodes.OfType<BoolSelectorNodeViewModel>().Single();
}
