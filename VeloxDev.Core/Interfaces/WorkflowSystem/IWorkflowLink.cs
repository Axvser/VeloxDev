using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowLink : IWorkflowContext
    {
        public IWorkflowSlot? Sender { get; set; }
        public IWorkflowSlot? Processor { get; set; }
    }
}
