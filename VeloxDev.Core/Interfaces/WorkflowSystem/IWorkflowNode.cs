using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowNode : IWorkflowContext
    {
        public IWorkflowTree? Parent { get; set; }
        public Anchor Anchor { get; set; }
        public Size Size { get; set; }
        public ObservableCollection<IWorkflowSlot> Slots { get; set; }

        public IVeloxCommand CreateSlotCommand { get; }
        public IVeloxCommand DeleteCommand { get; }
        public IVeloxCommand BroadcastCommand { get; }
        public IVeloxCommand WorkCommand { get; }
    }
}
