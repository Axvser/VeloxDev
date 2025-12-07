using Avalonia_StyleGraph.ViewModels.Workflow.Helper;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Extension;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.ViewModels.Workflow;

[WorkflowBuilder.ViewModel.Tree<GraphHelper>]
public partial class StyleGraphViewModel
{
    public StyleGraphViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型

    [VeloxCommand]
    private async Task Save(object? parameter, CancellationToken ct)
    {
        if (parameter is not string path || !File.Exists(path)) return;
        await Helper.CloseAsync();
        var json = this.Serialize();
        await File.WriteAllTextAsync(path, json, ct);
    }
}
