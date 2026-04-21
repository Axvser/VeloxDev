using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

public partial class TestNode : BoolSelectorNodeViewModel
{
    public TestNode()
    {
        InitializeWorkflow();
    }
}

[AgentContext(AgentLanguages.Chinese, "布尔选择器节点，将输入路由到 True 或 False 输出口。默认大小为 260*200。创建后已预置 True/False 输出口，Condition 属性控制路由方向")]
[AgentContext(AgentLanguages.English, "Bool selector node that routes input to True or False output slot based on Condition. Default size: 260×200. True/False slots are pre-populated on creation.")]
[WorkflowBuilder.Node<BoolSelectorHelper>(workSemaphore: 1)]
public partial class BoolSelectorNodeViewModel
{
    public BoolSelectorNodeViewModel()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(bool));
    }

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "布尔输出口集合（False/True），由 SlotEnumerator<bool> 管理")]
    [VeloxProperty]
    [SlotSelectors(typeof(bool))]
    public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Bool Selector";

    [AgentContext(AgentLanguages.Chinese, "路由条件，true 走 TrueSlot，false 走 FalseSlot")]
    [VeloxProperty] private bool condition = true;

    [VeloxProperty] private string lastRouted = "-";

    public bool HasInputSlot => _inputSlot is not null;

    public SlotViewModel? TrueSlot => OutputSlots?.TrySelect(true, out var s) == true ? s : null;
    public SlotViewModel? FalseSlot => OutputSlots?.TrySelect(false, out var s) == true ? s : null;
}
