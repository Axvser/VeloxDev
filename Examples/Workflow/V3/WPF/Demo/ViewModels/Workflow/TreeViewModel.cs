using System.IO;
using VeloxDev.Core.Extension;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Tree<WorkflowHelper.ViewModel.Tree>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型

    [VeloxCommand]
    private async Task Save(object? parameter, CancellationToken ct)
    {
        if (parameter is not string path) return;
        if (string.IsNullOrEmpty(path)) path = @"E:\Workflow.json";
        await Helper.CloseAsync();
        var json = this.Serialize();
        await File.WriteAllTextAsync(path, json, ct);
    }
}
