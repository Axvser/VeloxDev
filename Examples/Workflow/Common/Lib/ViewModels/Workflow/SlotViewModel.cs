using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Slot组件之一")]
[WorkflowBuilder.Slot<SlotHelper>]
public partial class SlotViewModel
{
    public SlotViewModel() => InitializeWorkflow();

    // …… 自由扩展您的输入/输出口视图模型
}