using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.ViewModels;

[WorkflowBuilder.ViewModel.Node<WorkflowHelper.ViewModel.Node>(1)]
public partial class NodeViewModel
{
    public NodeViewModel() { InitializeWorkflow(); }
}