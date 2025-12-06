using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem
{
    /// <summary>
    /// Provide a pair of redo and undo actions for workflow operations
    /// </summary>
    public readonly struct WorkflowActionPair(Action redo, Action undo) : IWorkflowActionPair
    {
        public Action Redo { get; } = redo;
        public Action Undo { get; } = undo;
    }
}
