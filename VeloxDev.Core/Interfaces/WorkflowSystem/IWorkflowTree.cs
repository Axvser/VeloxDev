using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowTree : IWorkflowContext
    {
        public IWorkflowConnection VirtualConnection { get; set; }
        public ObservableCollection<IWorkflowNode> Children { get; set; }
        public ObservableCollection<IWorkflowSlot> Slots { get; set; }
        public ObservableCollection<IWorkflowConnection> Connections { get; set; }

        public IVeloxCommand CreateNodeCommand { get; }
        public IVeloxCommand DeleteNodeCommand { get; }
        public IVeloxCommand SetVirtualMouseCommand { get; }
        public IVeloxCommand SetVirtualSenderCommand { get; }
        public IVeloxCommand SetVirtualProcessorCommand { get; }
        public IVeloxCommand ClearVirtualCommand { get; }
    }
}
