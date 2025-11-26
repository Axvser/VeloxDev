using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[Workflow.Context.Node(semaphore: 10)] // 允许 10 个任务并发
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
        try
        {
            // 任务执行
            await Task.Delay(3000, ct);
            IsExecuting = true;
            await Task.Delay(3000, ct);
            IsExecuting = false;
        }
        catch (TaskCanceledException ex)
        {
            // 任务取消时不在 Work 做任何处理
        }
    }

    partial void OnWorkCanceled(object? parameter)
    {
        Name = "任务被取消";
    }
    partial void OnWorkExecuting(object? parameter)
    {
        Name = "任务开始前";
    }
    partial void OnWorkFinished(object? parameter)
    {
        Name = "任务完成后";
        BroadcastCommand.Execute(parameter);
    }

    partial void OnFlowing(object? parameter, IWorkflowLink link)
    {
        Name = "广播开始";
    }
    partial void OnFlowFinished(object? parameter, IWorkflowLink link)
    {
        Name = "广播结束";
    }
}