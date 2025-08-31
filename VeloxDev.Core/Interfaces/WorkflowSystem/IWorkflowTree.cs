using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowTree : IWorkflowContext
    {
        public IWorkflowLink VirtualLink { get; set; }
        public ObservableCollection<IWorkflowNode> Nodes { get; set; }
        public ObservableCollection<IWorkflowLink> Links { get; set; }

        public void PushUndo(Action undo);
        public IWorkflowLink? FindLink(IWorkflowNode sender, IWorkflowNode processor);

        public IVeloxCommand CreateNodeCommand { get; }
        public IVeloxCommand SetPointerCommand { get; }
        public IVeloxCommand SetSenderCommand { get; }
        public IVeloxCommand SetProcessorCommand { get; }
        public IVeloxCommand ResetStateCommand { get; }
    }
}
