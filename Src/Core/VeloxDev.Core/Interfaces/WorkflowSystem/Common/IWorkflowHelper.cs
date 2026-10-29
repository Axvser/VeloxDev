namespace VeloxDev.WorkflowSystem
{
    /// <summary>The lifecycle hooks every workflow helper implements.</summary>
    public interface IWorkflowHelper
    {
        /// <summary>Runs before the workflow closes.</summary>
        public void Closing();

        /// <summary>Closes the workflow safely.</summary>
        public Task CloseAsync();

        /// <summary>Runs after the workflow has closed.</summary>
        public void Closed();
    }
}
