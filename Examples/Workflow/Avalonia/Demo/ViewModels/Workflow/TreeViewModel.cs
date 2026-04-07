using Demo.ViewModels.Workflow.Helper;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Extension;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Tree<TreeHelper>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型

    [VeloxProperty] private CanvasLayout layout = new();
    [VeloxProperty] private ObservableCollection<IWorkflowViewModel> _visibleItems = [];
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

    [VeloxCommand]
    private async Task Save(object? parameter)
    {
        if (parameter is not string path) return;
        await Helper.CloseAsync();
        var json = this.Serialize();
        await File.WriteAllTextAsync(path, json);
    }

    [VeloxCommand]
    private async Task Test(CancellationToken ct)
    {

    }
}
