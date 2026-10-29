namespace VeloxDev.WorkflowSystem
{
    /// <summary>An undo/redo pair of actions.</summary>
    public interface IWorkflowActionPair
    {
        /// <summary>Redoes the action.</summary>
        public Action Redo { get; }

        /// <summary>Undoes the action.</summary>
        public Action Undo { get; }
    }
}
