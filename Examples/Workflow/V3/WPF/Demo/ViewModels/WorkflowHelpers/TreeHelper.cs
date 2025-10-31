using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels.WorkflowHelpers
{
    public class TreeHelper : WorkflowHelper.ViewModel.Tree
    {
        public override bool ValidateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
        {
            // 可以替换默认的验证
            return base.ValidateConnection(sender, receiver);
        }
    }
}
