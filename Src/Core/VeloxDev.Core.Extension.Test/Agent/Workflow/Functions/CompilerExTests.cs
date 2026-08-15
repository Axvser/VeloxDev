using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// 验证新 CompilerEx 分解算法：线性段 → ExecuteEntry，选择器 → BranchEntry（嵌套子图），
/// 分支下游序号带偏移（不归零）；两种模式下所有节点都被编译器走到——Static 下被略过的分支节点
/// 收到重置信号（Order = -1，绝对停止），Dynamic 下全部分支分配 live orders。
/// </summary>
[TestClass]
public class CompilerExTests
{
    [TestMethod]
    public async Task CompileBaseGraph_ProducesBranchTree_WithOffsetOrders()
    {
        var session = WorkflowDemoSession.Create();
        var compiler = new CompilerViewModel();

        var graphs = await compiler.CompileAsync(session.Controller);
        Assert.AreEqual(1, graphs.Count, "single start node → single compiled graph");
        var graph = graphs[0];

        // ── 结构：线性段 → Bool 分支 → 汇合线性段 → Enum 分支 → 尾段 ──
        Assert.IsInstanceOfType<ExecuteEntry>(graph.Entries[0], "first entry is a linear chain");
        Assert.IsInstanceOfType<BranchEntry>(graph.Entries[1], "second entry is a branch (bool selector)");
        Assert.IsInstanceOfType<ExecuteEntry>(graph.Entries[2], "third entry is the post-branch join chain");
        Assert.IsInstanceOfType<BranchEntry>(graph.Entries[3], "fourth entry is a branch (enum selector)");
        Assert.IsInstanceOfType<ExecuteEntry>(graph.Entries[4], "fifth entry is the tail chain");

        // Demo 默认 Dynamic：两分支都分配 live orders（不剪枝）。
        var boolBranch = (BranchEntry)graph.Entries[1];
        Assert.AreEqual(2, boolBranch.Options.Count, "bool selector has True/False options");
        Assert.IsTrue(boolBranch.IsDynamic, "demo default dynamic → both branches alive");
        Assert.IsNotNull(boolBranch.Router, "branch carries its router node");

        // ── 编译身份：序号带偏移连续分配 ──
        Assert.AreEqual(0, ControllerOf(session).CompileContext?.Order, "controller is order 0");
        Assert.AreEqual(1, LoadSeedOf(session).CompileContext?.Order, "load seed is order 1");
        Assert.AreEqual(2, BoolSelectorOf(session).CompileContext?.Order, "bool selector is order 2");

        var trueOpt = boolBranch.Options.First(o => Equals(o.Key, true));
        var falseOpt = boolBranch.Options.First(o => Equals(o.Key, false));
        Assert.IsFalse(trueOpt.IsSkipped, "chosen True branch is not skipped");
        Assert.IsFalse(falseOpt.IsSkipped, "dynamic mode keeps the False branch alive too");

        // True 分支下游从 offset 继续（不归零）：Hot Path 是第 3 个，Cold Path 第 4 个（都活）。
        var hot = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Hot Path");
        var cold = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Cold Path");
        Assert.AreEqual(3, hot.CompileContext?.Order, "Hot Path continues numbering from the offset (not 0)");
        Assert.AreEqual(4, cold.CompileContext?.Order, "dynamic mode gives Cold Path a live order");
        Assert.IsFalse(cold.IsCompileStopped, "dynamic Cold Path is not stopped");
        Assert.IsFalse(hot.IsCompileStopped, "chosen branch node is not stopped");

        // 汇合点 Aggregate 的序号在分支之后继续（offset 已越过两个分支的槽位）
        var aggregate = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Aggregate");
        Assert.AreEqual(5, aggregate.CompileContext?.Order, "join node continues after branch slots");

        // ── 分支子图嵌套：True → [Hot Path]，False → [Cold Path] ──
        var trueSub = trueOpt.Graph!;
        Assert.IsInstanceOfType<ExecuteEntry>(trueSub.Entries[0]);
        var hotNode = (NodeViewModel)((ExecuteEntry)trueSub.Entries[0]).Nodes[0];
        Assert.AreEqual("Hot Path", hotNode.Title,
            "True branch sub-graph is the Hot Path chain");
    }

    [TestMethod]
    public async Task StaticMode_SendsResetSignalToSkippedBranch()
    {
        var session = WorkflowDemoSession.Create();
        var boolSelector = session.Tree.Nodes.OfType<BoolSelectorNodeViewModel>().Single();
        boolSelector.CompileMode = RouterCompileMode.Static;   // Condition=true → False 被略过

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
        var session = WorkflowDemoSession.Create();
        var compiler = new CompilerViewModel();
        await compiler.CompileAsync(session.Controller);

        var graph = compiler.Graphs[0];
        var context = new RuntimeContext();
        var engine = new CompilerEngine();
        await engine.RunAsync(graph, context, CancellationToken.None);

        Assert.AreEqual("Completed", context.Status, "engine completes the run");
        Assert.IsTrue(context.Sequence > 0, "nodes were driven and got sequence numbers");
        Assert.IsTrue(context.Logs.Count > 0, "engine captured an execution log per driven node");
        // 选择的分支（True → Hot Path）被执行；被剪枝的 False 分支不执行。
        Assert.IsTrue(context.Logs.Any(l => l.Contains("NodeViewModel")), "worker nodes were driven");

        // 编号编译期固定：运行后徽标仍保持编译顺序（Hot = #4），不会被运行时重写成 #1。
        var hot2 = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Hot Path");
        Assert.AreEqual(4, hot2.LastExecutionOrder, "badge stays at the compile-fixed number after run");
        // 执行状态码跳到编译期固定编号：运行结束停在尾节点（Finalize）的编号上。
        var finalize = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Finalize");
        Assert.AreEqual(finalize.CompileContext?.Order, context.CurrentOrder,
            "execution state code ends on the last node's compile-fixed order");
    }

    [TestMethod]
    public async Task MethodRouter_CompileMode_ChangesRouteTable()
    {
        var session = WorkflowDemoSession.Create();
        var selector = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();

        // Dynamic：路由表包含全部分支
        selector.CompileMode = RouterCompileMode.Dynamic;
        var dynTable = await selector.GetRouteTable();
        Assert.IsTrue(dynTable.Count > 1, "dynamic mode returns all branches");

        // Static：只包含当前选中分支
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
