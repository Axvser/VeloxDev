using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies GroupData data join: a multi-input join point reads a read-only "source Node → output"
/// dictionary from context.Data. Frozen design: chain/single-input nodes have an empty InputNodes →
/// keep bare Data; a multi-input join (Count &gt; 1) injects the dictionary.
/// </summary>
[TestClass]
public class GroupDataTests
{
    /// <summary>Fan-out (ParallelSegment) → join: each branch's output enters the dictionary.</summary>
    [TestMethod]
    public async Task FanOutJoin_ReceivesGroupedInputs()
    {
        var (tree, controller, sel, a, b, join) = BuildFanOutJoinGraph();
        sel.Condition = true;

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        var group = Assert.IsInstanceOfType<IGroupData>(join.LastGroupData,
            "join runs after both fan-out branches and receives a GroupData dictionary");
        Assert.AreEqual(2, group.Count, "both fan-out branch outputs are collected");
        Assert.IsTrue(group.TryGetValue(a, out var va) && Equals(10, va), "branch A output is present with its value");
        Assert.IsTrue(group.TryGetValue(b, out var vb) && Equals(20, vb), "branch B output is present with its value");
    }

    /// <summary>Branch → join (dynamic mode drives only the selected branch): an un-executed branch's source is absent from the dictionary (TryGetValue false).</summary>
    [TestMethod]
    public async Task BranchToJoin_IncludesOnlyTakenBranch()
    {
        var (tree, controller, sel, taken, untaken, join) = BuildBranchJoinGraph();
        sel.Condition = true;   // drive only the True branch (False branch is neither driven nor registered)

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        var group = Assert.IsInstanceOfType<IGroupData>(join.LastGroupData,
            "join still receives a GroupData (both branches alive at compile time)");
        Assert.AreEqual(1, group.Count, "only the taken branch's output is registered");
        Assert.IsTrue(group.TryGetValue(taken, out var vt) && Equals(10, vt),
            "taken branch output is present with its value");
        Assert.IsFalse(group.TryGetValue(untaken, out _),
            "untaken branch output must be absent — its node never ran this run");
    }

    /// <summary>Pure linear chain: no join dictionary injected — bare Data chain semantics are preserved (existing nodes need zero changes).</summary>
    [TestMethod]
    public async Task SingleInputChain_KeepsBareData()
    {
        var (tree, controller, chain, sink) = BuildLinearChainGraph();

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        Assert.IsNull(sink.LastGroupData, "single-input chain must not inject a GroupData");
        Assert.AreEqual(5, sink.LastData, "bare Data flows through the chain (5 from the upstream node)");
    }

    /// <summary>Branch switch (redirect back to the router and re-run): on pass 2 the old un-taken branch source is dropped — stale output no longer enters the join dictionary.</summary>
    [TestMethod]
    public async Task RedirectBranchSwitchJoin_ExcludesStaleSource()
    {
        var (tree, controller, sel, flipGate, y, join) = BuildBranchSwitchGraph();

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        // pass 1 takes True (FlipGate switches the route to False and redirects back to the router); pass 2 takes False (only Y is re-driven and registered).
        Assert.AreEqual(2, context.Attempt, "branch flip happens on attempt 1; pass 2 re-runs toward the router");

        var group = Assert.IsInstanceOfType<IGroupData>(join.LastGroupData,
            "join is re-driven after the redirect and receives the GroupData from pass 2");
        Assert.AreEqual(1, group.Count, "stale FlipGate output from pass 1 must be excluded from pass 2's join");
        Assert.IsTrue(group.TryGetValue(y, out var vy) && Equals(20, vy),
            "the newly-taken False branch (Y) output is present with its value");
        Assert.IsFalse(group.TryGetValue(flipGate, out _),
            "the un-re-run True branch (FlipGate) output must be absent — stale, not contract-preserved");
    }

    /// <summary>Redirect past the join point: prefix sources before the target are preserved per the resume contract (so an aggressive "clear everything each pass" scheme cannot drop legitimate old outputs).</summary>
    [TestMethod]
    public async Task RedirectPastJoin_PreservesSkippedPrefix()
    {
        var (tree, controller, sel, a, b, join, gate) = BuildRedirectPastJoinGraph();

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        // gate(5) warns on the 1st time and redirects back to join(4); pass 2 skips the whole Order<4 prefix (including the not-re-driven True branch A).
        Assert.AreEqual(2, context.Attempt, "gate warns on attempt 1 and redirects back to the join");

        var group = Assert.IsInstanceOfType<IGroupData>(join.LastGroupData,
            "join is re-driven as the redirect target and receives a GroupData");
        Assert.AreEqual(1, group.Count, "only the taken True branch (A) is preserved as contract prefix");
        Assert.IsTrue(group.TryGetValue(a, out var va) && Equals(10, va),
            "A's output survives as the contract-preserved prefix before the redirect target");
        Assert.IsFalse(group.TryGetValue(b, out _),
            "B never ran this run — must stay absent");
    }

    // ── Graph construction (mirrors ParallelFanOutTests.BuildFanOutGraph) ──

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
        Connect(tree, sel.TrueSlot!, a.InputSlot!);   // fan-out: True → [A, B]
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

/// <summary>Test node: returns <see cref="Value"/> in compile mode; when it receives a join dictionary (IGroupData), records it to <see cref="LastGroupData"/>.</summary>
[WorkflowBuilder.Node<TraceNodeHelper>(workSemaphore: 1)]
public partial class TraceNodeViewModel : NodeViewModel
{
    public int Value { get; set; }            // plain CLR property, does not rely on the generator
    public object? LastData { get; set; }     // context.Data from the most recent drive (bare value before the join)
    public object? LastGroupData { get; set; } // recorded when the most recent drive received an IGroupData (null when not a join)
}

/// <summary>TraceNode helper: returns Value for non-join input; for join input, records the dictionary and passes it through.</summary>
public class TraceNodeHelper : HttpHelper<TraceNodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is not null && ctx is IRuntimeContext rt)
        {
            Component.LastData = rt.Data;
            // Identify the join point by compile identity (matching the engine's injection condition):
            // a node with InputNodes.Count > 1 is a join point, not "Data happens to be a dictionary" —
            // on a re-run pass, Data may be a leftover join dictionary from the previous pass.
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

/// <summary>Test node: on the first drive it switches the router to another branch and requests a redirect back to the router (verifies that stale outputs are dropped on a branch switch).</summary>
[WorkflowBuilder.Node<BranchFlipHelper>(workSemaphore: 1)]
public partial class BranchFlipNodeViewModel : NodeViewModel, IRedirectable
{
    /// <summary>The router to flip (the helper sets its Condition to false on the first drive).</summary>
    public BoolSelectorNodeViewModel? TargetRouter { get; set; }

    /// <summary>Redirects back to the previous node (the router itself), triggering a reRouteOnly re-run.</summary>
    public Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct)
        => CompileContext is { } cc ? Task.FromResult<int?>(cc.Order - 1) : Task.FromResult<int?>(null);
}

/// <summary>BranchFlip helper: switches branches and requests a redirect on the first drive, then lets it pass.</summary>
public class BranchFlipHelper : HttpHelper<BranchFlipNodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is { } c && ctx is IRuntimeContext rt && rt.Attempt <= 1)
        {
            if (c.TargetRouter is not null) c.TargetRouter.Condition = false;   // pass 2 takes the False branch
            rt.Warn("Simulated failure: switched branches and redirected back to the router");
        }
        return base.ReceiveAsync(ctx, ct);
    }
}
