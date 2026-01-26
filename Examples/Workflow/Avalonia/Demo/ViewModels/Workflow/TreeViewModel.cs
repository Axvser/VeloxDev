using Avalonia;
using Demo.ViewModels.Workflow.Helper;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Extension;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Tree<TreeHelper>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型

    [VeloxProperty] private Layout layout = new();

    [VeloxCommand]
    private async Task Save(object? parameter, CancellationToken ct)
    {
        if (parameter is not string path) return;
        await Helper.CloseAsync();
        var json = this.Serialize();
        await File.WriteAllTextAsync(path, json, ct);
    }
}
