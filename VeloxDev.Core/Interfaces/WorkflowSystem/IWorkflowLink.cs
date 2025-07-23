namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowLink : IWorkflowContext
    {
        IWorkflowSlot? Sender { get; set; }
        IWorkflowSlot? Processor { get; set; }
    }
}
