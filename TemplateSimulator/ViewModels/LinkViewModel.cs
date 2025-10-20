using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels;

[WorkflowBuilder.ViewModel.Link<WorkflowHelper.ViewModel.Link>()]
public partial class LinkViewModel
{
    public LinkViewModel() { InitializeWorkflow(); }
}