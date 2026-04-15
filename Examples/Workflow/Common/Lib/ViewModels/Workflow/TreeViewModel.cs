using Demo.ViewModels.Workflow.Helper;
using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Tree组件之一")]
[WorkflowBuilder.Tree<AgentHelper>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型

    [AgentContext(AgentLanguages.Chinese, "画布布局上下文")]
    [VeloxProperty] private CanvasLayout layout = new();

    [VeloxProperty] private ObservableCollection<IWorkflowViewModel> _visibleItems = [];
    [VeloxProperty] private ObservableCollection<string> executionLog = [];
    [VeloxProperty] private bool isWorkflowRunning = false;

    [VeloxCommand]
    public async Task AskAsync(object? parameter, CancellationToken ct)
    {
        //await AgentContextTest();

        if (parameter is not string message ||
            Helper is not AgentHelper helper ||
            helper.Agent is null ||
            helper.Session is null)
            return;

        try
        {
            var response = await helper.Agent.RunAsync(
                message).ConfigureAwait(false);

            if (response is not null)
            {
                var text = response.Text;
                AppendExecutionLog(text ?? string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // canceled by caller - ignore
        }
        catch (Exception ex)
        {
            AppendExecutionLog($"Error: {ex.Message}");
        }
    }

    public void BeginWorkflowRun()
    {
        ResetExecutionLog();
        SetWorkflowRunning(true);
    }

    public void EndWorkflowRun()
    {
        SetWorkflowRunning(false);
    }

    public void RefreshWorkflowRunningState()
    {
        var isRunning = Nodes.OfType<NodeViewModel>().Any(node => node.IsRunning || node.RunCount > 0 || node.WaitCount > 0);
        SetWorkflowRunning(isRunning);
    }

    public void ResetExecutionLog()
    {
        ExecutionLog.Clear();

        foreach (var node in Nodes.OfType<NodeViewModel>())
        {
            node.LastExecutionOrder = 0;
            node.LastExecutionTrace = "未执行";
            node.LastStatus = "Idle";
            node.LastDuration = "-";
            node.LastError = string.Empty;
            node.IsRunning = false;
            node.RunCount = 0;
            node.WaitCount = 0;
        }

        SetWorkflowRunning(false);
    }

    public void AppendExecutionLog(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        ExecutionLog.Add(entry);
    }

    [VeloxCommand]
    private async Task Save(object? parameter)
    {
        if (parameter is not string path) return;
        await Helper.CloseAsync();
        var json = this.Serialize();
        using var writer = new StreamWriter(path, append: false);
        await writer.WriteAsync(json).ConfigureAwait(false);
    }

    [VeloxCommand]
    private Task AgentContextTest()
    {
        var context = this.AsAgentScope()
            .WithComponents(AgentLanguages.Chinese,
                typeof(NodeViewModel),
                typeof(ControllerViewModel),
                typeof(SlotViewModel),
                typeof(LinkViewModel),
                typeof(TreeViewModel))
            .ProvideAllContexts(AgentLanguages.English);

        ExecutionLog.Add(context);

        File.WriteAllText(@"E://agent.md", context);

        return Task.CompletedTask;
    }

    private void SetWorkflowRunning(bool isRunning)
    {
        if (IsWorkflowRunning != isRunning)
        {
            IsWorkflowRunning = isRunning;
        }

        if (Nodes.OfType<ControllerViewModel>().FirstOrDefault() is { } controller && controller.IsActive != isRunning)
        {
            controller.IsActive = isRunning;
        }
    }
}
