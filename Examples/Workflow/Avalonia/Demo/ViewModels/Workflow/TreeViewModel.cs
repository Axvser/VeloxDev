using CommunityToolkit.Mvvm.ComponentModel;
using Demo.ViewModels.Workflow.Helper;
using System.Collections.ObjectModel;
using System.IO;
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

    [VeloxCommand]
    private void PlusScale()
    {
        Layout.OriginScale += new Scale(x: 0.1, y: 0.1);
    }

    [VeloxCommand]
    private void MinusScale(object? param)
    {
        Layout.OriginScale -= new Scale(x: 0.1, y: 0.1);
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
