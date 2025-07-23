namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowConnection : IWorkflowContext
    {
        public IWorkflowSlot? Start { get; set; }
        public IWorkflowSlot? End { get; set; }
    }
}
