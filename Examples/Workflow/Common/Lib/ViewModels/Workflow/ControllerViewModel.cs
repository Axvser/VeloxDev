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
    [AgentContext(AgentLanguages.English, "Output slot (sender). Connect this to the first downstream node's input slot to start the execution chain.")]
    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "是否处于活跃状态")]
    [AgentContext(AgentLanguages.English, "Indicates whether the workflow is currently running.")]
    [VeloxProperty] private bool isActive = false;

    [AgentContext(AgentLanguages.Chinese, "种子负载，工作流执行时的初始数据")]
    [AgentContext(AgentLanguages.English, "Initial payload string injected into the workflow context when execution starts.")]
    [VeloxProperty] private string seedPayload = "demo-request-chain";

    [AgentContext(AgentLanguages.Chinese, "启动工作流，向下游广播种子负载")]
    [AgentContext(AgentLanguages.English, "Starts the workflow: broadcasts SeedPayload to all connected downstream nodes.")]
    [VeloxCommand]
    private async Task OpenWorkflow(object? parameters, CancellationToken ct)
    {
        var tree = Parent as TreeViewModel;
        tree?.BeginWorkflowRun();

        try
        {
            var context = NetworkFlowContext.Create(SeedPayload);
            await this.StandardBroadcastAsync(context, ct);
        }
        catch
        {
            tree?.EndWorkflowRun();
            throw;
        }
    }

    [AgentContext(AgentLanguages.Chinese, "停止工作流，关闭所有正在运行的任务")]
    [AgentContext(AgentLanguages.English, "Stops the workflow and closes all in-progress work items.")]
    [VeloxCommand]
    private async Task CloseWorkflow(object? parameters, CancellationToken ct)
    {
        if (Parent is null) return;
        await Parent.GetHelper().CloseAsync();
        if (Parent is TreeViewModel tree)
        {
            tree.EndWorkflowRun();
        }
    }
}
