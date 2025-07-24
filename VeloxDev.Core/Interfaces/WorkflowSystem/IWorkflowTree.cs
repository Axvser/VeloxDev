using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowTree : IWorkflowContext
    {
        public IWorkflowLink VirtualLink { get; set; }
        public ObservableCollection<IWorkflowNode> Nodes { get; set; }
        public ObservableCollection<IWorkflowLink> Links { get; set; }

        public IVeloxCommand CreateNodeCommand { get; }
        public IVeloxCommand RemoveNodeCommand { get; }
        public IVeloxCommand CreateSlotCommand { get; }
        public IVeloxCommand RemoveSlotCommand { get; }
        public IVeloxCommand CreateLinkCommand { get; }
        public IVeloxCommand RemoveLinkCommand { get; }
        public IVeloxCommand SetVirtualMouseCommand { get; }
        public IVeloxCommand SetVirtualSenderCommand { get; }
        public IVeloxCommand SetVirtualProcessorCommand { get; }
        public IVeloxCommand ClearVirtualLinkCommand { get; }
    }
}
