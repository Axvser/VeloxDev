using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;
using ExecCtx = VeloxDev.WorkflowSystem.Compilation.ExecutionContext;

namespace VeloxDev.Core.Test.WorkflowSystem;

// ── Stubs ───────────────────────────────────────────────────────────────────

file sealed class StubCommand : IVeloxCommand
{
    public int ExecuteCount;
    public int LockCount;
    public int UnLockCount;
    public List<object?> Parameters = [];
    public Action<object?>? AfterExecute; // 用于测试的钩子，可在此处设置 WorkResult
    public bool FailOnExecute; // 设为 true 使 Execute 触发 Failed 事件而非 Exited

    public event EventHandler? CanExecuteChanged;
    public event CommandEventHandler? Created;
    public event CommandEventHandler? Started;
    public event CommandEventHandler? Completed;
    public event CommandEventHandler? Canceled;
    public event CommandEventHandler? Failed;
    public event CommandEventHandler? Exited;
    public event CommandEventHandler? Enqueued;
    public event CommandEventHandler? Dequeued;

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)
    {
        ExecuteCount++;
        Parameters.Add(parameter);
        if (FailOnExecute)
        {
            Failed?.Invoke(new CommandEventArgs(null!, CommandEventType.Failed));
            Exited?.Invoke(new CommandEventArgs(null!, CommandEventType.Exited));
        }
        else
        {
            AfterExecute?.Invoke(parameter);
            Exited?.Invoke(new CommandEventArgs(null!, CommandEventType.Exited));
        }
    }
    public void Lock() { LockCount++; }
    public void UnLock() { UnLockCount++; }
    public void Notify() { }
    public void Clear() { }
    public void Interrupt() { }
    public void Continue() { }
    public void ChangeSemaphore(int semaphore) { }
    public Task ExecuteAsync(object? parameter) { Execute(parameter); return Task.CompletedTask; }
    public Task LockAsync() => Task.CompletedTask;
    public Task UnLockAsync() => Task.CompletedTask;
    public Task ClearAsync() => Task.CompletedTask;
    public Task InterruptAsync() => Task.CompletedTask;
    public Task ContinueAsync() => Task.CompletedTask;
    public Task ChangeSemaphoreAsync(int semaphore) => Task.CompletedTask;
}

file sealed class StubSlot : IWorkflowSlotViewModel
{
    public ObservableCollection<IWorkflowSlotViewModel> Targets { get; set; } = [];
    public ObservableCollection<IWorkflowSlotViewModel> Sources { get; set; } = [];
    public IWorkflowNodeViewModel? Parent { get; set; }
    public SlotChannel Channel { get; set; }
    public SlotState State { get; set; }
    public Anchor Anchor { get; set; } = new();
    public IVeloxCommand SetChannelCommand { get; } = new StubCommand();
    public IVeloxCommand SendConnectionCommand { get; } = new StubCommand();
    public IVeloxCommand ReceiveConnectionCommand { get; } = new StubCommand();
    public IVeloxCommand DeleteCommand { get; } = new StubCommand();
    public IVeloxCommand CloseCommand { get; } = new StubCommand();
    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string p) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(p));
    public void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public IWorkflowSlotViewModelHelper GetHelper() => throw new NotSupportedException();
    public void SetHelper(IWorkflowSlotViewModelHelper helper) { }
}

file class StubNode : IWorkflowNodeViewModel
{
    public StubCommand TrackedCommand { get; } = new();
    public IWorkflowTreeViewModel? Parent { get; set; }
    public Anchor Anchor { get; set; } = new();
    public Size Size { get; set; } = new();
    public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; } = [];

    public IVeloxCommand MoveCommand { get; } = new StubCommand();
    public IVeloxCommand SetAnchorCommand { get; } = new StubCommand();
    public IVeloxCommand SetSizeCommand { get; } = new StubCommand();
    public IVeloxCommand CreateSlotCommand { get; } = new StubCommand();
    public IVeloxCommand DeleteCommand { get; } = new StubCommand();
    public IVeloxCommand WorkCommand => TrackedCommand;
    public IVeloxCommand BroadcastCommand { get; } = new StubCommand();
    public IVeloxCommand ReverseBroadcastCommand { get; } = new StubCommand();
    public IVeloxCommand CloseCommand { get; } = new StubCommand();

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string p) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(p));
    public void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public IWorkflowNodeViewModelHelper GetHelper() => throw new NotSupportedException();
    public void SetHelper(IWorkflowNodeViewModelHelper helper) { }
}

file sealed class StubTree : IWorkflowTreeViewModel
{
    public CanvasLayout Layout { get; set; } = new();
    public IWorkflowLinkViewModel VirtualLink { get; set; } = null!;
    public ObservableCollection<IWorkflowNodeViewModel> Nodes { get; set; } = [];
    public ObservableCollection<IWorkflowLinkViewModel> Links { get; set; } = [];
    public Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>> LinksMap { get; set; } = [];
    public IVeloxCommand CreateNodeCommand { get; } = new StubCommand();
    public IVeloxCommand SetPointerCommand { get; } = new StubCommand();
    public IVeloxCommand ResetVirtualLinkCommand { get; } = new StubCommand();
    public IVeloxCommand SendConnectionCommand { get; } = new StubCommand();
    public IVeloxCommand ReceiveConnectionCommand { get; } = new StubCommand();
    public IVeloxCommand SubmitCommand { get; } = new StubCommand();
    public IVeloxCommand RedoCommand { get; } = new StubCommand();
    public IVeloxCommand UndoCommand { get; } = new StubCommand();
    public IVeloxCommand CloseCommand { get; } = new StubCommand();
    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string p) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(p));
    public void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public IWorkflowTreeViewModelHelper GetHelper() => throw new NotSupportedException();
    public void SetHelper(IWorkflowTreeViewModelHelper helper) { }
}

// ── Stub with ICompileTimeRouter ────────────────────────────────────────────

file sealed class RoutingNode : StubNode, ICompileTimeRouter
{
    public IReadOnlyDictionary<object, IWorkflowNodeViewModel> RouteTable { get; set; }
        = new Dictionary<object, IWorkflowNodeViewModel>();

    public object? CurrentRouteKey { get; set; }

    public IReadOnlyDictionary<object, IWorkflowNodeViewModel> GetRouteTable() => RouteTable;
    public object? GetCurrentRouteKey() => CurrentRouteKey;
}

// ── Stub with ICompileTimeSink ──────────────────────────────────────────────

file sealed class SinkNode : StubNode, ICompileTimeSink
{
    public List<ExecCtx> Events { get; } = [];

    public void OnExecutionEvent(ExecCtx context)
        => Events.Add(context);
}

// ── Stub with ICompileTimePriority ──────────────────────────────────────────

file sealed class PriorityNode : StubNode, ICompileTimePriority
{
    public int CompilePriority { get; set; }
}

// ── Graph builders ──────────────────────────────────────────────────────────

file static class GraphBuilder
{
    internal static void Connect(StubSlot from, StubSlot to)
    { from.Targets.Add(to); to.Sources.Add(from); }

    internal static (StubTree Tree, StubNode[] Nodes) BuildLinearChain()
    {
        var t = new StubTree();
        var n = new[] { new StubNode{Parent=t}, new StubNode{Parent=t}, new StubNode{Parent=t} };
        var s = new[] { new StubSlot{Parent=n[0]}, new StubSlot{Parent=n[1]}, new StubSlot{Parent=n[2]} };
        n[0].Slots.Add(s[0]); n[1].Slots.Add(s[1]); n[2].Slots.Add(s[2]);
        Connect(s[0], s[1]); Connect(s[1], s[2]);
        foreach (var x in n) t.Nodes.Add(x);
        return (t, n);
    }

    internal static (StubTree Tree, StubNode[] Nodes) BuildDiamond()
    {
        var t = new StubTree();
        var n = new[] { new StubNode{Parent=t}, new StubNode{Parent=t}, new StubNode{Parent=t}, new StubNode{Parent=t} };
        var s = new[] { new StubSlot{Parent=n[0]}, new StubSlot{Parent=n[1]}, new StubSlot{Parent=n[2]}, new StubSlot{Parent=n[3]} };
        foreach (var i in Enumerable.Range(0,4)) n[i].Slots.Add(s[i]);
        Connect(s[0], s[1]); Connect(s[0], s[2]); Connect(s[1], s[3]); Connect(s[2], s[3]);
        foreach (var x in n) t.Nodes.Add(x);
        return (t, n);
    }

    internal static (StubTree Tree, StubNode[] Nodes) BuildCycle()
    {
        var t = new StubTree();
        var n = new[] { new StubNode{Parent=t}, new StubNode{Parent=t}, new StubNode{Parent=t} };
        var s = new[] { new StubSlot{Parent=n[0]}, new StubSlot{Parent=n[1]}, new StubSlot{Parent=n[2]} };
        foreach (var i in Enumerable.Range(0,3)) n[i].Slots.Add(s[i]);
        Connect(s[0], s[1]); Connect(s[1], s[2]); Connect(s[2], s[0]);
        foreach (var x in n) t.Nodes.Add(x);
        return (t, n);
    }

    internal static (StubTree Tree, StubNode[] Nodes) BuildTwoEntries()
    {
        var t = new StubTree();
        var n = new[] { new StubNode{Parent=t}, new StubNode{Parent=t}, new StubNode{Parent=t}, new StubNode{Parent=t} };
        var s = new[] { new StubSlot{Parent=n[0]}, new StubSlot{Parent=n[1]}, new StubSlot{Parent=n[2]}, new StubSlot{Parent=n[3]} };
        foreach (var i in Enumerable.Range(0,4)) n[i].Slots.Add(s[i]);
        Connect(s[0], s[1]); Connect(s[2], s[3]);
        foreach (var x in n) t.Nodes.Add(x);
        return (t, n);
    }

    internal static (StubTree Tree, StubNode[] Nodes) BuildSingleNode()
    {
        var t = new StubTree();
        var n = new StubNode { Parent = t };
        t.Nodes.Add(n);
        return (t, new[] { n });
    }

    internal static (StubTree Tree, StubNode[] Nodes) BuildVShape()
    {
        var t = new StubTree();
        var n = new[] { new StubNode{Parent=t}, new StubNode{Parent=t}, new StubNode{Parent=t} };
        var s = new[] { new StubSlot{Parent=n[0]}, new StubSlot{Parent=n[1]}, new StubSlot{Parent=n[2]} };
        foreach (var i in Enumerable.Range(0,3)) n[i].Slots.Add(s[i]);
        Connect(s[0], s[1]); Connect(s[0], s[2]);
        foreach (var x in n) t.Nodes.Add(x);
        return (t, n);
    }
}

// ── Tests ───────────────────────────────────────────────────────────────────

[TestClass]
public class WorkflowCompilerTests
{
    private readonly WorkflowCompiler _compiler = new();

    // ═══════════════════════════════════════════════════════════════════════
    // 1. CompileMode — BFS / DFS
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod] public void BFS_LinearChain_ThreeItems() { var r = _compiler.Compile(GraphBuilder.BuildLinearChain().Nodes[0], CompileMode.BFS)[0]; Assert.AreEqual(3, r.Items.Count); }
    [TestMethod] public void BFS_Diamond_FourItems() { var r = _compiler.Compile(GraphBuilder.BuildDiamond().Nodes[0], CompileMode.BFS)[0]; Assert.AreEqual(4, r.Items.Count); }
    [TestMethod] public void DFS_LinearChain_ThreeItems() { var r = _compiler.Compile(GraphBuilder.BuildLinearChain().Nodes[0], CompileMode.DFS)[0]; Assert.AreEqual(3, r.Items.Count); }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. CompileDirection — Forward / Reverse
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Forward_FromMiddle_ReachesDownstreamOnly()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[1], CompileMode.BFS, CompileDirection.Forward)[0];
        Assert.AreEqual(2, r.Items.Count);
        Assert.AreSame(n[1], r.Items[0].Node);
        Assert.AreSame(n[2], r.Items[1].Node);
    }

    [TestMethod]
    public void Reverse_FromMiddle_ReachesUpstreamOnly()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[1], CompileMode.BFS, CompileDirection.Reverse)[0];
        Assert.AreEqual(2, r.Items.Count);
        Assert.AreSame(n[1], r.Items[0].Node);
        Assert.AreSame(n[0], r.Items[1].Node);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. CompileScope — FromNode / Omni
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void DefaultScope_IsFromNode()
    {
        var r = _compiler.Compile(GraphBuilder.BuildLinearChain().Nodes[0], CompileMode.BFS)[0];
        Assert.AreEqual(CompileScope.FromNode, r.Scope);
    }

    [TestMethod]
    public void Omni_TwoEntries_ReturnsAllFour()
    {
        var (t, n) = GraphBuilder.BuildTwoEntries();
        var results = _compiler.Compile(n[0], CompileMode.BFS, CompileDirection.Forward, CompileScope.Omni);
        Assert.AreEqual(2, results.Count, "Two independent subgraphs → 2 results");
        Assert.AreEqual(4, results.Sum(r => r.Items.Count), "Total 4 items across both subgraphs");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. CycleHandling — Throw / Trim / Allow
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Cycle_Throw_Throws()
    {
        try { _compiler.Compile(GraphBuilder.BuildCycle().Nodes[0], CompileMode.BFS, cycleHandling: CycleHandling.Throw); Assert.Fail(); }
        catch (InvalidOperationException) { }
    }

    [TestMethod]
    public void Cycle_Trim_AllItemsReturned()
    {
        var r = _compiler.Compile(GraphBuilder.BuildCycle().Nodes[0], CompileMode.BFS, cycleHandling: CycleHandling.Trim)[0];
        Assert.AreEqual(3, r.Items.Count);
        Assert.IsTrue(r.HasCycle);
    }

    [TestMethod]
    public void Cycle_Allow_LoopEntryMarked()
    {
        var r = _compiler.Compile(GraphBuilder.BuildCycle().Nodes[0], CompileMode.BFS, cycleHandling: CycleHandling.Allow)[0];
        Assert.IsTrue(r.HasCycle);
        var entry = r.Items.FirstOrDefault(i => i.IsLoopEntry);
        Assert.IsNotNull(entry);
        Assert.IsNotNull(entry.LoopTailId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Cross-domain
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void CrossDomain_Throws()
    {
        var t1 = new StubTree(); var t2 = new StubTree();
        var n0 = new StubNode { Parent = t1 }; var s0 = new StubSlot { Parent = n0 }; n0.Slots.Add(s0);
        var n1 = new StubNode { Parent = t2 }; var s1 = new StubSlot { Parent = n1 }; n1.Slots.Add(s1);
        GraphBuilder.Connect(s0, s1); t1.Nodes.Add(n0); t1.Nodes.Add(n1);
        try { _compiler.Compile(n0, CompileMode.BFS); Assert.Fail(); }
        catch (InvalidOperationException) { }
    }

    [TestMethod]
    public void StartNodeNoParent_Throws()
    {
        var n0 = new StubNode { Parent = null };
        try { _compiler.Compile(n0, CompileMode.BFS); Assert.Fail(); }
        catch (InvalidOperationException) { }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Execution
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_AllNodesExecuted()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        await r.ExecuteAsync();
        foreach (var node in n.Cast<StubNode>())
            Assert.AreEqual(1, node.TrackedCommand.ExecuteCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Result chaining
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ResultChaining_PassesCurrentParamThrough()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        var result = await r.ExecuteAsync("start");
        Assert.AreEqual("start", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. Error redirect
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ErrorRedirect_ExecutesTargetAndSkipsInMainLoop()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        // n[0] 会失败，重定向到 n[2]
        n[0].TrackedCommand.FailOnExecute = true;

        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        r.Items[0].ErrorRedirectId = 2;

        var result = await r.ExecuteAsync("start");

        Assert.AreEqual(1, n[0].TrackedCommand.ExecuteCount, "failed node executes once");
        Assert.AreEqual(1, n[1].TrackedCommand.ExecuteCount, "middle node executes normally");
        Assert.AreEqual(1, n[2].TrackedCommand.ExecuteCount, "redirect target executes once (not twice)");
        Assert.AreEqual("start", result, "result passes through as initial param");
    }

    [TestMethod]
    public async Task ErrorRedirect_NoRedirectId_ContinuesNormally()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        n[0].TrackedCommand.FailOnExecute = true;
        // 不设置 ErrorRedirectId → 不应执行任何重定向

        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        await r.ExecuteAsync();

        Assert.AreEqual(1, n[0].TrackedCommand.ExecuteCount, "failed node executes");
        Assert.AreEqual(1, n[1].TrackedCommand.ExecuteCount, "continues to next");
        Assert.AreEqual(1, n[2].TrackedCommand.ExecuteCount, "continues to last");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9. ICompileTimeRouter
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void CompileTimeRouter_RouteTableCollected()
    {
        var t = new StubTree();
        var targetA = new StubNode { Parent = t };
        var targetB = new StubNode { Parent = t };
        var router = new RoutingNode { Parent = t };
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["a"] = targetA,
            ["b"] = targetB,
        };
        t.Nodes.Add(router);
        t.Nodes.Add(targetA);
        t.Nodes.Add(targetB);

        var r = _compiler.Compile(router, CompileMode.BFS)[0];
        Assert.IsNotNull(r.Items[0].RouteTable);
        Assert.AreSame(targetA, r.Items[0].RouteTable["a"]);
        Assert.AreSame(targetB, r.Items[0].RouteTable["b"]);
    }

    [TestMethod]
    public void CompileTimeRouter_WorksAcrossAllConfigs()
    {
        var t = new StubTree();
        var target = new StubNode { Parent = t };
        var router = new RoutingNode { Parent = t };
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["x"] = target,
        };
        // 用 Slot 把 router 和 target 连起来，确保两者都在遍历范围内
        var s0 = new StubSlot { Parent = router }; router.Slots.Add(s0);
        var s1 = new StubSlot { Parent = target }; target.Slots.Add(s1);
        GraphBuilder.Connect(s0, s1);
        t.Nodes.Add(router);
        t.Nodes.Add(target);

        // 编译参数组合
        var modes = new[] { CompileMode.BFS, CompileMode.DFS };

        foreach (var mode in modes)
        {
            var r1 = _compiler.Compile(router, mode, CompileDirection.Forward, CompileScope.FromNode)[0];
            var item1 = r1.Items.FirstOrDefault(i => i.Node == router);
            Assert.IsNotNull(item1, $"Router not found for {mode}/FromNode");
            Assert.IsNotNull(item1.RouteTable);
            Assert.AreSame(target, item1.RouteTable["x"]);

            var results2 = _compiler.Compile(router, mode, CompileDirection.Forward, CompileScope.Omni);
            var item2 = results2.SelectMany(r => r.Items).FirstOrDefault(i => i.Node == router);
            Assert.IsNotNull(item2, $"Router not found for {mode}/Omni");
            Assert.IsNotNull(item2.RouteTable);
            Assert.AreSame(target, item2.RouteTable["x"]);
        }

        // Reverse + FromNode
        var revR = _compiler.Compile(target, CompileMode.BFS, CompileDirection.Reverse)[0];
        var revItem = revR.Items.FirstOrDefault(i => i.Node == router);
        Assert.IsNotNull(revItem, "Router not found in Reverse mode");
        Assert.IsNotNull(revItem.RouteTable, "RouteTable is null in Reverse mode");
    }

    [TestMethod]
    public void NonRouterNode_RouteTableIsNull()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        Assert.IsNull(r.Items[0].RouteTable);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 10. ICompileTimeSink
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task CompileTimeSink_ReceivesLifecycleEvents()
    {
        var t = new StubTree();
        var sink = new SinkNode { Parent = t };
        t.Nodes.Add(sink);

        var r = _compiler.Compile(sink, CompileMode.BFS)[0];
        await r.ExecuteAsync("test");

        Assert.IsTrue(sink.Events.Count >= 2); // BeforeExecute + AfterExecute at minimum
        Assert.AreEqual(ExecutionEvent.BeforeExecute, sink.Events[0].Event);
        Assert.AreEqual(ExecutionEvent.AfterExecute, sink.Events[1].Event);
        // Last event should be OnCompleted
        Assert.AreEqual(ExecutionEvent.OnCompleted, sink.Events.Last().Event);
    }

    [TestMethod]
    public async Task CompileTimeSink_OnCompletedFired()
    {
        var t = new StubTree();
        var sink = new SinkNode { Parent = t };
        var slot = new StubSlot { Parent = sink };
        sink.Slots.Add(slot);
        t.Nodes.Add(sink);

        var r = _compiler.Compile(sink, CompileMode.BFS)[0];
        await r.ExecuteAsync();

        Assert.IsTrue(sink.Events.Any(e => e.Event == ExecutionEvent.OnCompleted),
            "Sink should receive OnCompleted event");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11. ICompileTimePriority
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void BFS_Priority_LowerComesFirst()
    {
        var t = new StubTree();
        var n0 = new StubNode { Parent = t };
        var n1 = new PriorityNode { Parent = t, CompilePriority = 0 };
        var n2 = new PriorityNode { Parent = t, CompilePriority = 2 };
        var s0 = new StubSlot { Parent = n0 }; n0.Slots.Add(s0);
        var s1 = new StubSlot { Parent = n1 }; n1.Slots.Add(s1);
        var s2 = new StubSlot { Parent = n2 }; n2.Slots.Add(s2);
        GraphBuilder.Connect(s0, s1); GraphBuilder.Connect(s0, s2);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2);

        var r = _compiler.Compile(n0, CompileMode.BFS)[0];
        Assert.AreSame(n1, r.Items[1].Node);
        Assert.AreSame(n2, r.Items[2].Node);
    }

    [TestMethod]
    public void BFS_Priority_HigherComesLast()
    {
        var t = new StubTree();
        var n0 = new StubNode { Parent = t };
        var n1 = new PriorityNode { Parent = t, CompilePriority = 2 };
        var n2 = new PriorityNode { Parent = t, CompilePriority = 0 };
        var s0 = new StubSlot { Parent = n0 }; n0.Slots.Add(s0);
        var s1 = new StubSlot { Parent = n1 }; n1.Slots.Add(s1);
        var s2 = new StubSlot { Parent = n2 }; n2.Slots.Add(s2);
        GraphBuilder.Connect(s0, s1); GraphBuilder.Connect(s0, s2);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2);

        var r = _compiler.Compile(n0, CompileMode.BFS)[0];
        Assert.AreSame(n2, r.Items[1].Node);
        Assert.AreSame(n1, r.Items[2].Node);
    }

    [TestMethod]
    public void BFS_DefaultPriority_IsZero()
    {
        var t = new StubTree();
        // n1 不实现 ICompileTimePriority → 默认 0
        // n2 实现且 CompilePriority = 1
        var n0 = new StubNode { Parent = t };
        var n1 = new StubNode { Parent = t };
        var n2 = new PriorityNode { Parent = t, CompilePriority = 1 };
        var s0 = new StubSlot { Parent = n0 }; n0.Slots.Add(s0);
        var s1 = new StubSlot { Parent = n1 }; n1.Slots.Add(s1);
        var s2 = new StubSlot { Parent = n2 }; n2.Slots.Add(s2);
        GraphBuilder.Connect(s0, s1); GraphBuilder.Connect(s0, s2);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2);

        var r = _compiler.Compile(n0, CompileMode.BFS)[0];
        Assert.AreSame(n1, r.Items[1].Node);
        Assert.AreSame(n2, r.Items[2].Node);
    }

    [TestMethod]
    public void DFS_Priority_AffectsBranchOrder()
    {
        // 钻石图：n0 → n1(p=2), n2(p=0) → n3
        // 低优先级的 n2 分支应被优先探索
        var t = new StubTree();
        var n0 = new StubNode { Parent = t };
        var n1 = new PriorityNode { Parent = t, CompilePriority = 2 };
        var n2 = new PriorityNode { Parent = t, CompilePriority = 0 };
        var n3 = new StubNode { Parent = t };
        var s = Enumerable.Range(0, 4).Select(i => new StubSlot { Parent = i switch { 0 => n0, 1 => n1, 2 => n2, _ => n3 } }).ToArray();
        n0.Slots.Add(s[0]); n1.Slots.Add(s[1]); n2.Slots.Add(s[2]); n3.Slots.Add(s[3]);
        GraphBuilder.Connect(s[0], s[1]); GraphBuilder.Connect(s[0], s[2]);
        GraphBuilder.Connect(s[1], s[3]); GraphBuilder.Connect(s[2], s[3]);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2); t.Nodes.Add(n3);

        var r = _compiler.Compile(n0, CompileMode.DFS)[0];
        // DFS post-order: n2 分支优先 → 结果中 n2 应出现在 n1 之前
        var idx1 = -1; var idx2 = -1;
        for (int i = 0; i < r.Items.Count; i++)
        {
            if (r.Items[i].Node == n1) idx1 = i;
            if (r.Items[i].Node == n2) idx2 = i;
        }
        Assert.IsTrue(idx2 < idx1, "n2 (priority=0) should appear before n1 (priority=2) in DFS result");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 12. Branch exclusive (routing)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task BranchExclusive_ChosenBranchExecutes_OtherSkipped()
    {
        // 钻石图：n0(router) → n1, n2 → n3
        // n1 独占 true 分支，n2 独占 false 分支，n3 非独占
        var t = new StubTree();
        var n0 = new RoutingNode { Parent = t, CurrentRouteKey = true };
        var n1 = new StubNode { Parent = t };
        var n2 = new StubNode { Parent = t };
        var n3 = new StubNode { Parent = t };
        n0.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            [true] = n1,
            [false] = n2,
        };
        var s = new[] { new StubSlot{Parent=n0}, new StubSlot{Parent=n1}, new StubSlot{Parent=n2}, new StubSlot{Parent=n3} };
        n0.Slots.Add(s[0]); n1.Slots.Add(s[1]); n2.Slots.Add(s[2]); n3.Slots.Add(s[3]);
        GraphBuilder.Connect(s[0], s[1]); GraphBuilder.Connect(s[0], s[2]);
        GraphBuilder.Connect(s[1], s[3]); GraphBuilder.Connect(s[2], s[3]);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2); t.Nodes.Add(n3);

        var r = _compiler.Compile(n0, CompileMode.BFS)[0];
        Assert.IsNotNull(r.Items[0].BranchExclusiveItems);

        await r.ExecuteAsync();

        Assert.AreEqual(1, ((StubNode)n0).TrackedCommand.ExecuteCount);
        Assert.AreEqual(1, n1.TrackedCommand.ExecuteCount, "n1 on chosen (true) branch");
        Assert.AreEqual(0, n2.TrackedCommand.ExecuteCount, "n2 on unchosen (false) branch — skipped");
        Assert.AreEqual(1, n3.TrackedCommand.ExecuteCount, "n3 reachable from both — executed");
    }

    [TestMethod]
    public async Task BranchExclusive_OtherBranchChosen()
    {
        var t = new StubTree();
        var n0 = new RoutingNode { Parent = t, CurrentRouteKey = false };
        var n1 = new StubNode { Parent = t };
        var n2 = new StubNode { Parent = t };
        var n3 = new StubNode { Parent = t };
        n0.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            [true] = n1,
            [false] = n2,
        };
        var s = new[] { new StubSlot{Parent=n0}, new StubSlot{Parent=n1}, new StubSlot{Parent=n2}, new StubSlot{Parent=n3} };
        n0.Slots.Add(s[0]); n1.Slots.Add(s[1]); n2.Slots.Add(s[2]); n3.Slots.Add(s[3]);
        GraphBuilder.Connect(s[0], s[1]); GraphBuilder.Connect(s[0], s[2]);
        GraphBuilder.Connect(s[1], s[3]); GraphBuilder.Connect(s[2], s[3]);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2); t.Nodes.Add(n3);

        var r = _compiler.Compile(n0, CompileMode.BFS)[0];
        await r.ExecuteAsync();

        Assert.AreEqual(0, n1.TrackedCommand.ExecuteCount, "n1 on true — skipped (chose false)");
        Assert.AreEqual(1, n2.TrackedCommand.ExecuteCount, "n2 on false — executed");
        Assert.AreEqual(1, n3.TrackedCommand.ExecuteCount);
    }

    [TestMethod]
    public void BranchExclusive_NonRouter_HasNull()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        Assert.IsNull(r.Items[0].BranchExclusiveItems);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 13. Edge cases
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void SingleNode_ReturnsOneItem()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        Assert.AreEqual(1, r.Items.Count);
    }

    [TestMethod]
    public void SingleNode_NoCycle()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        Assert.IsFalse(r.HasCycle);
    }

    [TestMethod]
    public void LinearChain_NoCycle()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        Assert.IsFalse(r.HasCycle);
    }

    [TestMethod]
    public void Cycle_HasCycleTrue()
    {
        var r = _compiler.Compile(GraphBuilder.BuildCycle().Nodes[0], CompileMode.BFS, cycleHandling: CycleHandling.Trim)[0];
        Assert.IsTrue(r.HasCycle);
    }

    [TestMethod]
    public void CompiledItem_WrapsNode()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        Assert.AreSame(n[0], r.Items[0].Node);
    }

    [TestMethod]
    public void CompiledItem_DefaultProperties()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        Assert.IsNull(r.Items[0].ErrorRedirectId);
        Assert.AreEqual(0, r.Items[0].MaxRetries);
        Assert.AreEqual(0, r.Items[0].Depth);
        Assert.IsFalse(r.Items[0].IsLoopEntry);
    }

    [TestMethod]
    public void CompiledItem_Order_Sequential()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        for (int i = 0; i < r.Items.Count; i++)
            Assert.AreEqual(i, r.Items[i].Order);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 13. Result metadata
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Result_StoresMode()
    {
        var r = _compiler.Compile(GraphBuilder.BuildSingleNode().Nodes[0], CompileMode.DFS)[0];
        Assert.AreEqual(CompileMode.DFS, r.Mode);
    }

    [TestMethod]
    public void Result_DirectionAndScope_Defaults()
    {
        var r = _compiler.Compile(GraphBuilder.BuildSingleNode().Nodes[0], CompileMode.BFS)[0];
        Assert.AreEqual(CompileDirection.Forward, r.Direction);
        Assert.AreEqual(CompileScope.FromNode, r.Scope);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 14. Comprehensive: BFS + DFS compilation order verification
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void BFS_LinearChain_ForwardOrder()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS, CompileDirection.Forward)[0];
        Assert.AreSame(n[0], r.Items[0].Node, "BFS Forward chain: first");
        Assert.AreSame(n[1], r.Items[1].Node, "BFS Forward chain: second");
        Assert.AreSame(n[2], r.Items[2].Node, "BFS Forward chain: third");
    }

    [TestMethod]
    public void DFS_LinearChain_ForwardOrder_PreOrder()
    {
        // 前序 DFS：父节点先进结果 → n0, n1, n2
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.DFS, CompileDirection.Forward)[0];
        Assert.AreSame(n[0], r.Items[0].Node, "DFS Forward chain: first (parent)");
        Assert.AreSame(n[1], r.Items[1].Node, "DFS Forward chain: second");
        Assert.AreSame(n[2], r.Items[2].Node, "DFS Forward chain: third");
    }

    [TestMethod]
    public void BFS_Diamond_ForwardOrder()
    {
        var (t, n) = GraphBuilder.BuildDiamond();
        var r = _compiler.Compile(n[0], CompileMode.BFS, CompileDirection.Forward)[0];
        // BFS: n0, 然后 n1/n2 (同层，顺序取决于分配)，然后 n3
        Assert.AreSame(n[0], r.Items[0].Node, "BFS Diamond: start");
        Assert.AreSame(n[3], r.Items[3].Node, "BFS Diamond: end (n3)");
        // n1, n2 顺序不定（同层），但 n3 必须在最后
    }

    [TestMethod]
    public void DFS_Diamond_ForwardOrder_PreOrder()
    {
        var (t, nodes) = GraphBuilder.BuildDiamond();
        var r = _compiler.Compile(nodes[0], CompileMode.DFS, CompileDirection.Forward)[0];
        // 前序 DFS: n0 → (n1分支 → n3) → 回溯 → (n2分支 → n3已访问)
        // 结果: [n0, n1, n3, n2] 或 [n0, n2, n3, n1]
        Assert.AreSame(nodes[0], r.Items[0].Node, "DFS Diamond: start");
        // n3 必须在 n1 或 n2 之后（至少一个子节点已探索）
        var idx3 = -1; var idx1 = -1; var idx2 = -1;
        for (int i = 0; i < r.Items.Count; i++)
        {
            if (r.Items[i].Node == nodes[1]) idx1 = i;
            if (r.Items[i].Node == nodes[2]) idx2 = i;
            if (r.Items[i].Node == nodes[3]) idx3 = i;
        }
        Assert.IsTrue(idx3 > idx1 || idx3 > idx2, "n3 should come after at least one branch node");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 15. Comprehensive: Direction + Scope
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Forward_FromStart_FullChain()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode)[0];
        Assert.AreEqual(3, r.Items.Count);
    }

    [TestMethod]
    public void Reverse_FromEnd_FullChainReverse()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[2], CompileMode.BFS, CompileDirection.Reverse, CompileScope.FromNode)[0];
        Assert.AreEqual(3, r.Items.Count);
        Assert.AreSame(n[2], r.Items[0].Node, "Reverse starts from end");
        Assert.AreSame(n[0], r.Items[2].Node, "Reverse ends at start");
    }

    [TestMethod]
    public void OmniForward_LinearChain_StartsFromEntry()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var results = _compiler.Compile(n[1], CompileMode.BFS, CompileDirection.Forward, CompileScope.Omni);
        Assert.AreEqual(1, results.Count, "Single entry (n0) in linear chain");
        var r = results[0];
        Assert.AreEqual(3, r.Items.Count);
        Assert.AreSame(n[0], r.Items[0].Node, "Omni+Forward discovers entry node");
    }

    [TestMethod]
    public void OmniReverse_LinearChain_StartsFromExit()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var results = _compiler.Compile(n[1], CompileMode.BFS, CompileDirection.Reverse, CompileScope.Omni);
        Assert.AreEqual(1, results.Count, "Single exit (n2) in linear chain");
        var r = results[0];
        Assert.AreEqual(3, r.Items.Count);
        Assert.AreSame(n[2], r.Items[0].Node, "Omni+Reverse discovers exit node");
    }

    [TestMethod]
    public void OmniForward_TwoEntries_BothUsed()
    {
        var (t, n) = GraphBuilder.BuildTwoEntries();
        var results = _compiler.Compile(n[0], CompileMode.BFS, CompileDirection.Forward, CompileScope.Omni);
        Assert.AreEqual(2, results.Count, "Two entries → 2 results");
        Assert.AreEqual(4, results.Sum(r => r.Items.Count), "Both sub-graphs covered");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 16. Comprehensive: Execution correctness
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task Execute_BFS_Chain_ResultPassThrough()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        var result = await r.ExecuteAsync("init");

        Assert.AreEqual("init", result, "BFS passes through initial param");
    }

    [TestMethod]
    public async Task Execute_DFS_Chain_ResultPassThrough()
    {
        // 前序 DFS: [n0, n1, n2]，执行顺序 n0→n1→n2
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.DFS)[0];
        var result = await r.ExecuteAsync("init");

        Assert.AreEqual("init", result, "DFS pre-order passes through initial param");
    }

    [TestMethod]
    public async Task Execute_BFS_Diamond_AllNodesExecuted()
    {
        var (t, n) = GraphBuilder.BuildDiamond();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        await r.ExecuteAsync();

        foreach (var node in n)
            Assert.AreEqual(1, node.TrackedCommand.ExecuteCount, $"BFS Diamond: {node.GetHashCode()} executed once");
    }

    [TestMethod]
    public async Task Execute_Reverse_Chain_ReversedOrder()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        // Reverse+BFS: [n2, n1, n0]
        var r = _compiler.Compile(n[2], CompileMode.BFS, CompileDirection.Reverse)[0];
        var result = await r.ExecuteAsync("init");

        Assert.AreEqual("init", result, "Reverse execution passes through initial param");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 17. Comprehensive: Cancellation
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task Execute_CancelledBeforeStart_Throws()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await r.ExecuteAsync("test", cts.Token);
            Assert.Fail("Expected OperationCanceledException");
        }
        catch (OperationCanceledException) { }
    }

    [TestMethod]
    public async Task Execute_NodeFailure_ContinuesToSubsequent()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        // n[1] 抛出普通异常 — 被 catch 捕获，FailureException 设值后仍继续执行
        n[1].TrackedCommand.AfterExecute = _ => throw new InvalidOperationException("boom");

        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        await r.ExecuteAsync();

        Assert.AreEqual(1, n[0].TrackedCommand.ExecuteCount, "first executes");
        Assert.AreEqual(1, n[1].TrackedCommand.ExecuteCount, "second throws, caught");
        Assert.AreEqual(1, n[2].TrackedCommand.ExecuteCount, "third still executes (error not fatal)");
    }

    [TestMethod]
    public async Task Execute_CancelledInMiddle_StopsChain()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        using var cts = new CancellationTokenSource();

        // n[1] 执行时触发取消
        n[1].TrackedCommand.AfterExecute = _ => cts.Cancel();

        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];

        try
        {
            await r.ExecuteAsync("test", cts.Token);
            Assert.Fail("Expected OperationCanceledException");
        }
        catch (OperationCanceledException) { }

        Assert.AreEqual(1, n[0].TrackedCommand.ExecuteCount, "n0 executes before cancel");
        Assert.AreEqual(1, n[1].TrackedCommand.ExecuteCount, "n1 triggers cancel");
        Assert.AreEqual(0, n[2].TrackedCommand.ExecuteCount, "n2 must NOT execute after cancel");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 18. Comprehensive: ICompileTimeSink TotalCount
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task CompileTimeSink_TotalCount_Correct()
    {
        var t = new StubTree();
        var sink = new SinkNode { Parent = t };
        t.Nodes.Add(sink);

        var r = _compiler.Compile(sink, CompileMode.BFS)[0];
        await r.ExecuteAsync();

        Assert.AreEqual(1, sink.Events[0].TotalCount, "TotalCount = 1 for single node");
        Assert.AreEqual(1, sink.Events.Last().TotalCount, "OnCompleted TotalCount = 1");
    }

    [TestMethod]
    public async Task CompileTimeSink_TotalCount_ThreeNodes()
    {
        var t = new StubTree();
        var sinks = new[] { new SinkNode { Parent = t }, new SinkNode { Parent = t }, new SinkNode { Parent = t } };
        var slots = sinks.Select(s => new StubSlot { Parent = s }).ToArray();
        for (int i = 0; i < 3; i++) sinks[i].Slots.Add(slots[i]);
        GraphBuilder.Connect(slots[0], slots[1]);
        GraphBuilder.Connect(slots[1], slots[2]);
        foreach (var s in sinks) t.Nodes.Add(s);

        var r = _compiler.Compile(sinks[0], CompileMode.BFS)[0];
        await r.ExecuteAsync();

        foreach (var sink in sinks)
        {
            var be = sink.Events.First(e => e.Event == ExecutionEvent.BeforeExecute);
            Assert.AreEqual(3, be.TotalCount, "TotalCount = 3 for chain of 3");
        }
        var completedEvents = sinks[0].Events.Where(e => e.Event == ExecutionEvent.OnCompleted);
        foreach (var ce in completedEvents)
            Assert.AreEqual(3, ce.TotalCount, "OnCompleted TotalCount = 3");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 19. Comprehensive: Skipped items lifecycle
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task BranchExclusive_SkippedItem_GetsBothLifecycleEvents()
    {
        var t = new StubTree();
        var n0 = new RoutingNode { Parent = t, CurrentRouteKey = true };
        var n1 = new SinkNode { Parent = t }; // true 分支
        var n2 = new SinkNode { Parent = t }; // false 分支

        n0.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            [true] = n1,
            [false] = n2,
        };
        var s = new[] { new StubSlot{Parent=n0}, new StubSlot{Parent=n1}, new StubSlot{Parent=n2} };
        n0.Slots.Add(s[0]); n1.Slots.Add(s[1]); n2.Slots.Add(s[2]);
        GraphBuilder.Connect(s[0], s[1]); GraphBuilder.Connect(s[0], s[2]);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2);

        var r = _compiler.Compile(n0, CompileMode.BFS)[0];
        await r.ExecuteAsync();

        // n2 被跳过，但仍应收到 BeforeExecute + AfterExecute
        var skippedEvents = n2.Events.Where(e => e.Event == ExecutionEvent.BeforeExecute ||
                                                  e.Event == ExecutionEvent.AfterExecute).ToList();
        Assert.AreEqual(2, skippedEvents.Count,
            "Skipped node gets both BeforeExecute and AfterExecute");
        Assert.AreEqual(ExecutionEvent.BeforeExecute, skippedEvents[0].Event);
        Assert.AreEqual(ExecutionEvent.AfterExecute, skippedEvents[1].Event);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 20. Comprehensive: Config matrix — all combinations compile without error
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void ConfigMatrix_AllCombinations_CompileWithoutError()
    {
        var modes = new[] { CompileMode.BFS, CompileMode.DFS };
        var dirs = new[] { CompileDirection.Forward, CompileDirection.Reverse };
        var scopes = new[] { CompileScope.FromNode, CompileScope.Omni };
        var cycles = new[] { CycleHandling.Throw, CycleHandling.Trim, CycleHandling.Allow };

        int tested = 0;
        foreach (var mode in modes)
        foreach (var dir in dirs)
        foreach (var scope in scopes)
        foreach (var cycle in cycles)
        {
            // Throw 模式在环路图上会抛异常，用线性链测试
            var (tree, nodes) = cycle == CycleHandling.Throw
                ? GraphBuilder.BuildLinearChain()
                : GraphBuilder.BuildCycle();

            var startNode = dir == CompileDirection.Reverse
                ? nodes[^1] // 反向从末尾开始
                : nodes[0];

            try
            {
                var results = _compiler.Compile(startNode, mode, dir, scope, cycle);
                // 环路图 + Omni: 可能返回空列表（无自然边界点）
                if (scope == CompileScope.Omni && cycle != CycleHandling.Throw)
                {
                    // 环路+Omni 无入度/出度为0的节点 → 空结果合法
                    tested++;
                    continue;
                }
                Assert.IsTrue(results.Count > 0, $"At least one result for {mode}/{dir}/{scope}/{cycle}");
                var r = results[0];
                Assert.IsNotNull(r);
                // 线性链+Omni 应有节点
                if (scope == CompileScope.Omni)
                    Assert.IsTrue(r.Items.Count > 0, $"Items should exist for {mode}/{dir}/{scope}/{cycle}");
                else
                    Assert.IsTrue(r.Items.Count > 0, $"Items not empty for {mode}/{dir}/{scope}/{cycle}");
                tested++;
            }
            catch (InvalidOperationException) when (cycle == CycleHandling.Throw)
            {
                tested++; // 环路+Throw 预期抛异常
            }
        }

        Assert.AreEqual(2 * 2 * 2 * 3, tested, "All 24 config combinations compiled");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 21. Edge case: Router with RouteTable but no slot connections
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void RouterWithRouteTable_NoSlotConnections_OnlyRouterFound()
    {
        var t = new StubTree();
        var downstream = new StubNode { Parent = t };
        var router = new RoutingNode { Parent = t };
        // RouteTable 有下游节点引用
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["target"] = downstream,
        };
        // 但不创建任何 Slot 连接
        t.Nodes.Add(router);
        t.Nodes.Add(downstream);

        // Forward + FromNode: 从 router 出发，无出边 → 只找到 router
        var r = _compiler.Compile(router, CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode)[0];
        Assert.AreEqual(1, r.Items.Count, "Only router should be found — no slot connections");
        Assert.AreSame(router, r.Items[0].Node);

        // RouteTable 依然被收集
        Assert.IsNotNull(r.Items[0].RouteTable);
        Assert.AreEqual(1, r.Items[0].RouteTable.Count);
        Assert.AreSame(downstream, r.Items[0].RouteTable["target"]);

        // BranchExclusiveItems 应为 null — 下游不在 items 中，ComputeBranchExclusives 无法建立映射
        Assert.IsNull(r.Items[0].BranchExclusiveItems);
    }

    [TestMethod]
    public void RouterWithRouteTable_NoSlotConnections_OmniForward_OnlyEntryFound()
    {
        var t = new StubTree();
        var downstream = new StubNode { Parent = t };
        var router = new RoutingNode { Parent = t };
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["target"] = downstream,
        };
        t.Nodes.Add(router);
        t.Nodes.Add(downstream);

        // Omni + Forward: 两个节点入度都是 0，但无连接，各自独立
        var results = _compiler.Compile(router, CompileMode.BFS, CompileDirection.Forward, CompileScope.Omni);
        // 两个节点入度都为 0，都会作为起点 → 2 个独立结果
        Assert.AreEqual(2, results.Count, "Both nodes are entries (in-degree=0) — 2 results");
        Assert.AreEqual(1, results[0].Items.Count, "Each result has 1 item");
        Assert.AreEqual(1, results[1].Items.Count, "Each result has 1 item");
    }

    [TestMethod]
    public void RouterWithRouteTable_NoSlotConnections_ExecutesOnlyRouter()
    {
        var t = new StubTree();
        var downstream = new StubNode { Parent = t };
        var router = new RoutingNode { Parent = t };
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["target"] = downstream,
        };
        t.Nodes.Add(router);
        t.Nodes.Add(downstream);

        var r = _compiler.Compile(router, CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode)[0];
        var result = r.ExecuteAsync().Result;

        // 只有 router 被执行
        Assert.AreEqual(1, router.TrackedCommand.ExecuteCount, "Router executes");
        Assert.AreEqual(0, downstream.TrackedCommand.ExecuteCount, "Downstream NOT executed — no slot connection");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 22. Edge case: Connect → disconnect (simulate SetSelector switch)
    //     Then compile should NOT find the disconnected downstream
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void ConnectThenDisconnect_CompileFindsOnlyRouter()
    {
        var t = new StubTree();
        var router = new RoutingNode { Parent = t };
        var downstream = new StubNode { Parent = t };

        // 1) 建立连接（模拟最初 NetworkRequestMethod 的连接）
        var routerSlot = new StubSlot { Parent = router };
        var downstreamSlot = new StubSlot { Parent = downstream };
        router.Slots.Add(routerSlot);
        downstream.Slots.Add(downstreamSlot);
        GraphBuilder.Connect(routerSlot, downstreamSlot);  // routerSlot.Targets = [downstreamSlot]
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["Get"] = downstream,
        };
        t.Nodes.Add(router);
        t.Nodes.Add(downstream);

        // 验证连接存在
        var r1 = _compiler.Compile(router, CompileMode.BFS)[0];
        Assert.AreEqual(2, r1.Items.Count, "Both found when connected");

        // 2) 断开连接（模拟 SetSelector(typeof(VoltageRange)) 的清理逻辑）
        //    清空 Targets/Sources
        routerSlot.Targets.Clear();
        downstreamSlot.Sources.Clear();
        //    从 Parent.Slots 中移除旧 Slot
        router.Slots.Remove(routerSlot);
        //    清空 RouteTable（新的 VoltageRange slots 没有 Targets）
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>();
        //    创建新 Slot（模拟 VoltageRange 的新 slots）
        var newSlot = new StubSlot { Parent = router };
        router.Slots.Add(newSlot);
        //    router 的 Slots 现在是: [newSlot] — 没有下游连接

        // 3) 编译 → 应该只找到 router
        var r2 = _compiler.Compile(router, CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode)[0];
        Assert.AreEqual(1, r2.Items.Count, "Only router after disconnect — downstream NOT reachable");
        Assert.AreSame(router, r2.Items[0].Node);
        Assert.IsNotNull(r2.Items[0].RouteTable);
        Assert.AreEqual(0, r2.Items[0].RouteTable.Count, "RouteTable should be empty");
    }

    [TestMethod]
    public async Task ConnectThenDisconnect_ExecuteFindsOnlyRouter()
    {
        var t = new StubTree();
        var router = new RoutingNode { Parent = t };
        var downstream = new StubNode { Parent = t };

        // 1) 建立连接
        var routerSlot = new StubSlot { Parent = router };
        var downstreamSlot = new StubSlot { Parent = downstream };
        router.Slots.Add(routerSlot);
        downstream.Slots.Add(downstreamSlot);
        GraphBuilder.Connect(routerSlot, downstreamSlot);
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["Get"] = downstream,
        };
        t.Nodes.Add(router);
        t.Nodes.Add(downstream);

        // 2) 断开连接（模拟 SetSelector 切换）
        routerSlot.Targets.Clear();
        downstreamSlot.Sources.Clear();
        router.Slots.Remove(routerSlot);
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>();
        var newSlot = new StubSlot { Parent = router };
        router.Slots.Add(newSlot);

        // 3) 执行 → downstream 不应被执行
        var r = _compiler.Compile(router, CompileMode.BFS)[0];
        await r.ExecuteAsync();

        Assert.AreEqual(1, router.TrackedCommand.ExecuteCount, "Router executes");
        Assert.AreEqual(0, downstream.TrackedCommand.ExecuteCount,
            "Disconnected downstream MUST NOT execute");
    }

    [TestMethod]
    public async Task ConnectThenDisconnect_OmniMode_DownstreamIsSeparateEntry()
    {
        var t = new StubTree();
        var router = new RoutingNode { Parent = t };
        var downstream = new StubNode { Parent = t };

        // 1) 建立连接
        var routerSlot = new StubSlot { Parent = router };
        var downstreamSlot = new StubSlot { Parent = downstream };
        router.Slots.Add(routerSlot);
        downstream.Slots.Add(downstreamSlot);
        GraphBuilder.Connect(routerSlot, downstreamSlot);
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["target"] = downstream,
        };
        t.Nodes.Add(router);
        t.Nodes.Add(downstream);

        // 2) 编译（连接时）→ 2个节点
        var rBefore = _compiler.Compile(router, CompileMode.BFS)[0];
        Assert.AreEqual(2, rBefore.Items.Count);

        // 3) 断开连接
        routerSlot.Targets.Clear();
        downstreamSlot.Sources.Clear();
        router.Slots.Remove(routerSlot);
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>();
        var newSlot = new StubSlot { Parent = router };
        router.Slots.Add(newSlot);

        // 4) FromNode 模式 → 只有 router
        var rFromNode = _compiler.Compile(router, CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode)[0];
        Assert.AreEqual(1, rFromNode.Items.Count, "FromNode: only router after disconnect");

        // 5) Omni+Forward 模式 → 各自独立结果
        var resultsOmni = _compiler.Compile(router, CompileMode.BFS, CompileDirection.Forward, CompileScope.Omni);
        Assert.AreEqual(2, resultsOmni.Count, "Omni: 2 independent results (router + downstream)");

        // 6) 执行 FromNode 结果 → downstream 不应被执行
        await rFromNode.ExecuteAsync();
        Assert.AreEqual(1, router.TrackedCommand.ExecuteCount, "Router executes");
        Assert.AreEqual(0, downstream.TrackedCommand.ExecuteCount,
            "Disconnected downstream NOT executed via FromNode path");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 23. Depth computation
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void BFS_LinearChain_DepthIncrements()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS, CompileDirection.Forward)[0];
        Assert.AreEqual(0, r.Items[0].Depth, "n0 depth=0");
        Assert.AreEqual(1, r.Items[1].Depth, "n1 depth=1");
        Assert.AreEqual(2, r.Items[2].Depth, "n2 depth=2");
    }

    [TestMethod]
    public void DFS_LinearChain_DepthIncrements()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.DFS, CompileDirection.Forward)[0];
        Assert.AreEqual(0, r.Items[0].Depth, "n0 depth=0");
        Assert.AreEqual(1, r.Items[1].Depth, "n1 depth=1");
        Assert.AreEqual(2, r.Items[2].Depth, "n2 depth=2");
    }

    [TestMethod]
    public void BFS_Diamond_DepthCorrect()
    {
        // n0 → n1, n2 → n3
        var (t, n) = GraphBuilder.BuildDiamond();
        var r = _compiler.Compile(n[0], CompileMode.BFS)[0];
        Assert.AreEqual(0, r.Items[0].Depth, "n0 depth=0 (start)");
        // n1, n2 同层 → depth=1
        var d1 = r.Items.First(i => i.Node == n[1]).Depth;
        var d2 = r.Items.First(i => i.Node == n[2]).Depth;
        Assert.AreEqual(1, d1, "n1 depth=1");
        Assert.AreEqual(1, d2, "n2 depth=1");
        // n3 是 n1/n2 的共同子节点 → depth=2
        Assert.AreEqual(2, r.Items.First(i => i.Node == n[3]).Depth, "n3 depth=2");
    }

    [TestMethod]
    public void BFS_RouterNode_DoesNotIncrementDepth()
    {
        // n0 → n1(router) → n2, n3
        var t = new StubTree();
        var n0 = new StubNode { Parent = t };
        var n1 = new RoutingNode { Parent = t };
        var n2 = new StubNode { Parent = t };
        var n3 = new StubNode { Parent = t };
        n1.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["a"] = n2,
            ["b"] = n3,
        };
        var s = new[] { new StubSlot{Parent=n0}, new StubSlot{Parent=n1}, new StubSlot{Parent=n2}, new StubSlot{Parent=n3} };
        n0.Slots.Add(s[0]); n1.Slots.Add(s[1]); n2.Slots.Add(s[2]); n3.Slots.Add(s[3]);
        GraphBuilder.Connect(s[0], s[1]);
        GraphBuilder.Connect(s[1], s[2]);
        GraphBuilder.Connect(s[1], s[3]);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2); t.Nodes.Add(n3);

        var r = _compiler.Compile(n0, CompileMode.BFS)[0];
        var item0 = r.Items.First(i => i.Node == n0);
        var item1 = r.Items.First(i => i.Node == n1);
        var item2 = r.Items.First(i => i.Node == n2);
        var item3 = r.Items.First(i => i.Node == n3);

        Assert.AreEqual(0, item0.Depth, "n0 depth=0");
        Assert.AreEqual(1, item1.Depth, "n1 (router) depth=1 — counted");
        // Router 不增加深度，所以 n2, n3 的深度 = n1 的深度 = 1
        Assert.AreEqual(1, item2.Depth, "n2 (past router) depth=1, not 2");
        Assert.AreEqual(1, item3.Depth, "n3 (past router) depth=1, not 2");
    }

    [TestMethod]
    public void BFS_DoubleRouter_DepthSkipsBoth()
    {
        // n0 → n1(router) → n2(router) → n3
        var t = new StubTree();
        var n0 = new StubNode { Parent = t };
        var n1 = new RoutingNode { Parent = t };
        var n2 = new RoutingNode { Parent = t };
        var n3 = new StubNode { Parent = t };
        n1.RouteTable = new Dictionary<object, IWorkflowNodeViewModel> { ["x"] = n2 };
        n2.RouteTable = new Dictionary<object, IWorkflowNodeViewModel> { ["y"] = n3 };
        var s = new[] { new StubSlot{Parent=n0}, new StubSlot{Parent=n1}, new StubSlot{Parent=n2}, new StubSlot{Parent=n3} };
        n0.Slots.Add(s[0]); n1.Slots.Add(s[1]); n2.Slots.Add(s[2]); n3.Slots.Add(s[3]);
        GraphBuilder.Connect(s[0], s[1]); GraphBuilder.Connect(s[1], s[2]); GraphBuilder.Connect(s[2], s[3]);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2); t.Nodes.Add(n3);

        var r = _compiler.Compile(n0, CompileMode.BFS)[0];
        Assert.AreEqual(0, r.Items.First(i => i.Node == n0).Depth, "n0 depth=0");
        Assert.AreEqual(1, r.Items.First(i => i.Node == n1).Depth, "n1(router) depth=1");
        Assert.AreEqual(1, r.Items.First(i => i.Node == n2).Depth, "n2(router) depth=1");
        Assert.AreEqual(1, r.Items.First(i => i.Node == n3).Depth, "n3 depth=1");
    }

    [TestMethod]
    public void DFS_RouterNode_DoesNotIncrementDepth()
    {
        // n0 → n1(router) → n2
        var t = new StubTree();
        var n0 = new StubNode { Parent = t };
        var n1 = new RoutingNode { Parent = t };
        var n2 = new StubNode { Parent = t };
        n1.RouteTable = new Dictionary<object, IWorkflowNodeViewModel> { ["t"] = n2 };
        var s = new[] { new StubSlot{Parent=n0}, new StubSlot{Parent=n1}, new StubSlot{Parent=n2} };
        n0.Slots.Add(s[0]); n1.Slots.Add(s[1]); n2.Slots.Add(s[2]);
        GraphBuilder.Connect(s[0], s[1]); GraphBuilder.Connect(s[1], s[2]);
        t.Nodes.Add(n0); t.Nodes.Add(n1); t.Nodes.Add(n2);

        var r = _compiler.Compile(n0, CompileMode.DFS)[0];
        Assert.AreEqual(0, r.Items.First(i => i.Node == n0).Depth, "n0 depth=0");
        Assert.AreEqual(1, r.Items.First(i => i.Node == n1).Depth, "n1(router) depth=1");
        Assert.AreEqual(1, r.Items.First(i => i.Node == n2).Depth, "n2 depth=1 (router doesn't increment)");
    }

    [TestMethod]
    public void Reverse_DepthReflectsReverseTraversal()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[2], CompileMode.BFS, CompileDirection.Reverse)[0];
        // Reverse: n2(0) → n1(1) → n0(2)
        Assert.AreEqual(0, r.Items.First(i => i.Node == n[2]).Depth, "n2 depth=0 (start)");
        Assert.AreEqual(1, r.Items.First(i => i.Node == n[1]).Depth, "n1 depth=1");
        Assert.AreEqual(2, r.Items.First(i => i.Node == n[0]).Depth, "n0 depth=2");
    }

    [TestMethod]
    public void ConnectThenDisconnect_OmniMode_FindsDisconnectedAsEntry()
    {
        var t = new StubTree();
        var router = new RoutingNode { Parent = t };
        var downstream1 = new StubNode { Parent = t };
        var downstream2 = new StubNode { Parent = t };
        var finalize = new StubNode { Parent = t };

        // 建立连接: router → downstream1, router → downstream2 → finalize
        var rSlot1 = new StubSlot { Parent = router };
        var rSlot2 = new StubSlot { Parent = router };
        var d1Slot = new StubSlot { Parent = downstream1 };
        var d2InSlot = new StubSlot { Parent = downstream2 };
        var d2OutSlot = new StubSlot { Parent = downstream2 };
        var fSlot = new StubSlot { Parent = finalize };
        router.Slots.Add(rSlot1); router.Slots.Add(rSlot2);
        downstream1.Slots.Add(d1Slot);
        downstream2.Slots.Add(d2InSlot); downstream2.Slots.Add(d2OutSlot);
        finalize.Slots.Add(fSlot);
        GraphBuilder.Connect(rSlot1, d1Slot);
        GraphBuilder.Connect(rSlot2, d2InSlot);
        GraphBuilder.Connect(d2OutSlot, fSlot);
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>
        {
            ["Get"] = downstream1,
            ["Post"] = downstream2,
        };
        t.Nodes.Add(router);
        t.Nodes.Add(downstream1);
        t.Nodes.Add(downstream2);
        t.Nodes.Add(finalize);

        // 编译（连接时）— 一个连通图
        var rBefore = _compiler.Compile(router, CompileMode.BFS, CompileDirection.Forward, CompileScope.Omni);
        Assert.AreEqual(1, rBefore.Count, "Single connected graph → 1 result");
        Assert.AreEqual(4, rBefore[0].Items.Count, "All 4 nodes in one connected graph");

        // 断开 router 的所有下游连接（模拟 SetSelector 切换）
        rSlot1.Targets.Clear(); d1Slot.Sources.Clear(); router.Slots.Remove(rSlot1);
        rSlot2.Targets.Clear(); d2InSlot.Sources.Clear(); router.Slots.Remove(rSlot2);
        router.RouteTable = new Dictionary<object, IWorkflowNodeViewModel>();

        // Omni+Forward 编译 → disconnected 节点成为独立入口
        var rAfter = _compiler.Compile(router, CompileMode.BFS, CompileDirection.Forward, CompileScope.Omni);
        // 3个入口: router, downstream1, downstream2
        Assert.AreEqual(3, rAfter.Count,
            "Omni: 3 results — router + disconnected d1 + d2");
        // rAfter[0] = router (alone)
        // rAfter[1] = downstream1 (alone)
        // rAfter[2] = downstream2 → finalize (2 items)
        var entryNodes = rAfter.Select(r => r.Items[0].Node).ToHashSet();
        Assert.IsTrue(entryNodes.Contains(router), "router is entry");
        Assert.IsTrue(entryNodes.Contains(downstream1), "disconnected d1 is entry");
        Assert.IsTrue(entryNodes.Contains(downstream2), "disconnected d2 is entry");
    }
}
