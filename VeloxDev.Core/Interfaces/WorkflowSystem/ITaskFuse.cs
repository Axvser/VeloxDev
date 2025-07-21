namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface ITaskFuse
    {
        public bool Fuse(IContext sender, IContext processor, object?[] args, Exception ex);
    }
}
