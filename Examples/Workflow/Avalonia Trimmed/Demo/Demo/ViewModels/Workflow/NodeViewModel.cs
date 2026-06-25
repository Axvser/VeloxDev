using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow
{
    [WorkflowBuilder.Node<NodeHelper>]
    internal partial class NodeViewModel
    {
        public NodeViewModel() => InitializeWorkflow();

        // ↓ 扩展视图模型

        [VeloxProperty] private string _name = string.Empty;

        [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

        [VeloxProperty] public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
    }
}
