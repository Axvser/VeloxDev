using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

// Avalonia 数据绑定具有强类型特点，此处需要正确指定您想使用的 输入/输出口 & 连接线
[Workflow.Context.Tree(typeof(SlotViewModel), typeof(LinkViewModel))]
public partial class WorkflowViewModel
{
    public WorkflowViewModel()
    {
        // 必须执行此项
        InitializeWorkflow();
    }
    
    // …… 自由扩展您的工作流树视图模型
    
    
}