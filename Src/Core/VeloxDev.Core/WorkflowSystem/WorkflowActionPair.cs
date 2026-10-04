namespace VeloxDev.WorkflowSystem
{
    /// <summary>
    /// Provide a pair of redo and undo actions for workflow operations
    /// </summary>
    public readonly struct WorkflowActionPair(Action redo, Action undo) : IWorkflowActionPair
    {
        /// <inheritdoc />
        public Action Redo { get; } = redo;
        /// <inheritdoc />
        public Action Undo { get; } = undo;
    }
}
