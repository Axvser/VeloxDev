using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowTree : IWorkflowContext
    {
        public IWorkflowLink VirtualLink { get; set; }
        public ObservableCollection<IWorkflowNode> Nodes { get; set; }
        public ObservableCollection<IWorkflowSlot> Slots { get; set; }
        public ObservableCollection<IWorkflowLink> Links { get; set; }

        public IVeloxCommand CreateNodeCommand { get; }
        public IVeloxCommand DeleteNodeCommand { get; }
        public IVeloxCommand SetVirtualMouseCommand { get; }
        public IVeloxCommand SetVirtualSenderCommand { get; }
        public IVeloxCommand SetVirtualProcessorCommand { get; }
        public IVeloxCommand ClearVirtualLinkCommand { get; }
    }
}
