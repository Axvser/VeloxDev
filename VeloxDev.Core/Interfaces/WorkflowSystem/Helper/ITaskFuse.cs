using VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.Helper
{
    public interface ITaskFuse
    {
        public bool Fuse(IContext sender, IContext processor, object?[] args, Exception ex);
    }
}
