using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Link<WorkflowHelper.ViewModel.Link>]
public partial class LinkViewModel
{
    public LinkViewModel() => InitializeWorkflow();

    [VeloxProperty]
    protected partial string Name { set; }

    // …… 自由扩展您的连接线视图模型
}