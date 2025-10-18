using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem.Templates
{
    public partial class NodeViewModelBase : IWorkflowNodeViewModel
    {
        private IWorkflowNodeViewModelHelper Helper = new WorkflowHelper.ViewModel.Node();

        public NodeViewModelBase() { InitializeWorkflow(); }

        [VeloxProperty] private IWorkflowTreeViewModel? parent = null;
        [VeloxProperty] private Anchor anchor = new();
        [VeloxProperty] private Size size = new();
        [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> slots = [];

        [VeloxCommand]
        protected virtual Task Press(object? parameter, CancellationToken ct)
        {
            if (parameter is not Anchor anchor) return Task.CompletedTask;
            Helper.Press(anchor);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Move(object? parameter, CancellationToken ct)
        {
            if (parameter is not Anchor anchor) return Task.CompletedTask;
            Helper.Move(anchor);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Scale(object? parameter, CancellationToken ct)
        {
            if (parameter is not Size scale) return Task.CompletedTask;
            Helper.Scale(scale);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Release(object? parameter, CancellationToken ct)
        {
            if (parameter is not Anchor anchor) return Task.CompletedTask;
            Helper.Release(anchor);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task CreateSlot(object? parameter, CancellationToken ct)
        {
            if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
            Helper.CreateSlot(slot);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Delete(object? parameter, CancellationToken ct)
        {
            Helper.Delete();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Work(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Broadcast(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual async Task Close(object? parameter, CancellationToken ct)
        {
            await Helper.CloseAsync();
        }

        public virtual IWorkflowNodeViewModelHelper GetHelper() => Helper;
        public virtual void InitializeWorkflow() => Helper.Initialize(this);
        public virtual void SetHelper(IWorkflowNodeViewModelHelper helper)
        {
            helper.Initialize(this);
            Helper = helper;
        }
    }
}
