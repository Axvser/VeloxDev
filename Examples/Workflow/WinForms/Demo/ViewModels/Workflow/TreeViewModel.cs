using System.Collections.ObjectModel;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Tree<WorkflowHelper.ViewModel.Tree>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();

    [VeloxProperty] private ObservableCollection<string> executionLog = [];

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
    }

    public void AppendExecutionLog(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        ExecutionLog.Add(entry);
    }
}
