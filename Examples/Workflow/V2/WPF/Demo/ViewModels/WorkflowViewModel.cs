using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

// WPF 数据绑定不是强类型的，此处不一定需要你正确声明 输入/输出口 & 连接线 类型，直接省略也是可以的
[Workflow.Context.Tree(typeof(SlotViewModel), typeof(LinkViewModel))]
public partial class WorkflowViewModel
{
    public WorkflowViewModel()
    {
        InitializeWorkflow();
    }

    // …… 自由扩展您的工作流树视图模型
}