using Demo.ViewModels.Workflow.Helper;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

/// <summary>
/// Timer node: acts as a data source that emits the current timestamp each run, and fans out to every
/// downstream target through a single route key ("tick") — the compiler turns that fan-out into a
/// ParallelSegment so two Python workers run against the same tick and their outputs join downstream.
/// </summary>
[WorkflowBuilder.Node<TimerHelper>(workSemaphore: 1)]
[DefaultSize(200, 140)]
public partial class TimerNodeViewModel : ICompileTimeRouter, ICompileTimeAware
{
    public TimerNodeViewModel() => InitializeWorkflow();

    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [VeloxProperty] private string title = "Timer";

    [VeloxProperty] private int intervalMilliseconds = 1000;

    [VeloxProperty] private string lastTick = "-";

    public bool HasInputSlot => _inputSlot is not null;
    public bool HasOutputSlot => _outputSlot is not null;

    /// <summary>Compile-time identity injected by the compiler (Order = -1 means absolute stop).</summary>
    public ICompileContext? CompileContext { get; private set; }

    public bool IsCompileStopped => CompileContext is { Order: -1 };

    /// <inheritdoc/>
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

    /// <summary>Single route key fan-out: the key is always "tick", so the compiler emits one branch whose sub-graph is a ParallelSegment over all downstream targets.</summary>
    /// <inheritdoc/>
    public Task<object?> ResolveRouteKey(object? payload) => Task.FromResult<object?>("tick");

    /// <summary>Route table: one branch "tick" carrying every downstream target of <see cref="OutputSlot"/> (empty list = terminal).</summary>
    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<object, IReadOnlyList<INodeComponent>>> GetRouteTable()
    {
        var targets = OutputSlot?.Targets;
        var list = new List<INodeComponent>();
        if (targets is not null)
            foreach (var t in targets)
                if (t.Parent is INodeComponent parent && !list.Contains(parent))
                    list.Add(parent);
        return Task.FromResult<IReadOnlyDictionary<object, IReadOnlyList<INodeComponent>>>(
            new Dictionary<object, IReadOnlyList<INodeComponent>> { ["tick"] = list.AsReadOnly() });
    }
}
