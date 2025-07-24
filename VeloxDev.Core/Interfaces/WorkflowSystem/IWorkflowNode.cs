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

        public void Execute(object? parameter);

        public IVeloxCommand CreateSlotCommand { get; }
        public IVeloxCommand RemoveSlotCommand { get; }
        public IVeloxCommand CreateLinkCommand { get; }
        public IVeloxCommand RemoveLinkCommand { get; }
        public IVeloxCommand DeleteCommand { get; }
        public IVeloxCommand BroadcastCommand { get; }
        public IVeloxCommand ExecuteCommand { get; }
        public IVeloxCommand CancelCommand { get; }
        public IVeloxCommand InterruptCommand { get; }
    }
}
