using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    public partial class FactoryViewModel : IWorkflowTree
    {
        public FactoryViewModel()
        {
            nodes.CollectionChanged += OnNodesCollectionChanged;
        }

        private IWorkflowSlot? actualSender = null;
        private IWorkflowSlot? actualProcessor = null;

        [VeloxProperty]
        private IWorkflowLink virtualLink = new Link() { Processor = new Slot() };
        [VeloxProperty]
        private ObservableCollection<IWorkflowNode> nodes = [];
        [VeloxProperty]
        private ObservableCollection<IWorkflowLink> links = [];
        [VeloxProperty]
        private Dictionary<IWorkflowNode, Dictionary<IWorkflowNode, IWorkflowLink>> linkGraph = [];
        [VeloxProperty]
        public bool isEnabled = true;
        [VeloxProperty]
        public string uID = string.Empty;
        [VeloxProperty]
        public string name = string.Empty;

        partial void OnNodesChanged(ObservableCollection<IWorkflowNode> oldValue, ObservableCollection<IWorkflowNode> newValue)
        {
            oldValue.CollectionChanged -= OnNodesCollectionChanged;
            newValue.CollectionChanged += OnNodesCollectionChanged;
            foreach (IWorkflowNode node in newValue)
            {
                node.Parent = this;
            }
        }
        private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (IWorkflowNode node in e.NewItems)
                {
                    node.Parent = this;
                }
            }
            if (e.OldItems != null)
            {
                foreach (IWorkflowNode node in e.OldItems)
                {
                    node.Parent = null;
                }
            }
        }

        [VeloxCommand]
        private Task CreateNode(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowNode node)
            {
                nodes.Add(node);
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task SetVirtualMouse(object? parameter, CancellationToken ct)
        {
            if (parameter is Anchor anchor && VirtualLink.Processor is not null)
            {
                VirtualLink.Processor.Anchor = anchor;
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task SetVirtualSender(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowSlot slot &&
                slot.Capacity.HasFlag(SlotCapacity.Sender) &&
                actualProcessor != slot)
            {
                actualSender = slot;
                VirtualLink.Sender = slot;
                slot.State = SlotState.PreviewSender;
            }
            Connect();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task SetVirtualProcessor(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowSlot slot &&
                slot.Capacity.HasFlag(SlotCapacity.Processor) &&
                actualSender != slot)
            {
                actualProcessor = slot;
                slot.State = SlotState.PreviewProcessor;
            }
            Connect();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task ClearVirtualLink(object? parameter, CancellationToken ct)
        {
            VirtualLink.Sender = null;
            actualSender = null;
            actualProcessor = null;
            return Task.CompletedTask;
        }

        private void Connect()
        {
            if (actualSender?.Parent is not IWorkflowNode senderParent ||
                actualProcessor?.Parent is not IWorkflowNode processorParent)
                return;

            if(senderParent == processorParent)
            {
                actualSender = actualProcessor = null;
                VirtualLink.Sender = null;
                return;
            }

            actualSender.State = SlotState.Sender;
            actualProcessor.State = SlotState.Processor;

            var (existingLink, isReverse) = FindExistingConnection(senderParent, processorParent);

            switch (existingLink)
            {
                case null:
                    AddNewConnection(senderParent, processorParent);
                    break;

                case IWorkflowLink link when !isReverse:
                    UpdateSenderSlot(link, actualSender);
                    break;

                case IWorkflowLink link when isReverse:
                    RemoveConnection(link, isReverse ? processorParent : senderParent,
                                          isReverse ? senderParent : processorParent);
                    AddNewConnection(senderParent, processorParent);
                    break;
            }

            actualSender = actualProcessor = null;
            VirtualLink.Sender = null;

            (IWorkflowLink? link, bool isReverse) FindExistingConnection(IWorkflowNode from, IWorkflowNode to)
            {
                if (linkGraph.TryGetValue(from, out var outgoing) && outgoing.TryGetValue(to, out var forwardLink))
                    return (forwardLink, false);

                if (linkGraph.TryGetValue(to, out var incoming) && incoming.TryGetValue(from, out var reverseLink))
                    return (reverseLink, true);

                return (null, false);
            }

            void AddNewConnection(IWorkflowNode from, IWorkflowNode to)
            {
                var newLink = new Link { Sender = actualSender!, Processor = actualProcessor! };

                if (!linkGraph.TryGetValue(from, out var links))
                {
                    links = [];
                    linkGraph[from] = links;
                }
                links[to] = newLink;
                Links.Add(newLink);
            }

            void UpdateSenderSlot(IWorkflowLink existingLink, IWorkflowSlot newSender)
            {
                existingLink.Sender = newSender;

                if (existingLink.Sender.Parent != newSender.Parent)
                {
                    RemoveConnection(existingLink, existingLink.Sender!.Parent!, existingLink.Processor!.Parent!);
                    AddNewConnection(newSender.Parent!, existingLink.Processor.Parent!);
                }
            }

            void RemoveConnection(IWorkflowLink link, IWorkflowNode from, IWorkflowNode to)
            {
                if (linkGraph.TryGetValue(from, out var links))
                {
                    links.Remove(to);
                    if (links.Count == 0) linkGraph.Remove(from);
                }
                Links.Remove(link);
            }
        }
    }
}
