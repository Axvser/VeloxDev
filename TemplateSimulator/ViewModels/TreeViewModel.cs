using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels;

[WorkflowBuilder.ViewModel.Tree<WorkflowHelper.ViewModel.Tree>(typeof(LinkViewModel), typeof(SlotViewModel))]
public partial class TreeViewModel
{
    public TreeViewModel() { InitializeWorkflow(); }
}