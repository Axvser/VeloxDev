using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class TreeHelper : WorkflowHelper.ViewModel.Tree
{
    private TreeViewModel? _viewModel;

    public override void Install(IWorkflowTreeViewModel tree)
    {
        base.Install(tree);
        _viewModel = tree as TreeViewModel;
    }
}
