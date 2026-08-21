using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

/// <summary>
/// Logic gate node: a decision router. It reduces the incoming payload (a number, a bool, or an object
/// carrying a "pass"/"result"/"ok"/"value" field) to a boolean, applies <see cref="GateOp"/>, and routes to
/// the True or False output slot. The decision is always made at runtime (dynamic routing) — the gate has no
/// compile-time-decidable state, so both branches stay alive in the compiled graph.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "逻辑运算门节点：把输入值（数值/布尔/{pass:...} 字段）规约成布尔，套 GateOp 后路由到 True/False 输出口。始终运行期路由（两条分支都在编译图里存活）。默认大小 220×160")]
[AgentContext(AgentLanguages.English, "Logic gate node: reduces the input value (number / bool / {pass:...} field) to a boolean, applies GateOp, and routes to the True/False output slot. Always routes at runtime (both branches stay alive in the compiled graph). Default size: 220×160")]
[WorkflowBuilder.Node<LogicGateHelper>(workSemaphore: 1)]
public partial class LogicGateNodeViewModel : ICompileTimeRouter, ICompileTimeAware
{
    public LogicGateNodeViewModel()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(bool));
    }

    [AgentContext(AgentLanguages.Chinese, "输入口（接收端）")]
    [AgentContext(AgentLanguages.English, "Input slot (receiver)")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口（True/False）")]
    [AgentContext(AgentLanguages.English, "Output slots (True/False)")]
    [VeloxProperty]
    [SlotSelectors(typeof(bool))]
    public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [AgentContext(AgentLanguages.English, "Display title shown in the node header.")]
    [VeloxProperty] private string title = "Logic Gate";

    [AgentContext(AgentLanguages.Chinese, "逻辑运算：Identity=直接按输入真假路由；Not=取反后再路由")]
    [AgentContext(AgentLanguages.English, "Gate operation: Identity routes by the input's truthiness; Not inverts it before routing.")]
    [VeloxProperty] private GateOp gateOp = GateOp.Identity;

    /// <summary>Options for the gate-operation dropdown.</summary>
    public GateOp[] GateOpOptions => [GateOp.Identity, GateOp.Not];

    [AgentContext(AgentLanguages.Chinese, "最近一次路由结果")]
    [AgentContext(AgentLanguages.English, "Most recent routing result.")]
    [VeloxProperty] private string lastRouted = "-";

    [AgentContext(AgentLanguages.Chinese, "无状态模式下是否显示 Run/Forward 按钮")]
    [AgentContext(AgentLanguages.English, "Whether to show the Run/Forward buttons in stateless mode.")]
    [VeloxProperty] private bool showActionButtons = false;

    public bool HasInputSlot => _inputSlot is not null;

    public SlotViewModel? TrueSlot => OutputSlots?.TrySelect(true, out var s) == true ? s : null;
    public SlotViewModel? FalseSlot => OutputSlots?.TrySelect(false, out var s) == true ? s : null;

    /// <summary>Compile-time identity injected by the compiler (Order = -1 means absolute stop).</summary>
    public ICompileContext? CompileContext { get; private set; }

    /// <summary>Whether the node is in the compile-time absolute stop state (unselected static branch / terminated).</summary>
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

    /// <summary>
    /// Route key: a null compile-time payload returns null (dynamic routing — all branches stay alive);
    /// at runtime it evaluates <see cref="IRuntimeContext.Data"/> through the gate and returns the boolean result.
    /// </summary>
    public Task<object?> ResolveRouteKey(object? payload)
    {
        if (payload is not IRuntimeContext ctx)
            return Task.FromResult<object?>(null);

        var result = LogicGateHelper.Evaluate(ctx.Data, GateOp);
        LastRouted = result ? "→ True" : "→ False";
        return Task.FromResult<object?>(result);
    }

    /// <summary>Route table: both True/False branches (a branch without downstream is registered as a terminal branch — an empty list).</summary>
    public Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable()
    {
        var dict = new Dictionary<object, List<IWorkflowNodeViewModel>>();
        AddBranch(dict, true, TrueSlot);
        AddBranch(dict, false, FalseSlot);
        return Task.FromResult<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>>(
            dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<IWorkflowNodeViewModel>)kv.Value.AsReadOnly()));
    }

    private void AddBranch(Dictionary<object, List<IWorkflowNodeViewModel>> dict, object key, SlotViewModel? slot)
    {
        if (slot is null) return;
        if (slot.Targets.Count == 0)
        {
            if (!dict.ContainsKey(key)) dict[key] = [];
            return;
        }
        foreach (var target in slot.Targets)
            if (target.Parent is IWorkflowNodeViewModel parent)
                AddTarget(dict, key, parent);
    }

    private static void AddTarget(Dictionary<object, List<IWorkflowNodeViewModel>> dict, object key,
        IWorkflowNodeViewModel target)
    {
        if (!dict.TryGetValue(key, out var list))
            dict[key] = list = [];
        if (!list.Contains(target))
            list.Add(target);
    }
}
