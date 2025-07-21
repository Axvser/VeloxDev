using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    [Workflow.ContextTree]
    public partial class FactoryViewModel : IContextTree
    {
        public FactoryViewModel()
        {
            children.CollectionChanged += OnChildrenChanged;
            OnInitialized();
        }

        partial void OnInitialized();

        [VeloxProperty]
        private bool isEnabled = true;
        [VeloxProperty]
        private IContextConnector virtualConnector = new ConnectorContext()
        {
            End = new NodeContext()
        };
        [VeloxProperty]
        private ObservableCollection<IContextConnector> connectors = [];

        private ObservableCollection<IContext> children = [];
        public ObservableCollection<IContext> Children
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
                    foreach (IContext child in children)
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
                    foreach (IContext newItem in e.NewItems)
                    {
                        newItem.Tree = this;
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems == null) return;
                    foreach (IContext oldItem in e.OldItems)
                    {
                        oldItem.Tree = null;
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    if (e.NewItems == null) return;
                    foreach (IContext item in e.NewItems)
                    {
                        item.Tree = this;
                    }
                    if (e.OldItems == null) return;
                    foreach (IContext item in e.OldItems)
                    {
                        item.Tree = null;
                    }
                    break;
            }
        }

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
    }
}
