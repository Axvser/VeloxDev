using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow
{
    [WorkflowBuilder.Slot<SlotHelper>]
    internal partial class SlotViewModel
    {
        public SlotViewModel() => InitializeWorkflow();

        // ↓ 扩展视图模型
    }
}
