using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed partial class Slot : IWorkflowSlot
    {
        [VeloxProperty]
        private IWorkflowNode? parent = null;
        [VeloxProperty]
        private SlotCapacity capacity = SlotCapacity.Universal;
        [VeloxProperty]
        private SlotState state = SlotState.StandBy;
        [VeloxProperty]
        private Anchor anchor = Anchor.Default;
        [VeloxProperty]
        private bool isEnabled = true;
        [VeloxProperty]
        private string uID = string.Empty;
        [VeloxProperty]
        private string name = string.Empty;

        [VeloxCommand]
        private Task Delete(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Connecting(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Connected(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
