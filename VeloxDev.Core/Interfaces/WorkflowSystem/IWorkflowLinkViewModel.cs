using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowLinkViewModel : IWorkflowViewModel
    {
        public IWorkflowSlotViewModel? Sender { get; set; }
        public IWorkflowSlotViewModel? Receiver { get; set; }

        public IVeloxCommand DeleteCommand { get; }  // 删除 | parameter Null

        public IWorkflowLinkViewModelHelper GetHelper();
        public void SetHelper(IWorkflowLinkViewModelHelper helper);
    }

    public interface IWorkflowLinkViewModelHelper : IWorkflowHelper
    {
        public void Initialize(IWorkflowLinkViewModel link); // 初始化不允许异步
        public void Delete(); // 工作流元素变更不允许异步
    }
}
