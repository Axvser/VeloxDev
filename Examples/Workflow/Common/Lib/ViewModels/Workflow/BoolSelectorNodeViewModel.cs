using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "布尔选择器节点，将输入路由到 True 或 False 输出口。默认大小为 260*200")]
[AgentContext(AgentLanguages.English, "Bool selector node that routes input to True or False output slot based on Condition. Default size: 260×200")]
[WorkflowBuilder.Node<BoolSelectorHelper>(workSemaphore: 1)]
public partial class BoolSelectorNodeViewModel : ICompileTimeRouter, ICompileTimeAware
{
    public BoolSelectorNodeViewModel()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(bool));
    }

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [AgentContext(AgentLanguages.English, "Input slot (receiver). Connect an upstream output slot here to trigger routing.")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口（False/True）")]
    [AgentContext(AgentLanguages.English, "Output slot (False/True)")]
    [VeloxProperty]
    [SlotSelectors(typeof(bool))]
    public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [AgentContext(AgentLanguages.English, "Display title shown in the node header.")]
    [VeloxProperty] private string title = "Bool Selector";

    [AgentContext(AgentLanguages.Chinese, "路由条件，true 走 TrueSlot，false 走 FalseSlot")]
    [AgentContext(AgentLanguages.English, "Routing condition. true routes to TrueSlot; false routes to FalseSlot.")]
    [VeloxProperty] private bool condition = true;

    [AgentContext(AgentLanguages.Chinese, "是否自动广播给下游节点")]
    [AgentContext(AgentLanguages.English, "When true, the node automatically forwards the result to all connected downstream nodes after execution.")]
    [VeloxProperty] private bool autoBroadcast = true;

    [VeloxProperty] private string lastRouted = "-";

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

    /// <summary>Compile mode: Static returns only the currently selected branch; Dynamic returns all True/False branches.</summary>
    [AgentContext(AgentLanguages.Chinese, "编译模式：Static 编译期锁定当前路由条件（未选中分支被剪除，其下游节点 Order = -1 绝对停止）；Dynamic 运行期按数据负载重新路由（True/False 分支全存活）。通过 PatchNodeProperties 设置，如 {\"CompileMode\":\"Static\"}。")]
    [AgentContext(AgentLanguages.English, "Compile mode: Static locks the current routing condition at compile time (the unselected branch is pruned; its downstream nodes get Order = -1 / absolute stop); Dynamic re-routes at runtime from the data payload (both True/False branches stay alive). Set via PatchNodeProperties, e.g. {\"CompileMode\":\"Static\"}.")]
    [VeloxProperty] private RouterCompileMode _compileMode = RouterCompileMode.Dynamic;

    /// <summary>Options for the compile-mode dropdown.</summary>
    public RouterCompileMode[] CompileModeOptions => [RouterCompileMode.Static, RouterCompileMode.Dynamic];

    /// <summary>
    /// Unified route-key entry point: Static → Condition (decidable at compile time); Dynamic returns null for a
    /// null compile-time payload (IsDynamic) and reads the shared "selector.bool" field at runtime, else falls back to Condition.
    /// </summary>
    public Task<object?> ResolveRouteKey(object? payload)
    {
        if (CompileMode == RouterCompileMode.Dynamic && payload is null)
            return Task.FromResult<object?>(null);

        if (payload is IRuntimeContext ctx && ctx.TryGet("selector.bool", out var v) && v is string s)
            return Task.FromResult<object?>(bool.TryParse(s, out var b) ? b : Condition);

        return Task.FromResult<object?>(Condition);
    }

    /// <summary>Compile-time route table (changes with mode): Static contains only the currently selected branch; Dynamic contains True/False (preserving 1:N fan-out). Branches with no downstream are registered as terminal branches (empty lists).</summary>
    public Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable()
    {
        var dict = new Dictionary<object, List<IWorkflowNodeViewModel>>();
        if (CompileMode == RouterCompileMode.Static)
        {
            AddBranch(dict, Condition, Condition ? TrueSlot : FalseSlot);
        }
        else
        {
            AddBranch(dict, true, TrueSlot);
            AddBranch(dict, false, FalseSlot);
        }
        return Task.FromResult<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>>(
            dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<IWorkflowNodeViewModel>)kv.Value.AsReadOnly()));
    }

    /// <summary>Registers a branch: with downstream → active branch; without downstream → terminal branch (empty list, ends the run when selected).</summary>
    private void AddBranch(Dictionary<object, List<IWorkflowNodeViewModel>> dict, object key, SlotViewModel? slot)
    {
        if (slot is null) return;
        if (slot.Targets.Count == 0)
        {
            if (!dict.ContainsKey(key)) dict[key] = [];
            return;
        }
        foreach (var target in slot.Targets)
            if (target.Parent is not null)
                AddTarget(dict, key, target.Parent);
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
