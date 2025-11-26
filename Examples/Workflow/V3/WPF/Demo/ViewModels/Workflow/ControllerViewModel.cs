using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Node<WorkflowHelper.ViewModel.Node>]
public partial class ControllerViewModel
{
    public ControllerViewModel() => InitializeWorkflow();

    // 作为一个控制器节点而非任务节点

    // 当前系统是否处于运行中
    [VeloxProperty] private bool isActive = false;

    // 打开系统
    [VeloxCommand]
    private async Task OpenWorkflow(object? parameters, CancellationToken ct)
    {
        if (IsActive) return;
        IsActive = true;
        await Helper.BroadcastAsync(0, ct); // 假定信号0表示系统开始启动
    }

    // 安全地终结整个工作流
    [VeloxCommand]
    private async Task CloseWorkflow(object? parameters, CancellationToken ct)
    {
        if (Parent is null) return;
        await Parent.GetHelper().CloseAsync();
    }
}
