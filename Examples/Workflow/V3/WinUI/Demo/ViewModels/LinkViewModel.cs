using VeloxDev.Core.AOT;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
[WorkflowBuilder.ViewModel.Link<WorkflowHelper.ViewModel.Link>]
public partial class LinkViewModel
{
    public LinkViewModel() => InitializeWorkflow();

    // …… 自由扩展您的连接线视图模型
}