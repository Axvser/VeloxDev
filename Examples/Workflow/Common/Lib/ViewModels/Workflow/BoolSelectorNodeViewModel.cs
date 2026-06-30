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
public partial class BoolSelectorNodeViewModel : ICompileTimeRouter
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

    [VeloxProperty] private string lastRouted = "-";

    // WorkResult（生成器 NuGet 暂未生成该属性，手动实现）
    private object? workResult;
    public object? WorkResult
    {
        get => workResult;
        set
        {
            if (Equals(workResult, value)) return;
            workResult = value;
            OnPropertyChanged(nameof(WorkResult));
        }
    }

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

    public bool HasInputSlot => _inputSlot is not null;

    public SlotViewModel? TrueSlot => OutputSlots?.TrySelect(true, out var s) == true ? s : null;
    public SlotViewModel? FalseSlot => OutputSlots?.TrySelect(false, out var s) == true ? s : null;

    public object? GetCurrentRouteKey() => Condition ? (object)true : (object)false;

    /// <summary>
    /// 编译时路由表：true → TrueSlot 的后续节点，false → FalseSlot 的后续节点
    /// </summary>
    public IReadOnlyDictionary<object, IWorkflowNodeViewModel> GetRouteTable()
    {
        var dict = new Dictionary<object, IWorkflowNodeViewModel>();
        if (TrueSlot is not null)
        {
            foreach (var target in TrueSlot.Targets)
                if (target.Parent is not null)
                    dict[true] = target.Parent;
        }
        if (FalseSlot is not null)
        {
            foreach (var target in FalseSlot.Targets)
                if (target.Parent is not null)
                    dict[false] = target.Parent;
        }
        return dict;
    }
}
