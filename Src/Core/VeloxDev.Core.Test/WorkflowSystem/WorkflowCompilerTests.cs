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
    public void Execute(object? parameter) { ExecuteCount++; Parameters.Add(parameter); AfterExecute?.Invoke(parameter); RaiseExited(); }
    private void RaiseExited() => Exited?.Invoke(new CommandEventArgs(null!, CommandEventType.Exited));
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
    public object? WorkResult { get; set; }

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

    public IReadOnlyDictionary<object, IWorkflowNodeViewModel> GetRouteTable() => RouteTable;
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

    [TestMethod] public void BFS_LinearChain_ThreeItems() { var r = _compiler.Compile(GraphBuilder.BuildLinearChain().Nodes[0], CompileMode.BFS); Assert.AreEqual(3, r.Items.Count); }
    [TestMethod] public void BFS_Diamond_FourItems() { var r = _compiler.Compile(GraphBuilder.BuildDiamond().Nodes[0], CompileMode.BFS); Assert.AreEqual(4, r.Items.Count); }
    [TestMethod] public void DFS_LinearChain_ThreeItems() { var r = _compiler.Compile(GraphBuilder.BuildLinearChain().Nodes[0], CompileMode.DFS); Assert.AreEqual(3, r.Items.Count); }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. CompileDirection — Forward / Reverse
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Forward_FromMiddle_ReachesDownstreamOnly()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[1], CompileMode.BFS, CompileDirection.Forward);
        Assert.AreEqual(2, r.Items.Count);
        Assert.AreSame(n[1], r.Items[0].Node);
        Assert.AreSame(n[2], r.Items[1].Node);
    }

    [TestMethod]
    public void Reverse_FromMiddle_ReachesUpstreamOnly()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[1], CompileMode.BFS, CompileDirection.Reverse);
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
        var r = _compiler.Compile(GraphBuilder.BuildLinearChain().Nodes[0], CompileMode.BFS);
        Assert.AreEqual(CompileScope.FromNode, r.Scope);
    }

    [TestMethod]
    public void Omni_TwoEntries_ReturnsAllFour()
    {
        var (t, n) = GraphBuilder.BuildTwoEntries();
        var r = _compiler.Compile(n[0], CompileMode.BFS, CompileDirection.Forward, CompileScope.Omni);
        Assert.AreEqual(4, r.Items.Count);
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
        var r = _compiler.Compile(GraphBuilder.BuildCycle().Nodes[0], CompileMode.BFS, cycleHandling: CycleHandling.Trim);
        Assert.AreEqual(3, r.Items.Count);
        Assert.IsTrue(r.HasCycle);
    }

    [TestMethod]
    public void Cycle_Allow_LoopEntryMarked()
    {
        var r = _compiler.Compile(GraphBuilder.BuildCycle().Nodes[0], CompileMode.BFS, cycleHandling: CycleHandling.Allow);
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
        var r = _compiler.Compile(n[0], CompileMode.BFS);
        await r.ExecuteAsync();
        foreach (var node in n.Cast<StubNode>())
            Assert.AreEqual(1, node.TrackedCommand.ExecuteCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Result chaining
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ResultChaining_ReturnsLastWorkResult()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var s = n.Cast<StubNode>().ToArray();
        // 在每个节点执行后设置 WorkResult 以模拟链式传递
        s[0].TrackedCommand.AfterExecute = _ => n[0].WorkResult = "r0";
        s[1].TrackedCommand.AfterExecute = _ => n[1].WorkResult = "r1";
        s[2].TrackedCommand.AfterExecute = _ => n[2].WorkResult = "r2";

        var r = _compiler.Compile(n[0], CompileMode.BFS);
        var result = await r.ExecuteAsync("start");
        Assert.AreEqual("r2", result);
    }

    [TestMethod]
    public async Task ResultChaining_NoWorkResult_ReturnsInitialParam()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS);
        var result = await r.ExecuteAsync("hello");
        Assert.AreEqual("hello", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. Error redirect
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ErrorRedirect_ExecutesTarget()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS);
        r.Items[0].ErrorRedirectId = 2;

        // Simulate: item 0 fails
        ((StubNode)n[0]).TrackedCommand.Failed += _ => throw new InvalidOperationException("fail");

        await r.ExecuteAsync();
        Assert.AreEqual(1, ((StubNode)n[0]).TrackedCommand.ExecuteCount);
        Assert.AreEqual(1, ((StubNode)n[2]).TrackedCommand.ExecuteCount);
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

        var r = _compiler.Compile(router, CompileMode.BFS);
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
            var r1 = _compiler.Compile(router, mode, CompileDirection.Forward, CompileScope.FromNode);
            var item1 = r1.Items.FirstOrDefault(i => i.Node == router);
            Assert.IsNotNull(item1, $"Router not found for {mode}/FromNode");
            Assert.IsNotNull(item1.RouteTable);
            Assert.AreSame(target, item1.RouteTable["x"]);

            var r2 = _compiler.Compile(router, mode, CompileDirection.Forward, CompileScope.Omni);
            var item2 = r2.Items.FirstOrDefault(i => i.Node == router);
            Assert.IsNotNull(item2, $"Router not found for {mode}/Omni");
            Assert.IsNotNull(item2.RouteTable);
            Assert.AreSame(target, item2.RouteTable["x"]);
        }

        // Reverse + FromNode
        var revR = _compiler.Compile(target, CompileMode.BFS, CompileDirection.Reverse);
        var revItem = revR.Items.FirstOrDefault(i => i.Node == router);
        Assert.IsNotNull(revItem, "Router not found in Reverse mode");
        Assert.IsNotNull(revItem.RouteTable, "RouteTable is null in Reverse mode");
    }

    [TestMethod]
    public void NonRouterNode_RouteTableIsNull()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS);
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

        var r = _compiler.Compile(sink, CompileMode.BFS);
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

        var r = _compiler.Compile(sink, CompileMode.BFS);
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

        var r = _compiler.Compile(n0, CompileMode.BFS);
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

        var r = _compiler.Compile(n0, CompileMode.BFS);
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

        var r = _compiler.Compile(n0, CompileMode.BFS);
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

        var r = _compiler.Compile(n0, CompileMode.DFS);
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
    // 12. Edge cases
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void SingleNode_ReturnsOneItem()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS);
        Assert.AreEqual(1, r.Items.Count);
    }

    [TestMethod]
    public void SingleNode_NoCycle()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS);
        Assert.IsFalse(r.HasCycle);
    }

    [TestMethod]
    public void LinearChain_NoCycle()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS);
        Assert.IsFalse(r.HasCycle);
    }

    [TestMethod]
    public void Cycle_HasCycleTrue()
    {
        var r = _compiler.Compile(GraphBuilder.BuildCycle().Nodes[0], CompileMode.BFS, cycleHandling: CycleHandling.Trim);
        Assert.IsTrue(r.HasCycle);
    }

    [TestMethod]
    public void CompiledItem_WrapsNode()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS);
        Assert.AreSame(n[0], r.Items[0].Node);
    }

    [TestMethod]
    public void CompiledItem_DefaultProperties()
    {
        var (t, n) = GraphBuilder.BuildSingleNode();
        var r = _compiler.Compile(n[0], CompileMode.BFS);
        Assert.IsNull(r.Items[0].ErrorRedirectId);
        Assert.AreEqual(0, r.Items[0].MaxRetries);
        Assert.IsFalse(r.Items[0].IsLoopEntry);
    }

    [TestMethod]
    public void CompiledItem_Order_Sequential()
    {
        var (t, n) = GraphBuilder.BuildLinearChain();
        var r = _compiler.Compile(n[0], CompileMode.BFS);
        for (int i = 0; i < r.Items.Count; i++)
            Assert.AreEqual(i, r.Items[i].Order);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 13. Result metadata
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Result_StoresMode()
    {
        var r = _compiler.Compile(GraphBuilder.BuildSingleNode().Nodes[0], CompileMode.DFS);
        Assert.AreEqual(CompileMode.DFS, r.Mode);
    }

    [TestMethod]
    public void Result_DirectionAndScope_Defaults()
    {
        var r = _compiler.Compile(GraphBuilder.BuildSingleNode().Nodes[0], CompileMode.BFS);
        Assert.AreEqual(CompileDirection.Forward, r.Direction);
        Assert.AreEqual(CompileScope.FromNode, r.Scope);
    }
}
