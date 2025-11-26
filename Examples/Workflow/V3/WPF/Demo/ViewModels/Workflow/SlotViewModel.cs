using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Slot<WorkflowHelper.ViewModel.Slot>]
public partial class SlotViewModel
{
    public SlotViewModel() => InitializeWorkflow();

    // …… 自由扩展您的输入/输出口视图模型
}