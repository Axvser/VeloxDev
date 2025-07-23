using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.Context]
    public partial class ShowerNodeViewModel
    {
        [VeloxProperty]
        private bool isEnabled = true;
        [VeloxProperty]
        private Anchor anchor = Anchor.Default;
        [VeloxProperty]
        private IWorkflowTree? tree = null;
        [VeloxProperty]
        private ObservableCollection<IWorkflowNode> targets = [];
        [VeloxProperty]
        private ObservableCollection<IWorkflowSlot> slots = [];

        [VeloxCommand]
        public Task Move(object? parameter, CancellationToken ct)
        {
            if (parameter is Anchor anchor)
            {
                Anchor = anchor;
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        public Task Delete(object? parameter, CancellationToken ct)
        {
            Tree?.Children.Remove(this);
            return Task.CompletedTask;
        }
    }
}
