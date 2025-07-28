using System.Windows;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace WpfApp2.ViewModels;

[Workflow.Context.Node]
public partial class ShowerNodeViewModel
{
    public ShowerNodeViewModel() { InitializeWorkflow(); }

    [VeloxProperty]
    private bool isWorking = false;

    async partial void OnExecute(object? parameter)
    {
        IsWorking = true;
        await Task.Delay(3000);
        BroadcastCommand.Execute(null);
        IsWorking = false;
    }
}
