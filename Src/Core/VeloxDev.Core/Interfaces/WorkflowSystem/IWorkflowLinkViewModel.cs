using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WeakTypes;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowLinkViewModel : IWorkflowViewModel
    {
        public IWorkflowSlotViewModel Sender { get; set; }
        public IWorkflowSlotViewModel Receiver { get; set; }
        public bool IsVisible { get; set; }

        public IVeloxCommand DeleteCommand { get; }  // 删除 | parameter Null

        public IWorkflowLinkViewModelHelper GetHelper();
        public void SetHelper(IWorkflowLinkViewModelHelper helper);
    }

    public interface IWorkflowLinkViewModelHelper : IWorkflowHelper
    {
        public void Install(IWorkflowLinkViewModel link);
        public void Uninstall(IWorkflowLinkViewModel link);
        public void Delete();
    }
}
