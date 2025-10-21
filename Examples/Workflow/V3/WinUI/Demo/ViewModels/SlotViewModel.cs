using VeloxDev.Core.AOT;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
[WorkflowBuilder.ViewModel.Slot<WorkflowHelper.ViewModel.Slot>]
public partial class SlotViewModel
{
    public SlotViewModel() => InitializeWorkflow();

    // …… 自由扩展您的输入/输出口视图模型
}