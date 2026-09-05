using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

// ── Stubs (mirrors WorkflowTreeExTests; file-scoped so each test file owns its own) ──

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
    public IVeloxCommand ReceiveCommand { get; } = new StubCommand();
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
public class EnsureNegativeCoverTests
{
    [TestMethod]
    public void EnsureNegativeCover_NullTree_ReturnsFalse()
    {
        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(null));
    }

    [TestMethod]
    public void EnsureNegativeCover_EmptyNodes_NoOp()
    {
        var tree = new StubTree();
        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_PositiveOnlyContent_StrictNoOp()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(140, 140, 0) });
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(30, -0, 0) });

        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_NegativeContent_GrowsToNegMinAndSyncsActualOffset()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(-140, -120, 0) });
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(140, 140, 0) });

        Assert.IsTrue(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(140d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(120d, tree.Layout.NegativeOffset.Vertical);
        // CanvasLayout.Update re-raises ActualOffset == NegativeOffset in the same tick.
        Assert.AreEqual(140d, tree.Layout.ActualOffset.Horizontal);
        Assert.AreEqual(120d, tree.Layout.ActualOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_SingleAxisNegative_GrowsOnlyThatAxis()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(-90, 140, 0) });

        Assert.IsTrue(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(90d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_ExistingCoverAlreadyEnough_NeverShrinks()
    {
        var tree = new StubTree();
        tree.Layout.NegativeOffset = new Offset(500, 500);
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(-140, -120, 0) });

        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(500d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(500d, tree.Layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_DeepZoomGrowth_IsMonotonicAndTracksNewMin()
    {
        var tree = new StubTree();
        // Scale 0.5: world −140 collapses to −280.
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(-280, -240, 0) });
        Assert.IsTrue(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(280d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(240d, tree.Layout.NegativeOffset.Vertical);

        // Scale 0.1: same world node collapses further to −1400.
        tree.Nodes[0].Anchor = new Anchor(-1400, -1200, 0);
        Assert.IsTrue(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(1400d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(1200d, tree.Layout.NegativeOffset.Vertical);

        // Zoom back out (collapsed min grows toward positive): cover stays put, never shrinks.
        tree.Nodes[0].Anchor = new Anchor(-70, -60, 0);
        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(1400d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(1200d, tree.Layout.NegativeOffset.Vertical);
    }
}
