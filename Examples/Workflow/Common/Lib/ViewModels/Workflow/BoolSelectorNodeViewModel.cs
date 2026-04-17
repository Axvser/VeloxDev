using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "布尔选择器节点，根据 Condition 将输入路由到 TrueSlot 或 FalseSlot。默认大小为 260*200")]
[AgentContext(AgentLanguages.English, "Bool selector node that routes input to TrueSlot or FalseSlot based on Condition. Default size: 260×200")]
[WorkflowBuilder.Node<BoolSelectorHelper>(workSemaphore: 1)]
public partial class BoolSelectorNodeViewModel
{
    public BoolSelectorNodeViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "条件为真时的输出口")]
    [VeloxProperty] public partial SlotViewModel TrueSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "条件为假时的输出口")]
    [VeloxProperty] public partial SlotViewModel FalseSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Bool Selector";

    [AgentContext(AgentLanguages.Chinese, "路由条件，true 走 TrueSlot，false 走 FalseSlot")]
    [VeloxProperty] private bool condition = true;

    [VeloxProperty] private string lastRouted = "-";

    public bool HasInputSlot => _inputSlot is not null;
}
