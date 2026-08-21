namespace VeloxDev.WorkflowSystem
{
    public interface IWorkflowHelper
    {
        public void Closing();    // Before closing
        public Task CloseAsync(); // Close safely
        public void Closed();     // After closing
    }
}
