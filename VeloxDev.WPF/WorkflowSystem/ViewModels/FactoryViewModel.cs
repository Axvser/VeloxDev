using System.Windows;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.ContextTree]
    public partial class FactoryViewModel
    {
        [VeloxCommand]
        public Task Load(object? parameter,CancellationToken ct)
        {
            MessageBox.Show("Command Invoked");
            return Task.CompletedTask;
        }
    }
}
