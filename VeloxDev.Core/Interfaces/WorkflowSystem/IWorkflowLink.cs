using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowLink : IWorkflowContext
    {
        public IWorkflowSlot? Sender { get; set; }
        public IWorkflowSlot? Processor { get; set; }

        public IVeloxCommand DeleteCommand { get; }
    }
}
