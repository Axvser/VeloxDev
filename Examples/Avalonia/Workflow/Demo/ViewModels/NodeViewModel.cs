using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[Workflow.Context.Node(semaphore:10)] // 允许 10 个任务并发
public partial class NodeViewModel
{
    public NodeViewModel()
    {
        // 必须执行此项
        InitializeWorkflow();
    }

    // …… 自由扩展您的节点视图模型
    
    // 例子 ：自定义一个属性来监听任务执行情况
    [VeloxProperty] private bool _isExecuting = false;

    // 例子 ：假设有一个耗时3秒的任务
    private async partial Task Work(object? parameter, CancellationToken ct)
    {
        // 任务执行
        await Task.Delay(3000);
        IsExecuting = true;
        await Task.Delay(3000);
        IsExecuting = false;
        
        // 任务结束后可以继续向下传播
        BroadcastCommand.Execute(parameter);
    }
}