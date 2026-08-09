using Demo.ViewModels.Workflow.Helper;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow;

[WorkflowBuilder.Tree<VirtualizeHelper>]
internal partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();
}
