using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

// ─────────────────────────────────────────────────────────────────────────────
// 探针基建:CompilerEx(编译器 → 运行时引擎)契约测试用。节点不挂树、不走 undo/Link,
// 只实现 Compiler/Engine 真正消费的东西 —— 槽连接拓扑 + Helper 的
// AccessAsync/ReceiveAsync + ICompileTimeAware/IRuntimeAware/ICompileTimeRouter/IRedirectable 钩子。
// 每个节点记录自己每次被驱动的调用(ProbeCall),供断言"驱动顺序/每 pass 数据/重定向跳段"。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Record of one ReceiveAsync drive: the driven node, the runtime pass attempt, the input data received, and the phase.</summary>
internal readonly record struct ProbeCall(ProbeNode Node, int Attempt, object? Data, bool Compiled);

/// <summary>No-op IVeloxCommand: countable, with an optional Execute hook (models ReceiveCommand dispatch).</summary>
internal sealed class TestCommand : IVeloxCommand
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

    private readonly Action<object?>? _onExecute;
    public int ExecuteCount { get; private set; }

    public TestCommand(Action<object?>? onExecute = null) => _onExecute = onExecute;

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)
    {
        ExecuteCount++;
        _onExecute?.Invoke(parameter);
    }
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

/// <summary>Minimal IWorkflowSlotViewModel: a data carrier whose Targets/Sources/Parent can be wired directly.</summary>
internal sealed class TestSlot : IWorkflowSlotViewModel
{
    public ObservableCollection<IWorkflowSlotViewModel> Targets { get; set; } = [];
    public ObservableCollection<IWorkflowSlotViewModel> Sources { get; set; } = [];
    public IWorkflowNodeViewModel? Parent { get; set; }
    public SlotChannel Channel { get; set; } = SlotChannel.OneBoth;
    public SlotState State { get; set; } = SlotState.StandBy;
    public Anchor Anchor { get; set; } = new(double.NaN, double.NaN, 0);

    public IVeloxCommand SetChannelCommand { get; } = new TestCommand();
    public IVeloxCommand SendConnectionCommand { get; } = new TestCommand();
    public IVeloxCommand ReceiveConnectionCommand { get; } = new TestCommand();
    public IVeloxCommand DeleteCommand { get; } = new TestCommand();
    public IVeloxCommand CloseCommand { get; } = new TestCommand();

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string p) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(p));
    public void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public IWorkflowSlotViewModelHelper GetHelper() => throw new NotSupportedException();
    public void SetHelper(IWorkflowSlotViewModelHelper helper) { }
}

/// <summary>
/// Node helper used by the engine/compiler: logs each ReceiveAsync into the node's Calls, then hands over to the
/// injected Handler. AccessAsync passes by default; the node's AccessGate can reject individual edges (pruned at
/// compile time).
/// </summary>
internal sealed class ProbeHelper : NodeHelper<ProbeNode>
{
    public override Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
    {
        var owner = Component;
        if (owner is null) return Task.FromResult<object?>(null);

        var call = new ProbeCall(
            owner,
            Attempt: context is IRuntimeContext rc ? rc.Attempt : 0,
            Data: context.Data,
            Compiled: context is IRuntimeContext);
        owner.Calls.Add(call);

        // 节点的业务逻辑;抛出异常由引擎按重定向语义接管(DriveAsync 先记 Error 再抛)。
        object? result = owner.Handler?.Invoke(context, ct);
        return Task.FromResult(result);
    }

    public override Task<bool> AccessAsync(IAccessContext context, CancellationToken ct)
    {
        var owner = Component;
        return Task.FromResult(owner?.AccessGate?.Invoke(context) ?? true);
    }
}

/// <summary>
/// Probe node: IWorkflowNodeViewModel + compile identity (ICompileTimeAware) + runtime injection (IRuntimeAware).
/// Handler defaults to null (ReceiveAsync returns null); tests inject per-node behavior through Handler/AccessGate.
/// </summary>
internal class ProbeNode : IWorkflowNodeViewModel, ICompileTimeAware, IRuntimeAware
{
    private readonly ProbeHelper _helper;

    /// <summary>Drive records (runtime ReceiveAsync only; compile-time AccessAsync is not recorded).</summary>
    public List<ProbeCall> Calls { get; } = [];

    /// <summary>Node business logic: input context → return value (written to context.Data for downstream).</summary>
    public Func<ITaskContext, CancellationToken, object?>? Handler { get; set; }

    /// <summary>Edge validation gate; returning false treats the edge as unconnected.</summary>
    public Func<IAccessContext, bool>? AccessGate { get; set; }

    /// <summary>Deliveries received via ReceiveCommand (edge broadcast / manual Run); the chain-level engine must not trigger these.</summary>
    public List<ITaskContext> ReceivedDeliveries { get; } = [];

    /// <summary>Sessions injected by the runtime engine (IRuntimeAware.AttachRuntimeContext).</summary>
    public List<IRuntimeContext> AttachedContexts { get; } = [];

    public IWorkflowNodeViewModelHelper HelperInstance => _helper;

    public string Name { get; }

    public IWorkflowTreeViewModel? Parent { get; set; }
    public Anchor Anchor { get; set; } = new();
    public Size Size { get; set; } = new();
    public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; } = [];

    /// <summary>Input slot (receiver side).</summary>
    public TestSlot Input { get; }

    /// <summary>Output slot (sender side).</summary>
    public TestSlot Output { get; }

    public IVeloxCommand MoveCommand { get; } = new TestCommand();
    public IVeloxCommand SetAnchorCommand { get; } = new TestCommand();
    public IVeloxCommand SetSizeCommand { get; } = new TestCommand();
    public IVeloxCommand CreateSlotCommand { get; } = new TestCommand();
    public IVeloxCommand DeleteCommand { get; } = new TestCommand();
    public IVeloxCommand ReceiveCommand { get; }
    public IVeloxCommand BroadcastCommand { get; } = new TestCommand();
    public IVeloxCommand ReverseBroadcastCommand { get; } = new TestCommand();
    public IVeloxCommand CloseCommand { get; } = new TestCommand();

    public ProbeNode(string? name = null)
    {
        Name = name ?? $"n{NextId()}";
        _helper = new ProbeHelper();

        Input = new TestSlot { Parent = this };
        Output = new TestSlot { Parent = this };
        Slots.Add(Input);
        Slots.Add(Output);

        ReceiveCommand = new TestCommand(p =>
        {
            if (p is ITaskContext ctx) ReceivedDeliveries.Add(ctx);
        });

        _helper.Install(this);
    }

    // ── ICompileTimeAware ──
    public ICompileContext? CompileContext { get; private set; }
    public void AttachCompileTimeContext(ICompileContext context) => CompileContext = context;

    // ── IRuntimeAware ──
    public void AttachRuntimeContext(IRuntimeContext context) => AttachedContexts.Add(context);

    // ── IWorkflowViewModel 基础设施 ──
    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string p) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(p));
    public void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public IWorkflowNodeViewModelHelper GetHelper() => _helper;
    public void SetHelper(IWorkflowNodeViewModelHelper helper) { }

    public override string ToString() => Name;

    private static int _id;
    private static int NextId() => Interlocked.Increment(ref _id);
}

/// <summary>
/// Router node: ICompileTimeRouter. Static mode exposes only the currently selected branch at compile time;
/// Dynamic exposes all branches, ResolveRouteKey(null) returns null (undecidable at compile time) and the key is
/// decided at runtime by Selection.
/// </summary>
internal sealed class RouterNode : ProbeNode, ICompileTimeRouter
{
    public RouterCompileMode CompileMode { get; set; } = RouterCompileMode.Dynamic;

    /// <summary>Currently selected route key (locked at compile time under Static; effective at runtime under Dynamic).</summary>
    public object? Selection { get; set; }

    /// <summary>Full branch table: key → downstream node list (empty list = terminal).</summary>
    public Dictionary<object, IReadOnlyList<ProbeNode>> RouteTable { get; } = new();

    public RouterNode(string? name = null) : base(name) { }

    public Task<object?> ResolveRouteKey(object? payload)
    {
        // Dynamic + 编译期 payload(null) → 不可编译判定;Static 恒返回当前选中。
        if (CompileMode == RouterCompileMode.Dynamic && payload is null)
            return Task.FromResult<object?>(null);
        return Task.FromResult(Selection);
    }

    public Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable()
    {
        IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>> result;
        if (CompileMode == RouterCompileMode.Static && Selection is not null && RouteTable.ContainsKey(Selection))
        {
            // Static:编译产物只有当前选中分支一条路。
            result = new Dictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>
            {
                [Selection] = RouteTable[Selection].Cast<IWorkflowNodeViewModel>().ToList(),
            };
        }
        else
        {
            // Dynamic:全部分支存活。
            result = RouteTable.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<IWorkflowNodeViewModel>)kv.Value.Cast<IWorkflowNodeViewModel>().ToList());
        }
        return Task.FromResult(result);
    }
}

/// <summary>
/// Redirectable node: IWorkflowNodeViewModel + IRedirectable. After the node triggers Error/Warn, the engine calls
/// ResolveRedirectAsync — returning a predecessor Order re-runs the whole graph toward that state, returning null
/// continues.
/// </summary>
internal sealed class RedirectableNode : ProbeNode, IRedirectable
{
    /// <summary>Redirect decision: given the runtime context, returns the target Order (null = continue).</summary>
    public Func<IRuntimeContext, int?>? Resolve { get; set; }

    public RedirectableNode(string? name = null) : base(name) { }

    public Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct)
        => Task.FromResult(Resolve?.Invoke(context));
}
