using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    [Flags]
    public enum SlotCapacity : int
    {
        Default = 0,
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

    public interface IContextSlot : INotifyPropertyChanging, INotifyPropertyChanged, IContextState
    {
        public int UID { get; set; }
        public Anchor Anchor { get; set; }
        public IContext? Parent { get; set; }
        public IContextSlot? Target { get; set; }
        public SlotCapacity Capacity { get; set; }
        public SlotState State { get; set; }

        public IVeloxCommand ConnectingCommand { get; }
        public IVeloxCommand ConnectedCommand { get; }
        public IVeloxCommand ClearConnectionCommand { get; }
    }
}
