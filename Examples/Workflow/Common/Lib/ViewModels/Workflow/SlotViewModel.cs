using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Slot组件之一")]
[AgentContext(AgentLanguages.English, "A derived Slot component. Used as the sender (output) or receiver (input) endpoint of a connection.")]
[WorkflowBuilder.Slot<SlotHelper>]
public partial class SlotViewModel
{
    public SlotViewModel() => InitializeWorkflow();

    // ... extend your input/output slot view-models freely here
}