using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
            OnExecute(parameter);
        }
        partial void OnExecute(object? parameter);


        [VeloxCommand]
        private Task CreateSlot(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowSlot slot)
            {
                slots.Add(slot);
                Parent?.PushUndo(() => { slots.Remove(slot); });
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private static Task Delete(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowNode node && node.Parent is IWorkflowTree tree)
            {
                var affectedLinks = new List<IWorkflowLink>();
                var linkRemovalActions = new List<Action>();

                foreach (var link in tree.Links.ToList())
                {
                    if (link.Sender?.Parent == node || link.Processor?.Parent == node)
                    {
                        affectedLinks.Add(link);
                        tree.Links.Remove(link);

                        var sender = link.Sender;
                        var processor = link.Processor;
                        var senderNode = sender?.Parent;
                        var processorNode = processor?.Parent;

                        if (sender != null && processorNode != null)
                        {
                            sender.Targets.Remove(processorNode);
                        }
                        if (processor != null && senderNode != null)
                        {
                            processor.Sources.Remove(senderNode);
                        }

                        linkRemovalActions.Add(() =>
                        {
                            tree.Links.Add(link);
                            if (sender != null && processorNode != null)
                            {
                                sender.Targets.Add(processorNode);
                            }
                            if (processor != null && senderNode != null)
                            {
                                processor.Sources.Add(senderNode);
                            }
                        });
                    }
                }

                int nodeIndex = tree.Nodes.IndexOf(node);
                var nodeRemovalAction = new Action(() =>
                {
                    tree.Nodes.Insert(nodeIndex, node);
                    foreach (var slot in node.Slots)
                    {
                        slot.Parent = node;
                    }
                });

                tree.Nodes.Remove(node);

                tree.PushUndo(() =>
                {
                    nodeRemovalAction();
                    foreach (var undoAction in linkRemovalActions)
                    {
                        undoAction();
                    }
                });
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Broadcast(object? parameter, CancellationToken ct)
        {
            var senders = slots.Where(s => s.State == SlotState.Sender).ToArray();
            foreach (var slot in senders)
            {
                foreach (var target in slot.Targets)
                {
                    target.Execute(parameter);
                }
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task Execute(object? parameter, CancellationToken ct)
        {
            Execute(parameter);
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
