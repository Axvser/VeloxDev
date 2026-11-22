using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow;

[WorkflowBuilder.Node<NodeHelper>]
internal partial class NodeViewModel
{
    public NodeViewModel() => InitializeWorkflow();

    [VeloxProperty] private string _name = string.Empty;

    /// <summary>The node type name shown as the card subtitle.</summary>
    public string TypeName => GetType().Name;

    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [VeloxProperty] public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
}
