using Demo.ViewModels.WorkflowHelpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.IO;
using System.Text.Json.Serialization;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Tree<TreeHelper>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型

    [VeloxCommand]
    private async Task Save(object? parameter, CancellationToken ct)
    {
        var json = JsonConvert.SerializeObject(this, TreeHelper.js_settings);
        await File.WriteAllTextAsync(@"E:\\Workflow.json", json, ct);
    }
}
