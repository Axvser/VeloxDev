using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.View
{
    public interface IViewTree
    {
        public bool MoveNode(object node, Anchor anchor);
        public bool InstallNode(object node, Anchor anchor);
        public bool UninstallNode(object node);
        public bool InstallConnector(object connector);
        public bool UninstallConnector(object connector);
    }
}
