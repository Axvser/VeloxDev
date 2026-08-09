using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

namespace Demo.ViewModels;

public partial class TestNode : BoolSelectorNodeViewModel
{
    public TestNode()
    {
        InitializeWorkflow();
    }
}

[AgentContext(AgentLanguages.Chinese, "布尔选择器节点，将输入路由到 True 或 False 输出口。默认大小为 260*200")]
[AgentContext(AgentLanguages.English, "Bool selector node that routes input to True or False output slot based on Condition. Default size: 260×200")]
[WorkflowBuilder.Node<BoolSelectorHelper>(workSemaphore: 1)]
public partial class BoolSelectorNodeViewModel : ICompileTimeRouter, ICompileTimeNotifier
{
    public BoolSelectorNodeViewModel()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(bool));
    }

    /// <summary>
    /// 编译期回调：编译瞬间获知自己的编译身份。
    /// 正常编译（选中分支）写入顺序（+1 对齐运行时 1-based）；被略过时保持 0。
    /// </summary>
    public void OnCompiled(CompiledItem item)
    {
        IsCompileSkipped = item.IsSkipped;
        LastExecutionOrder = item.IsSkipped ? 0 : item.Order + 1;
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

    [VeloxProperty] private string lastRouted = "-";

    // 执行序列号（手动实现，生成器暂未覆盖）
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
    public bool HasExecutionOrder => LastExecutionOrder > 0;
    public string ExecutionOrderText => LastExecutionOrder > 0 ? $"#{LastExecutionOrder}" : "-";

    /// <summary>本次编译中该节点是否因属于未选中条件分支而被略过（编译期判定）。</summary>
    public bool IsCompileSkipped { get; private set; }

    public bool HasInputSlot => _inputSlot is not null;

    public SlotViewModel? TrueSlot => OutputSlots?.TrySelect(true, out var s) == true ? s : null;
    public SlotViewModel? FalseSlot => OutputSlots?.TrySelect(false, out var s) == true ? s : null;

    public object? GetCurrentRouteKey() => Condition ? (object)true : (object)false;

    /// <summary>
    /// 编译时路由表：true → TrueSlot 的后续节点列表，false → FalseSlot 的后续节点列表。
    /// 单个分支可扇出到多个目标（如 True→A 且 True→B），必须保留全部目标，
    /// 旧的 1:1 赋值会让后写的目标覆盖先写的，导致丢失分支路径。
    /// </summary>
    public IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>> GetRouteTable()
    {
        var dict = new Dictionary<object, List<IWorkflowNodeViewModel>>();
        if (TrueSlot is not null)
            foreach (var target in TrueSlot.Targets)
                if (target.Parent is not null)
                    AddTarget(dict, true, target.Parent);
        if (FalseSlot is not null)
            foreach (var target in FalseSlot.Targets)
                if (target.Parent is not null)
                    AddTarget(dict, false, target.Parent);
        return dict.ToDictionary(kv => kv.Key,
            kv => (IReadOnlyList<IWorkflowNodeViewModel>)kv.Value.AsReadOnly());
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
