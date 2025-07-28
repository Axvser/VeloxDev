using System.Windows;
using VeloxDev.Core.WorkflowSystem;

namespace WpfApp2.ViewModels;

[Workflow.Context.Node]
public partial class ShowerNodeViewModel
{
    public ShowerNodeViewModel() { InitializeWorkflow(); }

    partial void OnExecute(object? parameter)
    {
        MessageBox.Show(Name);
        BroadcastCommand.Execute(null);
    }
}
