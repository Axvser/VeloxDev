using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    public partial class ShowerNodeViewModel : IWorkflowNode
    {
        [VeloxProperty]
        private IWorkflowTree? parent = null;
        [VeloxProperty]
        private Anchor anchor = new();
        [VeloxProperty]
        private Size size = new();
        [VeloxProperty]
        private bool isEnabled = true;
        [VeloxProperty]
        private string uID = string.Empty;
        [VeloxProperty]
        private string name = string.Empty;

        [VeloxCommand]
        public Task Delete(object? parameter, CancellationToken ct)
        {
            if (parent is IWorkflowTree tree)
            {
                tree.Nodes.Remove(this);
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Broadcast(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
