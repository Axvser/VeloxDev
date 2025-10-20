using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels;

[WorkflowBuilder.ViewModel.Link<WorkflowHelper.ViewModel.Link>(typeof(SlotViewModel))]
public partial class LinkViewModel
{

}