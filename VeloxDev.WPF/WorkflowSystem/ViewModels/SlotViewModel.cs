using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    public partial class SlotViewModel : IWorkflowSlot
    {
        [VeloxProperty]
        private IWorkflowNode? parent = null;
        [VeloxProperty]
        private SlotCapacity capacity = SlotCapacity.Universal;
        [VeloxProperty]
        private SlotState state = SlotState.StandBy;
        [VeloxProperty]
        private Anchor anchor = new();
        [VeloxProperty]
        private Anchor offset = new();
        [VeloxProperty]
        private Size size = new();
        [VeloxProperty]
        private bool isEnabled = true;
        [VeloxProperty]
        private string uID = string.Empty;
        [VeloxProperty]
        private string name = string.Empty;

        [VeloxCommand]
        private Task CreateLink(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task RemoveLink(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Delete(object? parameter, CancellationToken ct)
        {
            Parent?.Slots?.Remove(this);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Connecting(object? parameter, CancellationToken ct)
        {
            Parent?.Parent?.SetVirtualSenderCommand.Execute(this);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Connected(object? parameter, CancellationToken ct)
        {
            Parent?.Parent?.SetVirtualProcessorCommand.Execute(this);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Undo(object? parameter, CancellationToken ct)
        {
            Parent?.Parent?.UndoCommand.Execute(null);
            return Task.CompletedTask;
        }
    }
}
