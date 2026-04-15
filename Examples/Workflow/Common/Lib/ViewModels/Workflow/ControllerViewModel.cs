using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务发起者")]
[WorkflowBuilder.Node<NodeHelper>]
public partial class ControllerViewModel
{
    public ControllerViewModel() => InitializeWorkflow();

    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }
    [VeloxProperty] private bool isActive = false;
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
