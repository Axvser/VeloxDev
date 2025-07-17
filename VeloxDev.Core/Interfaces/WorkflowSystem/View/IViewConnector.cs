using VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem.View
{
    public interface IViewConnector
    {
        public Anchor StartAnchor { get; set; }
        public Anchor EndAnchor { get; set; }
        public void InitializeWorkflow(IContextConnector context);
    }
}
