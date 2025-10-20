using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels;

[WorkflowBuilder.ViewModel.Slot<WorkflowHelper.ViewModel.Slot>]
public partial class SlotViewModel
{
    public SlotViewModel() { InitializeWorkflow(); }
}