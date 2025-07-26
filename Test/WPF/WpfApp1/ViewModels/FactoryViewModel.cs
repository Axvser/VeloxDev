namespace WpfApp1.ViewModels;

public partial class FactoryViewModel : VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree
{
    private void InitializeWorkflow()
    {
        nodes.CollectionChanged += OnNodesCollectionChanged;
    }

    private VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? actualSender = null;

    private readonly System.Collections.Concurrent.ConcurrentStack<Action> undos = [];

    private VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink virtualLink = new VeloxDev.Core.WorkflowSystem.LinkContext() { Processor = new VeloxDev.Core.WorkflowSystem.SlotContext() };
    private System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> nodes = [];
    private System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> links = [];
    public bool isEnabled = true;
    public string uID = string.Empty;
    public string name = string.Empty;

    public event System.ComponentModel.PropertyChangingEventHandler? PropertyChanging;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanging(string propertyName)
    {
        PropertyChanging?.Invoke(this, new System.ComponentModel.PropertyChangingEventArgs(propertyName));
    }
    public void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink VirtualLink
    {
        get => virtualLink;
        set
        {
            if (Equals(virtualLink, value)) return;
            var old = virtualLink;
            OnPropertyChanging(nameof(VirtualLink));
            OnVirtualLinkChanging(old, value);
            virtualLink = value;
            OnVirtualLinkChanged(old, value);
            OnPropertyChanged(nameof(VirtualLink));
        }
    }
    partial void OnVirtualLinkChanging(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink newValue);
    partial void OnVirtualLinkChanged(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink newValue);
    public System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> Nodes
    {
        get => nodes;
        set
        {
            if (Equals(nodes, value)) return;
            var old = nodes;
            OnPropertyChanging(nameof(Nodes));
            OnNodesChanging(old, value);
            nodes = value;
            old.CollectionChanged -= OnNodesCollectionChanged;
            value.CollectionChanged += OnNodesCollectionChanged;
            foreach (VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node in value)
            {
                node.Parent = this;
            }
            OnNodesChanged(old, value);
            OnPropertyChanged(nameof(Nodes));
        }
    }
    partial void OnNodesChanging(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    partial void OnNodesChanged(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    public System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> Links
    {
        get => links;
        set
        {
            if (Equals(links, value)) return;
            var old = links;
            OnPropertyChanging(nameof(Links));
            OnLinksChanging(old, value);
            links = value;
            OnLinksChanged(old, value);
            OnPropertyChanged(nameof(Links));
        }
    }
    partial void OnLinksChanging(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> newValue);
    partial void OnLinksChanged(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> newValue);
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (Equals(isEnabled, value)) return;
            var old = isEnabled;
            OnPropertyChanging(nameof(IsEnabled));
            OnIsEnabledChanging(old, value);
            isEnabled = value;
            OnIsEnabledChanged(old, value);
            OnPropertyChanged(nameof(IsEnabled));
        }
    }
    partial void OnIsEnabledChanging(bool oldValue, bool newValue);
    partial void OnIsEnabledChanged(bool oldValue, bool newValue);
    public string UID
    {
        get => uID;
        set
        {
            if (Equals(uID, value)) return;
            var old = uID;
            OnPropertyChanging(nameof(UID));
            OnUIDChanging(old, value);
            uID = value;
            OnUIDChanged(old, value);
            OnPropertyChanged(nameof(UID));
        }
    }
    partial void OnUIDChanging(string oldValue, string newValue);
    partial void OnUIDChanged(string oldValue, string newValue);
    public string Name
    {
        get => name;
        set
        {
            if (Equals(name, value)) return;
            var old = name;
            OnPropertyChanging(nameof(Name));
            OnNameChanging(old, value);
            name = value;
            OnNameChanged(old, value);
            OnPropertyChanged(nameof(Name));
        }
    }
    partial void OnNameChanging(string oldValue, string newValue);
    partial void OnNameChanged(string oldValue, string newValue);

    private void OnNodesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node in e.NewItems)
            {
                node.Parent = this;
                OnNodeAdded(node);
            }
        }
        if (e.OldItems != null)
        {
            foreach (VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node in e.OldItems)
            {
                node.Parent = null;
                OnNodeRemoved(node);
            }
        }
    }
    partial void OnNodeAdded(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node);
    partial void OnNodeRemoved(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node);

    private Task CreateNode(object? parameter, CancellationToken ct)
    {
        if (parameter is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node)
        {
            nodes.Add(node);
            PushUndo(() => { nodes.Remove(node); });
        }
        return Task.CompletedTask;
    }
    private Task SetMouse(object? parameter, CancellationToken ct)
    {
        if (parameter is VeloxDev.Core.WorkflowSystem.Anchor anchor && VirtualLink.Processor is not null)
        {
            VirtualLink.Processor.Anchor = anchor;
        }
        return Task.CompletedTask;
    }
    private Task SetSender(object? parameter, CancellationToken ct)
    {
        if (parameter is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot)
        {
            actualSender = slot;
            VirtualLink.Sender = slot;
            VirtualLink.IsEnabled = true;
        }
        return Task.CompletedTask;
    }
    private Task SetProcessor(object? parameter, CancellationToken ct)
    {
        if (parameter is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot && actualSender != null)
        {
            // 检查是否允许连接
            if (actualSender.Capacity.HasFlag(VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Sender) &&
                slot.Capacity.HasFlag(VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Processor))
            {
                // 创建新连接
                var newLink = new VeloxDev.Core.WorkflowSystem.LinkContext
                {
                    Sender = actualSender,
                    Processor = slot,
                    IsEnabled = true
                };

                // 更新连接关系
                links.Add(newLink);
                actualSender.Targets.Add(slot.Parent!);
                slot.Sources.Add(actualSender.Parent!);

                var old_actualSender = actualSender;

                // 设置撤销操作
                PushUndo(() =>
                {
                    slot.Sources.Remove(old_actualSender.Parent!);
                    old_actualSender.Targets.Remove(slot.Parent!);
                    links.Remove(newLink);
                });
            }

            // 重置连接状态
            actualSender = null;
            VirtualLink.Sender = null;
            VirtualLink.IsEnabled = false;
        }
        return Task.CompletedTask;
    }
    private Task Undo(object? parameter, CancellationToken ct)
    {
        if (undos.TryPop(out var recipient))
        {
            recipient.Invoke();
        }
        return Task.CompletedTask;
    }

    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CreateNodeCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CreateNodeCommand
    {
        get
        {
            _buffer_CreateNodeCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: CreateNode,
                canExecute: _ => true);
            return _buffer_CreateNodeCommand;
        }
    }
    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetMouseCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetMouseCommand
    {
        get
        {
            _buffer_SetMouseCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: SetMouse,
                canExecute: _ => true);
            return _buffer_SetMouseCommand;
        }
    }
    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetSenderCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetSenderCommand
    {
        get
        {
            _buffer_SetSenderCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: SetSender,
                canExecute: _ => true);
            return _buffer_SetSenderCommand;
        }
    }
    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetProcessorCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetProcessorCommand
    {
        get
        {
            _buffer_SetProcessorCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: SetProcessor,
                canExecute: _ => true);
            return _buffer_SetProcessorCommand;
        }
    }
    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_UndoCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand UndoCommand
    {
        get
        {
            _buffer_UndoCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Undo,
                canExecute: _ => true);
            return _buffer_UndoCommand;
        }
    }

    public void PushUndo(Action undo)
    {
        undos.Push(undo);
    }
    public VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink? FindLink(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode sender, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode processor)
    {
        return Links.FirstOrDefault(link =>
            link.Sender?.Parent == sender &&
            link.Processor?.Parent == processor);
    }
}
