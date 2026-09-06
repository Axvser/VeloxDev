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

/// <summary>一次 ReceiveAsync 驱动调用记录:驱动的节点、运行期 pass、收到的输入数据、阶段。</summary>
internal readonly record struct ProbeCall(ProbeNode Node, int Attempt, object? Data, bool Compiled);

/// <summary>空 IVeloxCommand:可计数、可选 Execute 钩子(模拟 ReceiveCommand 投递)。</summary>
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

/// <summary>最小 IWorkflowSlotViewModel:Targets/Sources/Parent 可直接接线的数据载体。</summary>
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
/// 引擎/编译器驱动用的节点助手:把每次 ReceiveAsync 记入节点 Calls,再交给节点注入的 Handler。
/// 默认 AccessAsync 放行;可通过节点的 AccessGate 决定单条边是否有效(编译期即被剪)。
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
/// 探针节点:IWorkflowNodeViewModel + 编译身份(ICompileTimeAware)+ 运行期注入(IRuntimeAware)。
/// 默认 Handler 为空(ReceiveAsync 返回 null);测试经 Handler/AccessGate 注入每节点行为。
/// </summary>
internal class ProbeNode : IWorkflowNodeViewModel, ICompileTimeAware, IRuntimeAware
{
    private readonly ProbeHelper _helper;

    /// <summary>驱动记录(仅运行期 ReceiveAsync,编译期 AccessAsync 不记)。</summary>
    public List<ProbeCall> Calls { get; } = [];

    /// <summary>节点业务:输入上下文 → 返回值(写入 context.Data 供下游)。</summary>
    public Func<ITaskContext, CancellationToken, object?>? Handler { get; set; }

    /// <summary>边校验门;返回 false = 该边按未连接剪除。</summary>
    public Func<IAccessContext, bool>? AccessGate { get; set; }

    /// <summary>ReceiveCommand 收到的投递(边级广播 / 手动 Run 使用);链级引擎不应触发。</summary>
    public List<ITaskContext> ReceivedDeliveries { get; } = [];

    /// <summary>运行期引擎注入过的会话(IRuntimeAware.AttachRuntimeContext)。</summary>
    public List<IRuntimeContext> AttachedContexts { get; } = [];

    public IWorkflowNodeViewModelHelper HelperInstance => _helper;

    public string Name { get; }

    public IWorkflowTreeViewModel? Parent { get; set; }
    public Anchor Anchor { get; set; } = new();
    public Size Size { get; set; } = new();
    public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; } = [];

    /// <summary>入槽(接收端)。</summary>
    public TestSlot Input { get; }

    /// <summary>出槽(发送端)。</summary>
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
/// 路由器节点:ICompileTimeRouter。Static 模式编译期只暴露当前选中分支;Dynamic 暴露全部分支,
/// ResolveRouteKey(null) 返回 null(编译期不可判),运行期再按 Selection 决定。
/// </summary>
internal sealed class RouterNode : ProbeNode, ICompileTimeRouter
{
    public RouterCompileMode CompileMode { get; set; } = RouterCompileMode.Dynamic;

    /// <summary>当前选中的路由键(Static 下编译期即锁定;Dynamic 下运行期生效)。</summary>
    public object? Selection { get; set; }

    /// <summary>全部分支表:键 → 下游节点列表(空列表 = terminal)。</summary>
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
/// 可重定向节点:IWorkflowNodeViewModel + IRedirectable。节点行为触发 Error/Warn 后,
/// 引擎调用 ResolveRedirectAsync —— 返回前驱 Order 则整图按目标重跑,返回 null 则继续。
/// </summary>
internal sealed class RedirectableNode : ProbeNode, IRedirectable
{
    /// <summary>重定向决策:输入运行期上下文,返回目标 Order(null = 继续)。</summary>
    public Func<IRuntimeContext, int?>? Resolve { get; set; }

    public RedirectableNode(string? name = null) : base(name) { }

    public Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct)
        => Task.FromResult(Resolve?.Invoke(context));
}
