using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[Workflow.Context.Node]
public partial class NodeViewModel
{
    public NodeViewModel()
    {
        // 必须执行此项
        InitializeWorkflow();
    }
    
    // …… 自由扩展您的节点视图模型

    async partial void OnExecute(object? parameter)
    {
        // 假设节点工作了三秒钟
        await Task.Delay(3000); 
        
        // 递归执行 输出口 -> 输入口 -> 输入口所在节点 -> 节点逻辑执行 -> 输入口 ……
        BroadcastCommand.Execute("任务来喽！");
    }
}