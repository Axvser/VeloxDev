using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

        [VeloxProperty]
        private IWorkflowLink virtualLink = new Link() { Processor = new Slot() };
        [VeloxProperty]
        private ObservableCollection<IWorkflowNode> nodes = [];
        [VeloxProperty]
        private ObservableCollection<IWorkflowLink> links = [];
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
            if(parameter is IWorkflowSlot slot)
            {

            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task SetVirtualProcessor(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task ClearVirtualLink(object? parameter, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
