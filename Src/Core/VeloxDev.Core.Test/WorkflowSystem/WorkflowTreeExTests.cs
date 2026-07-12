using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Test.WorkflowSystem;

// ── Stubs ───────────────────────────────────────────────────────────────────

file sealed class StubCommand : IVeloxCommand
{
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
    public void Execute(object? parameter) { }
    public void Lock() { }
    public void UnLock() { }
    public void Notify() { }
    public void Clear() { }
    public void Interrupt() { }
    public void Continue() { }
    public void ChangeSemaphore(int semaphore) { }
    public Task ExecuteAsync(object? parameter) => Task.CompletedTask;
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

file sealed class StubNode : IWorkflowNodeViewModel
{
    public IWorkflowTreeViewModel? Parent { get; set; }
    public Anchor Anchor { get; set; } = new();
    public Size Size { get; set; } = new();
    public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; } = [];

    public IVeloxCommand MoveCommand { get; } = new StubCommand();
    public IVeloxCommand SetAnchorCommand { get; } = new StubCommand();
    public IVeloxCommand SetSizeCommand { get; } = new StubCommand();
    public IVeloxCommand CreateSlotCommand { get; } = new StubCommand();
    public IVeloxCommand DeleteCommand { get; } = new StubCommand();
    public IVeloxCommand WorkCommand { get; } = new StubCommand();
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

    // ── Tests ────────────────────────────────────────────────────────────────────

[TestClass]
public class WorkflowTreeExTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and adds a slot to the given node, returning it via IWorkflowSlotViewModel.
    /// </summary>
    private static IWorkflowSlotViewModel MakeSlot(IWorkflowNodeViewModel owner)
    {
        var slot = new StubSlot { Parent = owner };
        owner.Slots.Add(slot);
        return slot;
    }

    /// <summary>
    /// Connects sender.Targets → receiver and receiver.Sources → sender.
    /// </summary>
    private static void Connect(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        sender.Targets.Add(receiver);
        receiver.Sources.Add(sender);
    }

    // ── GetNodeInDegree ──────────────────────────────────────────────────

    [TestMethod]
    public void GetNodeInDegree_NoConnections_ReturnsZero()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode());
        tree.Nodes.Add(new StubNode());

        Assert.AreEqual(0, tree.GetNodeInDegree(0));
        Assert.AreEqual(0, tree.GetNodeInDegree(1));
    }

    [TestMethod]
    public void GetNodeInDegree_SingleIncoming_ReturnsOne()
    {
        var tree = new StubTree();
        var nodeA = new StubNode();
        var nodeB = new StubNode();
        tree.Nodes.Add(nodeA);
        tree.Nodes.Add(nodeB);

        var slotA = MakeSlot(nodeA);
        var slotB = MakeSlot(nodeB);
        Connect(slotA, slotB);

        Assert.AreEqual(0, tree.GetNodeInDegree(0)); // nodeA
        Assert.AreEqual(1, tree.GetNodeInDegree(1)); // nodeB
    }

    [TestMethod]
    public void GetNodeInDegree_MultipleIncoming_ReturnsCorrectCount()
    {
        var tree = new StubTree();
        var upstream1 = new StubNode();
        var upstream2 = new StubNode();
        var downstream = new StubNode();
        tree.Nodes.Add(upstream1);
        tree.Nodes.Add(upstream2);
        tree.Nodes.Add(downstream);

        Connect(MakeSlot(upstream1), MakeSlot(downstream));
        Connect(MakeSlot(upstream2), MakeSlot(downstream));

        Assert.AreEqual(2, tree.GetNodeInDegree(2));
    }

    [TestMethod]
    public void GetNodeInDegree_NullTree_Throws()
    {
        IWorkflowTreeViewModel? tree = null;
        Assert.ThrowsExactly<ArgumentNullException>(() => tree!.GetNodeInDegree(0));
    }

    [TestMethod]
    public void GetNodeInDegree_NegativeIndex_Throws()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tree.GetNodeInDegree(-1));
    }

    [TestMethod]
    public void GetNodeInDegree_IndexOutOfRange_Throws()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tree.GetNodeInDegree(5));
    }

    // ── GetNodeOutDegree ─────────────────────────────────────────────────

    [TestMethod]
    public void GetNodeOutDegree_NoConnections_ReturnsZero()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode());

        Assert.AreEqual(0, tree.GetNodeOutDegree(0));
    }

    [TestMethod]
    public void GetNodeOutDegree_SingleOutgoing_ReturnsOne()
    {
        var tree = new StubTree();
        var nodeA = new StubNode();
        var nodeB = new StubNode();
        tree.Nodes.Add(nodeA);
        tree.Nodes.Add(nodeB);

        Connect(MakeSlot(nodeA), MakeSlot(nodeB));

        Assert.AreEqual(1, tree.GetNodeOutDegree(0));
        Assert.AreEqual(0, tree.GetNodeOutDegree(1));
    }

    [TestMethod]
    public void GetNodeOutDegree_MultipleSlotsEachWithTargets_ReturnsCorrectCount()
    {
        var tree = new StubTree();
        var source = new StubNode();
        var dest1 = new StubNode();
        var dest2 = new StubNode();
        tree.Nodes.Add(source);
        tree.Nodes.Add(dest1);
        tree.Nodes.Add(dest2);

        Connect(MakeSlot(source), MakeSlot(dest1));
        Connect(MakeSlot(source), MakeSlot(dest2));

        Assert.AreEqual(2, tree.GetNodeOutDegree(0));
    }

    [TestMethod]
    public void GetNodeOutDegree_NullTree_Throws()
    {
        IWorkflowTreeViewModel? tree = null;
        Assert.ThrowsExactly<ArgumentNullException>(() => tree!.GetNodeOutDegree(0));
    }

    // ── FindNodesByInDegree ──────────────────────────────────────────────

    [TestMethod]
    public void FindNodesByInDegree_NoMatching_ReturnsEmpty()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode());

        var result = tree.FindNodesByInDegree(5);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void FindNodesByInDegree_EmptyTree_ReturnsEmpty()
    {
        var tree = new StubTree();
        Assert.AreEqual(0, tree.FindNodesByInDegree(0).Count);
    }

    [TestMethod]
    public void FindNodesByInDegree_ReturnsCorrectIndices()
    {
        var tree = new StubTree();
        // node0: entry (in-degree 0)
        // node1: in-degree 1
        // node2: in-degree 2
        // node3: entry (in-degree 0)
        var nodes = Enumerable.Range(0, 4).Select(_ => new StubNode()).ToArray();
        foreach (var n in nodes) tree.Nodes.Add(n);

        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[1]));
        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[2]));
        Connect(MakeSlot(nodes[1]), MakeSlot(nodes[2]));

        // in-degree 0: node0, node3
        var entries = tree.FindNodesByInDegree(0);
        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.Contains(0));
        Assert.IsTrue(entries.Contains(3));

        // in-degree 1: node1
        var ones = tree.FindNodesByInDegree(1);
        Assert.AreEqual(1, ones.Count);
        Assert.AreEqual(1, ones[0]);

        // in-degree 2: node2
        var twos = tree.FindNodesByInDegree(2);
        Assert.AreEqual(1, twos.Count);
        Assert.AreEqual(2, twos[0]);
    }

    [TestMethod]
    public void FindNodesByInDegree_NullTree_Throws()
    {
        IWorkflowTreeViewModel? tree = null;
        Assert.ThrowsExactly<ArgumentNullException>(() => tree!.FindNodesByInDegree(0));
    }

    // ── FindNodesByOutDegree ─────────────────────────────────────────────

    [TestMethod]
    public void FindNodesByOutDegree_NoMatching_ReturnsEmpty()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode());

        Assert.AreEqual(0, tree.FindNodesByOutDegree(3).Count);
    }

    [TestMethod]
    public void FindNodesByOutDegree_EmptyTree_ReturnsEmpty()
    {
        var tree = new StubTree();
        Assert.AreEqual(0, tree.FindNodesByOutDegree(0).Count);
    }

    [TestMethod]
    public void FindNodesByOutDegree_ReturnsCorrectIndices()
    {
        var tree = new StubTree();
        // node0: out-degree 2 (broadcast)
        // node1: out-degree 0 (sink)
        // node2: out-degree 1
        var nodes = Enumerable.Range(0, 3).Select(_ => new StubNode()).ToArray();
        foreach (var n in nodes) tree.Nodes.Add(n);

        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[1]));
        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[2]));
        Connect(MakeSlot(nodes[2]), MakeSlot(nodes[1]));

        var sinks = tree.FindNodesByOutDegree(0);
        Assert.AreEqual(1, sinks.Count);
        Assert.AreEqual(1, sinks[0]);

        var singles = tree.FindNodesByOutDegree(1);
        Assert.AreEqual(1, singles.Count);
        Assert.AreEqual(2, singles[0]);

        var doubles = tree.FindNodesByOutDegree(2);
        Assert.AreEqual(1, doubles.Count);
        Assert.AreEqual(0, doubles[0]);
    }

    [TestMethod]
    public void FindNodesByOutDegree_NullTree_Throws()
    {
        IWorkflowTreeViewModel? tree = null;
        Assert.ThrowsExactly<ArgumentNullException>(() => tree!.FindNodesByOutDegree(0));
    }

    // ── FindEntryNodeIndices ─────────────────────────────────────────────

    [TestMethod]
    public void FindEntryNodeIndices_EmptyTree_ReturnsEmpty()
    {
        var tree = new StubTree();
        Assert.AreEqual(0, tree.FindEntryNodeIndices().Count);
    }

    [TestMethod]
    public void FindEntryNodeIndices_SingleNodeNoConnections_ReturnsNode()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode());

        var entries = tree.FindEntryNodeIndices();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(0, entries[0]);
    }

    [TestMethod]
    public void FindEntryNodeIndices_LinearChain_ReturnsFirstNode()
    {
        var tree = new StubTree();
        var nodes = Enumerable.Range(0, 3).Select(_ => new StubNode()).ToArray();
        foreach (var n in nodes) tree.Nodes.Add(n);

        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[1]));
        Connect(MakeSlot(nodes[1]), MakeSlot(nodes[2]));

        var entries = tree.FindEntryNodeIndices();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(0, entries[0]);
    }

    [TestMethod]
    public void FindEntryNodeIndices_Diamond_ReturnsSingleEntry()
    {
        var tree = new StubTree();
        var nodes = Enumerable.Range(0, 4).Select(_ => new StubNode()).ToArray();
        foreach (var n in nodes) tree.Nodes.Add(n);

        // entry ─┬─ middle1 ─┬─ exit
        //         └─ middle2 ─┘
        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[1]));
        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[2]));
        Connect(MakeSlot(nodes[1]), MakeSlot(nodes[3]));
        Connect(MakeSlot(nodes[2]), MakeSlot(nodes[3]));

        var entries = tree.FindEntryNodeIndices();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(0, entries[0]);
    }

    // ── FindExitNodeIndices ──────────────────────────────────────────────

    [TestMethod]
    public void FindExitNodeIndices_EmptyTree_ReturnsEmpty()
    {
        var tree = new StubTree();
        Assert.AreEqual(0, tree.FindExitNodeIndices().Count);
    }

    [TestMethod]
    public void FindExitNodeIndices_SingleNodeNoConnections_ReturnsNode()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode());

        var exits = tree.FindExitNodeIndices();
        Assert.AreEqual(1, exits.Count);
        Assert.AreEqual(0, exits[0]);
    }

    [TestMethod]
    public void FindExitNodeIndices_LinearChain_ReturnsLastNode()
    {
        var tree = new StubTree();
        var nodes = Enumerable.Range(0, 3).Select(_ => new StubNode()).ToArray();
        foreach (var n in nodes) tree.Nodes.Add(n);

        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[1]));
        Connect(MakeSlot(nodes[1]), MakeSlot(nodes[2]));

        var exits = tree.FindExitNodeIndices();
        Assert.AreEqual(1, exits.Count);
        Assert.AreEqual(2, exits[0]);
    }

    [TestMethod]
    public void FindExitNodeIndices_Diamond_ReturnsSingleExit()
    {
        var tree = new StubTree();
        var nodes = Enumerable.Range(0, 4).Select(_ => new StubNode()).ToArray();
        foreach (var n in nodes) tree.Nodes.Add(n);

        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[1]));
        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[2]));
        Connect(MakeSlot(nodes[1]), MakeSlot(nodes[3]));
        Connect(MakeSlot(nodes[2]), MakeSlot(nodes[3]));

        var exits = tree.FindExitNodeIndices();
        Assert.AreEqual(1, exits.Count);
        Assert.AreEqual(3, exits[0]);
    }

    [TestMethod]
    public void FindExitNodeIndices_MultipleSinks_ReturnsAll()
    {
        var tree = new StubTree();
        var nodes = Enumerable.Range(0, 3).Select(_ => new StubNode()).ToArray();
        foreach (var n in nodes) tree.Nodes.Add(n);

        // source forks to two independent sinks
        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[1]));
        Connect(MakeSlot(nodes[0]), MakeSlot(nodes[2]));

        var exits = tree.FindExitNodeIndices();
        Assert.AreEqual(2, exits.Count);
        Assert.IsTrue(exits.Contains(1));
        Assert.IsTrue(exits.Contains(2));
    }

    [TestMethod]
    public void FindEntryAndExit_DisjointGraph_AllIsolatedNodesFound()
    {
        var tree = new StubTree();
        for (int i = 0; i < 5; i++)
            tree.Nodes.Add(new StubNode());

        // Connect only nodes 1→2, leaving others isolated
        Connect(MakeSlot(tree.Nodes[1]), MakeSlot(tree.Nodes[2]));

        // Entry: nodes with in-degree 0 → all except node2
        var entries = tree.FindEntryNodeIndices();
        Assert.AreEqual(4, entries.Count);
        Assert.IsFalse(entries.Contains(2));

        // Exit: nodes with out-degree 0 → all except node1
        var exits = tree.FindExitNodeIndices();
        Assert.AreEqual(4, exits.Count);
        Assert.IsFalse(exits.Contains(1));
    }
}
