using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Extension;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Tree<WorkflowHelper.ViewModel.Tree>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型

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

    [VeloxCommand]
    private async Task Save(object? parameter, CancellationToken ct)
    {
        if (parameter is not string path) return;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await Helper.CloseAsync();
        var json = this.Serialize();
        await File.WriteAllTextAsync(path, json, ct);
    }
}
