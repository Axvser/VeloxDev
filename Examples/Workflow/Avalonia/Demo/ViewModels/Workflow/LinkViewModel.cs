using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Link<WorkflowHelper.ViewModel.Link>]
public partial class LinkViewModel
{
    public LinkViewModel() => InitializeWorkflow();

    // …… 自由扩展您的连接线视图模型
}