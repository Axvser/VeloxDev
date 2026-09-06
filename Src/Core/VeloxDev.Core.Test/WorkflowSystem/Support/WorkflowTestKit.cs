using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

// ─────────────────────────────────────────────────────────────────────────────
// 共享测试桩(曾以 file-scoped 形式复制在 WorkflowTreeExTests / EnsureNegativeCoverTests /
// DeepZoomReachabilitySimTests 三个文件里)。SlotEnumeratorTests 的桩带特化成员
// (TrackingCreateSlotCommand/CreatedSlots),保留其文件内局部副本。
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class StubCommand : IVeloxCommand
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

internal sealed class StubSlot : IWorkflowSlotViewModel
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

internal sealed class StubNode : IWorkflowNodeViewModel
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

internal sealed class StubTree : IWorkflowTreeViewModel
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
