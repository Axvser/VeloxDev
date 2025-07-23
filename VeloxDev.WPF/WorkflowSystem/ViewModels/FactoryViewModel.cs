using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Xml;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.ContextTree]
    public partial class FactoryViewModel : IWorkflowTree
    {
        public FactoryViewModel()
        {
            children.CollectionChanged += OnChildrenChanged;
        }

        [VeloxProperty]
        private bool isEnabled = true;

        /*节点上下文集合*/
        private ObservableCollection<IWorkflowNode> children = [];
        public ObservableCollection<IWorkflowNode> Children
        {
            get => children;
            set
            {
                if (Equals(children, value)) return;
                if (children is INotifyCollectionChanged oldCollection)
                {
                    oldCollection.CollectionChanged -= OnChildrenChanged;
                }
                children = value;
                if (children is INotifyCollectionChanged newCollection)
                {
                    newCollection.CollectionChanged += OnChildrenChanged;
                    foreach (IWorkflowNode child in children)
                    {
                        child.Tree = this;
                    }
                }
                OnPropertyChanged(nameof(Children));
            }
        }
        private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems == null) return;
                    foreach (IWorkflowNode newItem in e.NewItems)
                    {
                        newItem.Tree = this;
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems == null) return;
                    foreach (IWorkflowNode oldItem in e.OldItems)
                    {
                        oldItem.Tree = null;
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    if (e.NewItems == null) return;
                    foreach (IWorkflowNode item in e.NewItems)
                    {
                        item.Tree = this;
                    }
                    if (e.OldItems == null) return;
                    foreach (IWorkflowNode item in e.OldItems)
                    {
                        item.Tree = null;
                    }
                    break;
            }
        }

        [VeloxProperty]
        private ObservableCollection<IWorkflowConnection> connectors = [];

        private IWorkflowSlot? senderSlot = null;
        private IWorkflowSlot? processorSlot = null;
        [VeloxProperty]
        private Dictionary<int, IWorkflowConnection> slotPairs = [];
        [VeloxProperty]
        private IWorkflowConnection virtualConnector = new ConnectorContext()
        {
            End = new NodeContext()
        };
        [VeloxCommand]
        private Task CreateNode(object? parameter, CancellationToken ct)
        {
            var context = new ShowerNodeViewModel
            {
                Anchor = VirtualConnector.End?.Anchor ?? Anchor.Default
            };
            Children.Add(context);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task RemoveVirtualConnector(object? parameter, CancellationToken ct)
        {
            virtualConnector.End = null;
            return Task.CompletedTask;
        }
        public void SetSenderSlot(IWorkflowSlot slot)
        {
            if (slot.Capacity.HasFlag(SlotCapacity.Sender))
            {
                var oldSlot = Interlocked.Exchange(ref senderSlot, slot);
                if (oldSlot != null && oldSlot != slot)
                {
                    oldSlot.State = SlotState.StandBy;
                }
                slot.State = SlotState.PreviewSender;
            }
            SetSlotPair();
        }
        public void SetProcessorSlot(IWorkflowSlot slot)
        {
            if (slot.Capacity.HasFlag(SlotCapacity.Processor))
            {
                var oldSlot = Interlocked.Exchange(ref processorSlot, slot);
                if (oldSlot != null && oldSlot != slot)
                {
                    oldSlot.State = SlotState.StandBy;
                }
                slot.State = SlotState.PreviewProcessor;
            }
            SetSlotPair();
        }
        public void RemoveSlotPairFrom(IWorkflowSlot slot)
        {
            if (slot.State == SlotState.Sender && slot.Target?.State == SlotState.Processor)
            {
                RemoveSlotPair(slot, slot.Target);
            }
            else if (slot.State == SlotState.Processor && slot.Target?.State == SlotState.Sender)
            {
                RemoveSlotPair(slot.Target, slot);
            }
        }
        private void SetSlotPair()
        {
            if (senderSlot is not null && processorSlot is not null)
            {
                if (senderSlot != processorSlot)
                {
                    var code = HashCode.Combine(senderSlot.UID, processorSlot.UID);
                    senderSlot.State = SlotState.Sender;
                    processorSlot.State = SlotState.Processor;
                    var connector = new ConnectorContext
                    {
                        Start = senderSlot.Parent,
                        End = processorSlot.Parent
                    };
                    if (SlotPairs.TryGetValue(code, out var existingConnector))
                    {
                        existingConnector.Start = senderSlot.Parent;
                        existingConnector.End = processorSlot.Parent;
                    }
                    else
                    {
                        SlotPairs.Add(code, connector);
                        Connectors.Add(connector);
                    }
                }
                senderSlot = null;
                processorSlot = null;
            }
        }
        private void RemoveSlotPair(IWorkflowSlot sender, IWorkflowSlot processor)
        {
            var code = HashCode.Combine(sender.UID, processor.UID);
            if (SlotPairs.TryGetValue(code, out var connector))
            {
                SlotPairs.Remove(code);
                Connectors.Remove(connector);
            }
        }
    }
}
