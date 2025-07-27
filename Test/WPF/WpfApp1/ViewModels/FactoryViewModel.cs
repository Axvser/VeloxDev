namespace WpfApp1.ViewModels
{
    public partial class FactoryViewModel : global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree
    {
        public FactoryViewModel()
        {
            InitializeWorkflow();
        }

        private void InitializeWorkflow()
        {
            nodes.CollectionChanged += OnNodesCollectionChanged;
        }

        private readonly global::System.Collections.Concurrent.ConcurrentStack<global::System.Action> undos = new();

        private global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink virtualLink =
            new global::VeloxDev.Core.WorkflowSystem.LinkContext()
            {
                Processor = new global::VeloxDev.Core.WorkflowSystem.SlotContext()
            };

        private global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> nodes = [];
        private global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> links = [];
        private bool isEnabled = true;
        private string uID = string.Empty;
        private string name = string.Empty;

        public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public event global::System.ComponentModel.PropertyChangingEventHandler? PropertyChanging;

        public void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));

        public void OnPropertyChanging(string propertyName) =>
            PropertyChanging?.Invoke(this, new global::System.ComponentModel.PropertyChangingEventArgs(propertyName));

        public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink VirtualLink
        {
            get => virtualLink;
            set
            {
                if (global::System.Collections.Generic.EqualityComparer<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink>.Default.Equals(virtualLink, value))
                    return;
                OnPropertyChanging(nameof(VirtualLink));
                virtualLink = value;
                OnPropertyChanged(nameof(VirtualLink));
            }
        }

        public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> Nodes
        {
            get => nodes;
            set
            {
                if (global::System.Collections.Generic.EqualityComparer<global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode>>.Default.Equals(nodes, value))
                    return;

                var old = nodes;
                OnPropertyChanging(nameof(Nodes));
                nodes = value;
                old.CollectionChanged -= OnNodesCollectionChanged;
                value.CollectionChanged += OnNodesCollectionChanged;

                foreach (var node in value)
                {
                    node.Parent = this;
                }

                OnPropertyChanged(nameof(Nodes));
            }
        }

        public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> Links
        {
            get => links;
            set
            {
                if (global::System.Collections.Generic.EqualityComparer<global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink>>.Default.Equals(links, value))
                    return;

                OnPropertyChanging(nameof(Links));
                links = value;
                OnPropertyChanged(nameof(Links));
            }
        }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                if (global::System.Collections.Generic.EqualityComparer<bool>.Default.Equals(isEnabled, value))
                    return;

                OnPropertyChanging(nameof(IsEnabled));
                isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }

        public string UID
        {
            get => uID;
            set
            {
                if (global::System.Collections.Generic.EqualityComparer<string>.Default.Equals(uID, value))
                    return;

                OnPropertyChanging(nameof(UID));
                uID = value;
                OnPropertyChanged(nameof(UID));
            }
        }

        public string Name
        {
            get => name;
            set
            {
                if (global::System.Collections.Generic.EqualityComparer<string>.Default.Equals(name, value))
                    return;

                OnPropertyChanging(nameof(Name));
                name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public bool CanUndo => !undos.IsEmpty;

        private void OnNodesCollectionChanged(object? sender, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node in e.NewItems)
                {
                    node.Parent = this;
                }
            }

            if (e.OldItems != null)
            {
                foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node in e.OldItems)
                {
                    node.Parent = null;
                }
            }
        }

        private global::System.Threading.Tasks.Task CreateNode(object? parameter, global::System.Threading.CancellationToken ct)
        {
            if (parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node)
            {
                nodes.Add(node);
                PushUndo(() => nodes.Remove(node));
            }
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        private global::System.Threading.Tasks.Task SetMouse(object? parameter, global::System.Threading.CancellationToken ct)
        {
            if (parameter is global::VeloxDev.Core.WorkflowSystem.Anchor anchor && VirtualLink.Processor != null)
            {
                VirtualLink.Processor.Anchor = anchor;
            }
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        private global::System.Threading.Tasks.Task SetSender(object? parameter, global::System.Threading.CancellationToken ct)
        {
            if (parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot)
            {

            }
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        private global::System.Threading.Tasks.Task SetProcessor(object? parameter, global::System.Threading.CancellationToken ct)
        {
            if (parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot processorSlot &&
                VirtualLink.Sender is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot senderSlot)
            {

            }
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        public void PushUndo(global::System.Action undoAction)
        {
            undos.Push(undoAction);
            OnPropertyChanged(nameof(CanUndo));
        }

        private global::System.Threading.Tasks.Task Undo(object? parameter, global::System.Threading.CancellationToken ct)
        {
            if (undos.TryPop(out var action))
            {
                action.Invoke();
                OnPropertyChanged(nameof(CanUndo));
            }
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink? FindLink(
            global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode sender,
            global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode processor)
        {
            return Links.FirstOrDefault(link =>
                link.Sender?.Parent == sender &&
                link.Processor?.Parent == processor);
        }

        private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CreateNodeCommand;
        public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CreateNodeCommand =>
            _buffer_CreateNodeCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: CreateNode,
                canExecute: _ => true);

        private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetMouseCommand;
        public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetMouseCommand =>
            _buffer_SetMouseCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: SetMouse,
                canExecute: _ => true);

        private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetSenderCommand;
        public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetSenderCommand =>
            _buffer_SetSenderCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: SetSender,
                canExecute: _ => true);

        private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetProcessorCommand;
        public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetProcessorCommand =>
            _buffer_SetProcessorCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: SetProcessor,
                canExecute: _ => true);

        private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_UndoCommand;
        public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand UndoCommand =>
            _buffer_UndoCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Undo,
                canExecute: _ => CanUndo);
    }
}