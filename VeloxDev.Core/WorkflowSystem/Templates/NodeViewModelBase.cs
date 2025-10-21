using System.Collections.ObjectModel;
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
        protected virtual Task Move(object? parameter, CancellationToken ct)
        {
            if (parameter is not Offset offset) return Task.CompletedTask;
            Helper.Move(offset);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task SaveAnchor(object? parameter, CancellationToken ct)
        {
            Helper.SaveAnchor();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task SaveSize(object? parameter, CancellationToken ct)
        {
            Helper.SaveSize();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task SetAnchor(object? parameter, CancellationToken ct)
        {
            if (parameter is not Anchor anchor) return Task.CompletedTask;
            Helper.SetAnchor(anchor);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task SetSize(object? parameter, CancellationToken ct)
        {
            if (parameter is not Size scale) return Task.CompletedTask;
            Helper.SetSize(scale);
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
        protected virtual async Task Work(object? parameter, CancellationToken ct)
        {
            await Helper.WorkAsync(parameter, ct);
        }
        [VeloxCommand]
        protected virtual async Task Broadcast(object? parameter, CancellationToken ct)
        {
            await Helper.BroadcastAsync(parameter, ct);
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
