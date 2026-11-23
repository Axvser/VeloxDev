using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.Slot<SlotHelper>]
public partial class SlotViewModel
{
    public SlotViewModel() => InitializeWorkflow();

    // ... extend your input/output slot view-models freely here
}