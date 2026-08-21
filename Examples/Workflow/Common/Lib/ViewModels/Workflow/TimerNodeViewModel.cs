using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

/// <summary>
/// Timer node: acts as a data source that emits the current timestamp each run, and fans out to every
/// downstream target through a single route key ("tick") — the compiler turns that fan-out into a
/// ParallelEntry so two Python workers run against the same tick and their outputs join downstream.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "定时器节点：作为数据源，每次执行产出当前时间戳，并通过单一路由键 tick 扇出到所有下游（供并行计算与汇合演示）。默认大小 200×140")]
[AgentContext(AgentLanguages.English, "Timer node: acts as a data source that emits the current timestamp each run and fans out to all downstream targets via the single route key 'tick' (for parallel computation and join demos). Default size: 200×140")]
[WorkflowBuilder.Node<TimerHelper>(workSemaphore: 1)]
public partial class TimerNodeViewModel : ICompileTimeRouter, ICompileTimeAware
{
    public TimerNodeViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "输入口（接收端），接上游触发节点")]
    [AgentContext(AgentLanguages.English, "Input slot (receiver). Connect an upstream trigger here.")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口（发送端），图上设为 MultipleTargets 以扇出到多个下游")]
    [AgentContext(AgentLanguages.English, "Output slot (sender). Set it to MultipleTargets on the graph to fan out to several downstream nodes.")]
    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [AgentContext(AgentLanguages.English, "Display title shown in the node header.")]
    [VeloxProperty] private string title = "Timer";

    [AgentContext(AgentLanguages.Chinese, "定时器间隔（毫秒），仅作展示，不影响执行")]
    [AgentContext(AgentLanguages.English, "Timer interval in milliseconds (display only; does not affect execution).")]
    [VeloxProperty] private int intervalMilliseconds = 1000;

    [AgentContext(AgentLanguages.Chinese, "最近一次 tick 的时间戳")]
    [AgentContext(AgentLanguages.English, "Timestamp of the most recent tick.")]
    [VeloxProperty] private string lastTick = "-";

    [AgentContext(AgentLanguages.Chinese, "无状态模式下是否显示 Run/Forward 按钮")]
    [AgentContext(AgentLanguages.English, "Whether to show the Run/Forward buttons in stateless mode.")]
    [VeloxProperty] private bool showActionButtons = false;

    public bool HasInputSlot => _inputSlot is not null;
    public bool HasOutputSlot => _outputSlot is not null;

    /// <summary>Compile-time identity injected by the compiler (Order = -1 means absolute stop).</summary>
    public ICompileContext? CompileContext { get; private set; }

    public bool IsCompileStopped => CompileContext is { Order: -1 };

    public void AttachCompileTimeContext(ICompileContext context)
    {
        CompileContext = context;
        LastExecutionOrder = context.Order >= 0 ? context.Order + 1 : 0;
        OnPropertyChanged(nameof(CompileContext));
        OnPropertyChanged(nameof(IsCompileStopped));
        OnPropertyChanged(nameof(HasExecutionOrder));
        OnPropertyChanged(nameof(ExecutionOrderText));
    }

    // Execution sequence number (hand-written; the generator does not cover this yet)
    private int lastExecutionOrder;
    public int LastExecutionOrder
    {
        get => lastExecutionOrder;
        set
        {
            if (lastExecutionOrder == value) return;
            lastExecutionOrder = value;
            OnPropertyChanged(nameof(LastExecutionOrder));
            OnPropertyChanged(nameof(HasExecutionOrder));
            OnPropertyChanged(nameof(ExecutionOrderText));
        }
    }
    public bool HasExecutionOrder => LastExecutionOrder > 0 || IsCompileStopped;
    public string ExecutionOrderText => IsCompileStopped ? "⊘" : LastExecutionOrder > 0 ? $"#{LastExecutionOrder}" : "-";

    /// <summary>Single route key fan-out: the key is always "tick", so the compiler emits one branch whose sub-graph is a ParallelEntry over all downstream targets.</summary>
    public Task<object?> ResolveRouteKey(object? payload) => Task.FromResult<object?>("tick");

    /// <summary>Route table: one branch "tick" carrying every downstream target of <see cref="OutputSlot"/> (empty list = terminal).</summary>
    public Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable()
    {
        var targets = OutputSlot?.Targets;
        var list = new List<IWorkflowNodeViewModel>();
        if (targets is not null)
            foreach (var t in targets)
                if (t.Parent is IWorkflowNodeViewModel parent && !list.Contains(parent))
                    list.Add(parent);
        return Task.FromResult<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>>(
            new Dictionary<object, IReadOnlyList<IWorkflowNodeViewModel>> { ["tick"] = list.AsReadOnly() });
    }
}
