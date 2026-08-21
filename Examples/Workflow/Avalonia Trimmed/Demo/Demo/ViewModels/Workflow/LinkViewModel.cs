using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow
{
    [WorkflowBuilder.Link<LinkHelper>]
    internal partial class LinkViewModel
    {
        public LinkViewModel() => InitializeWorkflow();

        // ↓ Extend the view model
    }
}
