using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    public partial class ShowerNodeViewModel : IWorkflowNode
    {
        public ShowerNodeViewModel()
        {
            slots.CollectionChanged += OnSlotsCollectionChanged;
        }

        [VeloxProperty]
        private IWorkflowTree? parent = null;
        [VeloxProperty]
        private Anchor anchor = new();
        [VeloxProperty]
        private Size size = new();
        [VeloxProperty]
        private ObservableCollection<IWorkflowSlot> slots = [];
        [VeloxProperty]
        private bool isEnabled = true;
        [VeloxProperty]
        private string uID = string.Empty;
        [VeloxProperty]
        private string name = string.Empty;

        partial void OnAnchorChanged(Anchor oldValue, Anchor newValue)
        {
            foreach (IWorkflowSlot slot in slots)
            {
                slot.Anchor = newValue + slot.Offset + new Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
            }
        }
        partial void OnSlotsChanged(ObservableCollection<IWorkflowSlot> oldValue, ObservableCollection<IWorkflowSlot> newValue)
        {
            oldValue.CollectionChanged -= OnSlotsCollectionChanged;
            newValue.CollectionChanged += OnSlotsCollectionChanged;
            foreach (var slot in newValue)
            {
                slot.Parent = this;
            }
        }
        private void OnSlotsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems is null) return;
                    foreach (IWorkflowSlot slot in e.NewItems)
                    {
                        slot.Parent = this;
                        slot.Anchor = Anchor + slot.Offset + new Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems is null) return;
                    foreach (IWorkflowSlot slot in e.OldItems)
                    {
                        slot.Parent = null;
                        slot.Anchor = Anchor + slot.Offset + new Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
                    }
                    break;
            }
        }

        public void Execute(object? parameter)
        {

        }

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
        private Task CreateSlot(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowSlot slot)
            {
                Slots.Add(slot);
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task RemoveSlot(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Delete(object? parameter, CancellationToken ct)
        {
            if (parent is IWorkflowTree tree)
            {
                tree.Nodes.Remove(this);
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Broadcast(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Execute(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Cancel(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Interrupt(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Undo(object? parameter, CancellationToken ct)
        {
            Parent?.UndoCommand.Execute(null);
            return Task.CompletedTask;
        }
    }
}
