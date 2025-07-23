using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;

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
        public IWorkflowNode? Parent { get; set; }
        public SlotCapacity Capacity { get; set; }
        public SlotState State { get; set; }

        public IVeloxCommand ConnectingCommand { get; }
        public IVeloxCommand ConnectedCommand { get; }
    }
}
