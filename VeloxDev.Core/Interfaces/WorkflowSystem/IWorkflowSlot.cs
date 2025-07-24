using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    [Flags]
    public enum SlotCapacity : int
    {
        None = 0,
        Processor = 1,
        Sender = 2,
        Universal = Processor | Sender
    }

    public enum SlotState : int
    {
        StandBy = 0,
        PreviewProcessor = 1,
        PreviewSender = 2,
        Processor = 3,
        Sender = 4
    }

    public interface IWorkflowSlot : IWorkflowContext
    {
        public ObservableCollection<IWorkflowNode> Targets { get; set; }
        public ObservableCollection<IWorkflowNode> Sources { get; set; }
        public IWorkflowNode? Parent { get; set; }
        public SlotCapacity Capacity { get; set; }
        public SlotState State { get; set; }
        public Anchor Anchor { get; set; }
        public Anchor Offset { get; set; }
        public Size Size { get; set; }

        public IVeloxCommand DeleteCommand { get; }
        public IVeloxCommand ConnectingCommand { get; }
        public IVeloxCommand ConnectedCommand { get; }
    }
}
