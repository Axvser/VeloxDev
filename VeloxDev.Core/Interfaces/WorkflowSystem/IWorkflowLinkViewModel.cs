using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowLinkViewModel : IWorkflowViewModel
    {
        public IWorkflowSlotViewModel? Sender { get; set; }
        public IWorkflowSlotViewModel? Receiver { get; set; }

        public IVeloxCommand DeleteCommand { get; }  // 通知两端的Slot断开连接并删除此Link

        public IWorkflowLinkViewModelHelper GetHelper();
        public Task SetHelperAsync(IWorkflowLinkViewModelHelper helper);
    }

    public interface IWorkflowLinkViewModelHelper : IDisposable
    {
        public Task InitializeAsync(IWorkflowLinkViewModel viewModel);
        public Task CloseAsync();
        public Task DeleteAsync();
    }
}
