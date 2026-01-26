using Demo.ViewModels.Workflow.Helper;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Node // 构造一个Node组件用于Workflow
    <NodeHelper>                // 逻辑块抽离至自定义的NodeHelper
    (workSemaphore: 5)]         // 该节点执行Work任务时,最多并发5个,超出自动排队
public partial class NodeViewModel
{
    public NodeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的节点视图模型

    [VeloxProperty] private string host = string.Empty;
    [VeloxProperty] private string port = string.Empty;
    [VeloxProperty] private string userName = string.Empty;
    [VeloxProperty] private string passWord = string.Empty;
    [VeloxProperty] private string mFA = string.Empty;
}