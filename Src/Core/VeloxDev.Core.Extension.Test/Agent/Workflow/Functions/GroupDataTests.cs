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
            if (rt.Data is IGroupData group)
            {
                Component.LastGroupData = group;
                return Task.FromResult<object?>(group);
            }
            return Task.FromResult<object?>(Component.Value);
        }
        return Task.FromResult<object?>(null);
    }
}
