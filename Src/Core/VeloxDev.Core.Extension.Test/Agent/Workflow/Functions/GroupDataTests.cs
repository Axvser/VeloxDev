using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// 验证 GroupData 数据汇合：多输入汇合点从 context.Data 读到「来源 Node → 产物」只读字典。
/// 冻结设计：链式/单输入节点 InputNodes 为空 → 保持裸 Data；多输入汇合（Count &gt; 1）注入字典。
/// </summary>
[TestClass]
public class GroupDataTests
{
    /// <summary>扇出（ParallelEntry）→ 汇合：各分支的产物都进入字典。</summary>
    [TestMethod]
    public async Task FanOutJoin_ReceivesGroupedInputs()
    {
        var (tree, controller, sel, a, b, join) = BuildFanOutJoinGraph();
        sel.Condition = true;

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new CompilerEngine().RunAsync(graph, context, CancellationToken.None);

        var group = Assert.IsInstanceOfType<IGroupData>(join.LastGroupData,
            "join runs after both fan-out branches and receives a GroupData dictionary");
        Assert.AreEqual(2, group.Count, "both fan-out branch outputs are collected");
        Assert.IsTrue(group.TryGetValue(a, out var va) && Equals(10, va), "branch A output is present with its value");
        Assert.IsTrue(group.TryGetValue(b, out var vb) && Equals(20, vb), "branch B output is present with its value");
    }

    /// <summary>分支 → 汇合（动态模式只驱动选中分支）：未执行分支的来源不在字典中（TryGetValue false）。</summary>
    [TestMethod]
    public async Task BranchToJoin_IncludesOnlyTakenBranch()
    {
        var (tree, controller, sel, taken, untaken, join) = BuildBranchJoinGraph();
        sel.Condition = true;   // 只驱动 True 分支（False 分支不驱动、不登记）

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new CompilerEngine().RunAsync(graph, context, CancellationToken.None);

        var group = Assert.IsInstanceOfType<IGroupData>(join.LastGroupData,
            "join still receives a GroupData (both branches alive at compile time)");
        Assert.AreEqual(1, group.Count, "only the taken branch's output is registered");
        Assert.IsTrue(group.TryGetValue(taken, out var vt) && Equals(10, vt),
            "taken branch output is present with its value");
        Assert.IsFalse(group.TryGetValue(untaken, out _),
            "untaken branch output must be absent — its node never ran this run");
    }

    /// <summary>纯线性链：不注入汇合字典，保持裸 Data 链式语义（现有节点零改动）。</summary>
    [TestMethod]
    public async Task SingleInputChain_KeepsBareData()
    {
        var (tree, controller, chain, sink) = BuildLinearChainGraph();

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new CompilerEngine().RunAsync(graph, context, CancellationToken.None);

        Assert.IsNull(sink.LastGroupData, "single-input chain must not inject a GroupData");
        Assert.AreEqual(5, sink.LastData, "bare Data flows through the chain (5 from the upstream node)");
    }

    /// <summary>分支切换（重定向回路由器重跑）：pass 2 未走的旧分支来源被剔除——陈旧产物不再进入汇合字典。</summary>
    [TestMethod]
    public async Task RedirectBranchSwitchJoin_ExcludesStaleSource()
    {
        var (tree, controller, sel, flipGate, y, join) = BuildBranchSwitchGraph();

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new CompilerEngine().RunAsync(graph, context, CancellationToken.None);

        // pass 1 走 True（FlipGate 把路由切到 False 并回退到路由器），pass 2 走 False（只有 Y 被重驱动登记）。
        Assert.AreEqual(2, context.Attempt, "branch flip happens on attempt 1; pass 2 re-runs toward the router");

        var group = Assert.IsInstanceOfType<IGroupData>(join.LastGroupData,
            "join is re-driven after the redirect and receives the GroupData from pass 2");
        Assert.AreEqual(1, group.Count, "stale FlipGate output from pass 1 must be excluded from pass 2's join");
        Assert.IsTrue(group.TryGetValue(y, out var vy) && Equals(20, vy),
            "the newly-taken False branch (Y) output is present with its value");
        Assert.IsFalse(group.TryGetValue(flipGate, out _),
            "the un-re-run True branch (FlipGate) output must be absent — stale, not contract-preserved");
    }

    /// <summary>重定向越过汇合点：目标之前的 prefix 来源按 resume 契约保留（防止「每 pass 全清」的激进方案丢合法旧产物）。</summary>
    [TestMethod]
    public async Task RedirectPastJoin_PreservesSkippedPrefix()
    {
        var (tree, controller, sel, a, b, join, gate) = BuildRedirectPastJoinGraph();

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new CompilerEngine().RunAsync(graph, context, CancellationToken.None);

        // gate(5) 第 1 次 Warn 回退到 join(4)；pass 2 跳过 Order<4 的整段 prefix（含未重驱动的 True 分支 A）。
        Assert.AreEqual(2, context.Attempt, "gate warns on attempt 1 and redirects back to the join");

        var group = Assert.IsInstanceOfType<IGroupData>(join.LastGroupData,
            "join is re-driven as the redirect target and receives a GroupData");
        Assert.AreEqual(1, group.Count, "only the taken True branch (A) is preserved as contract prefix");
        Assert.IsTrue(group.TryGetValue(a, out var va) && Equals(10, va),
            "A's output survives as the contract-preserved prefix before the redirect target");
        Assert.IsFalse(group.TryGetValue(b, out _),
            "B never ran this run — must stay absent");
    }

    // ── 构图（仿 ParallelFanOutTests.BuildFanOutGraph）──

    private static (TreeDefaultViewModel Tree, ControllerViewModel Controller, BoolSelectorNodeViewModel Sel,
        TraceNodeViewModel A, TraceNodeViewModel B, TraceNodeViewModel Join) BuildFanOutJoinGraph()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var sel = new BoolSelectorNodeViewModel { Title = "FanOut", Condition = true };
        helper.CreateNode(sel);
        var a = new TraceNodeViewModel { Title = "A", Value = 10 };
        var b = new TraceNodeViewModel { Title = "B", Value = 20 };
        var join = new TraceNodeViewModel { Title = "Join" };
        helper.CreateNode(a); helper.CreateNode(b); helper.CreateNode(join);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sel.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        a.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        b.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        a.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        b.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        join.InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);

        Connect(tree, controller.OutputSlot!, sel.InputSlot!);
        Connect(tree, sel.TrueSlot!, a.InputSlot!);   // 扇出：True → [A, B]
        Connect(tree, sel.TrueSlot!, b.InputSlot!);
        Connect(tree, a.OutputSlot!, join.InputSlot!);
        Connect(tree, b.OutputSlot!, join.InputSlot!);

        return (tree, controller, sel, a, b, join);
    }

    private static (TreeDefaultViewModel Tree, ControllerViewModel Controller, BoolSelectorNodeViewModel Sel,
        TraceNodeViewModel Taken, TraceNodeViewModel Untaken, TraceNodeViewModel Join) BuildBranchJoinGraph()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var sel = new BoolSelectorNodeViewModel { Title = "Branch", Condition = true };
        helper.CreateNode(sel);
        var taken = new TraceNodeViewModel { Title = "Taken", Value = 10 };
        var untaken = new TraceNodeViewModel { Title = "Untaken", Value = 99 };
        var join = new TraceNodeViewModel { Title = "Join" };
        helper.CreateNode(taken); helper.CreateNode(untaken); helper.CreateNode(join);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sel.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        taken.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        untaken.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        taken.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        untaken.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        join.InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);

        Connect(tree, controller.OutputSlot!, sel.InputSlot!);
        Connect(tree, sel.TrueSlot!, taken.InputSlot!);
        Connect(tree, sel.FalseSlot!, untaken.InputSlot!);
        Connect(tree, taken.OutputSlot!, join.InputSlot!);
        Connect(tree, untaken.OutputSlot!, join.InputSlot!);

        return (tree, controller, sel, taken, untaken, join);
    }

    private static (TreeDefaultViewModel Tree, ControllerViewModel Controller,
        TraceNodeViewModel Chain, TraceNodeViewModel Sink) BuildLinearChainGraph()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var chain = new TraceNodeViewModel { Title = "Chain", Value = 5 };
        var sink = new TraceNodeViewModel { Title = "Sink" };
        helper.CreateNode(chain); helper.CreateNode(sink);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        chain.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        chain.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, controller.OutputSlot!, chain.InputSlot!);
        Connect(tree, chain.OutputSlot!, sink.InputSlot!);

        return (tree, controller, chain, sink);
    }

    private static (TreeDefaultViewModel Tree, ControllerViewModel Controller, BoolSelectorNodeViewModel Sel,
        BranchFlipNodeViewModel FlipGate, TraceNodeViewModel Y, TraceNodeViewModel Join) BuildBranchSwitchGraph()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var sel = new BoolSelectorNodeViewModel { Title = "Switch", Condition = true };
        helper.CreateNode(sel);
        var flipGate = new BranchFlipNodeViewModel { Title = "FlipGate", TargetRouter = sel };
        var y = new TraceNodeViewModel { Title = "Y", Value = 20 };
        var join = new TraceNodeViewModel { Title = "Join" };
        helper.CreateNode(flipGate); helper.CreateNode(y); helper.CreateNode(join);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sel.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        flipGate.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        y.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        flipGate.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        y.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        join.InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);

        Connect(tree, controller.OutputSlot!, sel.InputSlot!);
        Connect(tree, sel.TrueSlot!, flipGate.InputSlot!);
        Connect(tree, sel.FalseSlot!, y.InputSlot!);
        Connect(tree, flipGate.OutputSlot!, join.InputSlot!);
        Connect(tree, y.OutputSlot!, join.InputSlot!);

        return (tree, controller, sel, flipGate, y, join);
    }

    private static (TreeDefaultViewModel Tree, ControllerViewModel Controller, BoolSelectorNodeViewModel Sel,
        TraceNodeViewModel A, TraceNodeViewModel B, TraceNodeViewModel Join, RedirectGateNodeViewModel Gate)
        BuildRedirectPastJoinGraph()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var sel = new BoolSelectorNodeViewModel { Title = "Branch", Condition = true };
        helper.CreateNode(sel);
        var a = new TraceNodeViewModel { Title = "A", Value = 10 };
        var b = new TraceNodeViewModel { Title = "B", Value = 99 };
        var join = new TraceNodeViewModel { Title = "Join" };
        var gate = new RedirectGateNodeViewModel { Title = "Gate", FailCount = 1, RedirectBackSteps = 1 };
        helper.CreateNode(a); helper.CreateNode(b); helper.CreateNode(join); helper.CreateNode(gate);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sel.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        a.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        b.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        a.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        b.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        join.InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);
        join.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        gate.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, controller.OutputSlot!, sel.InputSlot!);
        Connect(tree, sel.TrueSlot!, a.InputSlot!);
        Connect(tree, sel.FalseSlot!, b.InputSlot!);
        Connect(tree, a.OutputSlot!, join.InputSlot!);
        Connect(tree, b.OutputSlot!, join.InputSlot!);
        Connect(tree, join.OutputSlot!, gate.InputSlot!);

        return (tree, controller, sel, a, b, join, gate);
    }

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}

/// <summary>测试节点：编译态返回 <see cref="Value"/>；收到汇合字典（IGroupData）时记录到 <see cref="LastGroupData"/>。</summary>
[WorkflowBuilder.Node<TraceNodeHelper>(workSemaphore: 1)]
public partial class TraceNodeViewModel : NodeViewModel
{
    public int Value { get; set; }            // 纯 CLR 属性，不依赖生成器
    public object? LastData { get; set; }     // 最近一次驱动时的 context.Data（汇合前为裸值）
    public object? LastGroupData { get; set; } // 最近一次驱动收到 IGroupData 时记录（非汇合为 null）
}

/// <summary>TraceNode 助手：非汇合输入返回 Value；汇合输入记录字典并透传。</summary>
public class TraceNodeHelper : HttpHelper<TraceNodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is not null && ctx is IRuntimeContext rt)
        {
            Component.LastData = rt.Data;
            // 按编译身份识别汇合点（与引擎注入条件一致）：InputNodes.Count > 1 的节点才是汇合点，
            // 而非「Data 恰好是字典」——重跑 pass 时 Data 可能是上一 pass 残留的汇合字典。
            if (Component.CompileContext?.InputNodes is { Count: > 1 } && rt.Data is IGroupData group)
            {
                Component.LastGroupData = group;
                return Task.FromResult<object?>(group);
            }
            return Task.FromResult<object?>(Component.Value);
        }
        return Task.FromResult<object?>(null);
    }
}

/// <summary>测试节点：第 1 次驱动时把路由器切到另一分支并请求重定向回路由器（验证分支切换下陈旧产物被剔除）。</summary>
[WorkflowBuilder.Node<BranchFlipHelper>(workSemaphore: 1)]
public partial class BranchFlipNodeViewModel : NodeViewModel, IRedirectable
{
    /// <summary>要翻转的路由器（helper 在第 1 次驱动时把它的 Condition 改为 false）。</summary>
    public BoolSelectorNodeViewModel? TargetRouter { get; set; }

    /// <summary>回退到前一节点（即路由器本身），触发「只重新路由」的 reRouteOnly 重跑。</summary>
    public Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct)
        => CompileContext is { } cc ? Task.FromResult<int?>(cc.Order - 1) : Task.FromResult<int?>(null);
}

/// <summary>BranchFlip 助手：第 1 次驱动时切分支并请求重定向，之后放行。</summary>
public class BranchFlipHelper : HttpHelper<BranchFlipNodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is { } c && ctx is IRuntimeContext rt && rt.Attempt <= 1)
        {
            if (c.TargetRouter is not null) c.TargetRouter.Condition = false;   // pass 2 走 False 分支
            rt.Warn("模拟故障:切分支并回退到路由器");
        }
        return base.ReceiveAsync(ctx, ct);
    }
}
