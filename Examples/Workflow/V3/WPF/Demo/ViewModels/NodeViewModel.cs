using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Node<WorkflowHelper.ViewModel.Node>(workSemaphore: 5)]
public partial class NodeViewModel
{
    public NodeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的节点视图模型
}