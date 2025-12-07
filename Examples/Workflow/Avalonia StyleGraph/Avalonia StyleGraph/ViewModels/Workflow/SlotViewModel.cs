using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.ViewModels.Workflow;

[WorkflowBuilder.ViewModel.Slot
    <WorkflowHelper.ViewModel.Slot>]
public partial class SlotViewModel
{
    public SlotViewModel() => InitializeWorkflow();

    // …… 自由扩展您的连接器视图模型
}