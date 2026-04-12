namespace VeloxDev.WorkflowSystem
{
    public interface IWorkflowActionPair
    {
        public Action Redo { get; }
        public Action Undo { get; }
    }
}
