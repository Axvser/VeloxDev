using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

public partial class ConditionalSlot<TSlot>
    where TSlot : IWorkflowSlotViewModel, new()
{
    [VeloxProperty] private string _name = string.Empty;
    [VeloxProperty] private object? _value;
    [VeloxProperty] private TSlot _slot = new();
}
