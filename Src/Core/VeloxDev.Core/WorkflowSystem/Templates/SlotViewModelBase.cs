using VeloxDev.Core.AOT;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem.Templates
{
    [AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
    public partial class SlotViewModelBase : IWorkflowSlotViewModel
    {
        private IWorkflowSlotViewModelHelper Helper = new WorkflowHelper.ViewModel.Slot();

        public SlotViewModelBase() { InitializeWorkflow(); }

        [VeloxProperty] private HashSet<IWorkflowSlotViewModel> targets = [];
        [VeloxProperty] private HashSet<IWorkflowSlotViewModel> sources = [];
        [VeloxProperty] private IWorkflowNodeViewModel? parent = null;
        [VeloxProperty] private SlotChannel channel = SlotChannel.OneBoth;
        [VeloxProperty] private SlotState state = SlotState.StandBy;
        [VeloxProperty] private Anchor anchor = new();
        [VeloxProperty] private Offset offset = new();
        [VeloxProperty] private Size size = new();

        [VeloxCommand]
        protected virtual Task SetOffset(object? parameter, CancellationToken ct)
        {
            if (parameter is not Offset offset) return Task.CompletedTask;
            Helper.SetOffset(offset);
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
        protected virtual Task SetChannel(object? parameter, CancellationToken ct)
        {
            if (parameter is not SlotChannel slotChannel) return Task.CompletedTask;
            Helper.SetChannel(slotChannel);
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
        public virtual void InitializeWorkflow() => Helper.Install(this);
        public virtual void SetHelper(IWorkflowSlotViewModelHelper helper)
        {
            Helper.Uninstall(this);
            helper.Install(this);
            Helper = helper;
        }
    }
}
