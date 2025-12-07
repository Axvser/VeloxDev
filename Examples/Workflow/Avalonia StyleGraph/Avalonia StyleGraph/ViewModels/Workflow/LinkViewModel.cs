using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.ViewModels.Workflow;

[WorkflowBuilder.ViewModel.Link
    <WorkflowHelper.ViewModel.Link>]
public partial class LinkViewModel
{
    public LinkViewModel() => InitializeWorkflow();

    // …… 自由扩展您的连接线视图模型
}