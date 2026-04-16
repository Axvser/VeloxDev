using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务发起者")]
[AgentContext(AgentLanguages.English, "A derived Node component that acts as a workflow initiator/controller. Default size: 300×260. Never use Size(0,0).")]
[WorkflowBuilder.Node<NodeHelper>]
public partial class ControllerViewModel
{
    public ControllerViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "输出口")]
    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "是否处于活跃状态")]
    [VeloxProperty] private bool isActive = false;

    [AgentContext(AgentLanguages.Chinese, "种子负载，工作流执行时的初始数据")]
    [VeloxProperty] private string seedPayload = "demo-request-chain";

    [VeloxCommand]
    private async Task OpenWorkflow(object? parameters, CancellationToken ct)
    {
        var tree = Parent as TreeViewModel;
        if (tree is not null)
        {
            if (tree.IsWorkflowRunning) return;
            tree.BeginWorkflowRun();
        }
        else
        {
            if (IsActive) return;
            IsActive = true;
        }

        try
        {
            var context = NetworkFlowContext.Create(SeedPayload);
            await this.StandardBroadcastAsync(context, ct);
        }
        catch
        {
            tree?.EndWorkflowRun();
            if (tree is null)
            {
                IsActive = false;
            }
            throw;
        }
    }

    [VeloxCommand]
    private async Task CloseWorkflow(object? parameters, CancellationToken ct)
    {
        if (Parent is null) return;
        await Parent.GetHelper().CloseAsync();
        if (Parent is TreeViewModel tree)
        {
            tree.EndWorkflowRun();
        }
        else
        {
            IsActive = false;
        }
    }
}
