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
        await Task.Delay(3000); // 假设这个节点有个耗时3秒的工作
        BroadcastCommand.Execute(null);
        IsWorking = false;
    }
}
