using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem.Templates
{
    public partial class SlotViewModelBase : IWorkflowSlotViewModel
    {
        private IWorkflowSlotViewModelHelper Helper = new WorkflowHelper.ViewModel.Slot();

        public SlotViewModelBase() { InitializeWorkflow(); }

        [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> targets = [];
        [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> sources = [];
        [VeloxProperty] private IWorkflowNodeViewModel? parent = null;
        [VeloxProperty] private SlotChannel channel = SlotChannel.Default;
        [VeloxProperty] private SlotState state = SlotState.StandBy;
        [VeloxProperty] private Anchor anchor = new();
        [VeloxProperty] private Offset offset = new();
        [VeloxProperty] private Size size = new();

        [VeloxCommand]
        protected virtual Task Press(object? parameter, CancellationToken ct)
        {
            if (parameter is not Anchor anchor) return Task.CompletedTask;
            Helper.Press(anchor);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Translate(object? parameter, CancellationToken ct)
        {
            if (parameter is not Offset offset) return Task.CompletedTask;
            Helper.Translate(offset);
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
        protected virtual Task ApplyConnection(object? parameter, CancellationToken ct)
        {
            Helper.ApplyConnection();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task ReceiveConnection(object? parameter, CancellationToken ct)
        {
            Helper.ReceiveConnection();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Delete(object? parameter, CancellationToken ct)
        {
            Helper.Delete();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual async Task Close(object? parameter, CancellationToken ct)
        {
            await Helper.CloseAsync();
        }

        public virtual IWorkflowSlotViewModelHelper GetHelper() => Helper;
        public virtual void InitializeWorkflow() => Helper.Initialize(this);
        public virtual void SetHelper(IWorkflowSlotViewModelHelper helper)
        {
            helper.Initialize(this);
            Helper = helper;
        }
    }
}
