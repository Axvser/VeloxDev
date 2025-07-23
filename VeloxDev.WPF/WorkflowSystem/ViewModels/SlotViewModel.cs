using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    public partial class SlotViewModel : IWorkflowSlot
    {
        [VeloxProperty]
        private SlotCapacity capacity = SlotCapacity.Universal;
        [VeloxProperty]
        private SlotState state = SlotState.StandBy;
        [VeloxProperty]
        private bool isEnabled = true;
        [VeloxProperty]
        private IWorkflowNode? _parent;
        [VeloxProperty]
        private IWorkflowSlot? _target;
        [VeloxProperty]
        private int _uID;
        [VeloxProperty]
        private Anchor _anchor = Anchor.Default;

        [VeloxCommand]
        private Task Connecting(object? parameter, CancellationToken ct)
        {
            Parent?.Tree?.SetSenderSlot(this);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Connected(object? parameter, CancellationToken ct)
        {
            Parent?.Tree?.SetProcessorSlot(this);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task ClearConnection(object? parameter, CancellationToken ct)
        {
            Parent?.Tree?.RemoveSlotPairFrom(this);
            return Task.CompletedTask;
        }
    }
}
