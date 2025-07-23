using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed partial class Link : IWorkflowLink
    {
        [VeloxProperty]
        private IWorkflowSlot? sender = null;
        [VeloxProperty]
        private IWorkflowSlot? processor = null;
        [VeloxProperty]
        public bool isEnabled = false;
        [VeloxProperty]
        public string uID = string.Empty;
        [VeloxProperty]
        public string name = string.Empty;

        partial void OnSenderChanged(IWorkflowSlot oldValue, IWorkflowSlot newValue)
        {
            IsEnabled = newValue != null && Processor != null;
        }
        partial void OnProcessorChanged(IWorkflowSlot oldValue, IWorkflowSlot newValue)
        {
            IsEnabled = Sender != null && newValue != null;
        }
    }
}
