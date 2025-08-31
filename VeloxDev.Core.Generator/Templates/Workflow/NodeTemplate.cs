namespace VeloxDev.Core.Generator.Templates.Workflow;

public static class NodeTemplate
{
    public static string FromTaskConfig(bool CanConcurrent)
    {
        string command = CanConcurrent ? "ConcurrentVeloxCommand" : "VeloxCommand";
        return $$"""
    private void InitializeWorkflow()
    {
        slots.CollectionChanged += OnSlotsCollectionChanged;
    }

    private global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? parent = null;
    private global::VeloxDev.Core.WorkflowSystem.Anchor anchor = new();
    private global::VeloxDev.Core.WorkflowSystem.Size size = new();
    private global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> slots = [];
    private bool isEnabled = true;
    private string uID = string.Empty;
    private string name = string.Empty;

    public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? Parent
    {
        get => parent;
        set
        {
            if (object.Equals(parent, value)) return;
            var old = parent;
            OnPropertyChanging(nameof(Parent));
            OnParentChanging(old, value);
            parent = value;
            OnParentChanged(old, value);
            OnPropertyChanged(nameof(Parent));
        }
    }
    partial void OnParentChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? newValue);
    partial void OnParentChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? newValue);
    public global::VeloxDev.Core.WorkflowSystem.Anchor Anchor
    {
        get => anchor;
        set
        {
            if (object.Equals(anchor, value)) return;
            var old = anchor;
            OnPropertyChanging(nameof(Anchor));
            OnAnchorChanging(old, value);
            anchor = value;
            foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot in slots)
            {
                slot.Anchor = value + slot.Offset + new global::VeloxDev.Core.WorkflowSystem.Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
            }
            OnAnchorChanged(old, value);
            OnPropertyChanged(nameof(Anchor));
        }
    }
    partial void OnAnchorChanging(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue, global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
    partial void OnAnchorChanged(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue, global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
    public global::VeloxDev.Core.WorkflowSystem.Size Size
    {
        get => size;
        set
        {
            if (object.Equals(size, value)) return;
            var old = size;
            OnPropertyChanging(nameof(Size));
            OnSizeChanging(old, value);
            size = value;
            OnSizeChanged(old, value);
            OnPropertyChanged(nameof(Size));
        }
    }
    partial void OnSizeChanging(global::VeloxDev.Core.WorkflowSystem.Size oldValue, global::VeloxDev.Core.WorkflowSystem.Size newValue);
    partial void OnSizeChanged(global::VeloxDev.Core.WorkflowSystem.Size oldValue, global::VeloxDev.Core.WorkflowSystem.Size newValue);
    public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> Slots
    {
        get => slots;
        set
        {
            if (object.Equals(slots, value)) return;
            var old = slots;
            OnPropertyChanging(nameof(Slots));
            OnSlotsChanging(old, value);
            slots = value;
            old.CollectionChanged -= OnSlotsCollectionChanged;
            value.CollectionChanged += OnSlotsCollectionChanged;
            foreach (var slot in value)
            {
                slot.Parent = this;
            }
            OnSlotsChanged(old, value);
            OnPropertyChanged(nameof(Slots));
        }
    }
    partial void OnSlotsChanging(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> newValue);
    partial void OnSlotsChanged(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> newValue);
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (object.Equals(isEnabled, value)) return;
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
            if (object.Equals(uID, value)) return;
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
            if (object.Equals(name, value)) return;
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

    private void OnSlotsCollectionChanged(object? sender, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case global::System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                if (e.NewItems is null) return;
                foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot in e.NewItems)
                {
                    slot.Parent = this;
                    slot.Anchor = Anchor + slot.Offset + new global::VeloxDev.Core.WorkflowSystem.Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
                    OnSlotAdded(slot);
                }
                break;
            case global::System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                if (e.OldItems is null) return;
                foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot in e.OldItems)
                {
                    slot.Parent = null;
                    slot.Anchor = Anchor + slot.Offset + new global::VeloxDev.Core.WorkflowSystem.Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
                    OnSlotRemoved(slot);
                }
                break;
        }
    }
    partial void OnSlotAdded(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot);
    partial void OnSlotRemoved(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot);

    private async global::System.Threading.Tasks.Task ExecuteNodeTask(object? parameter, global::System.Threading.CancellationToken ct)
    {
        await OnExecute(parameter,ct);
    }
    private partial global::System.Threading.Tasks.Task OnExecute(object? parameter, global::System.Threading.CancellationToken ct);

    private global::System.Threading.Tasks.Task CreateSlot(object? parameter, global::System.Threading.CancellationToken ct)
    {
        if (parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot)
        {
            slots.Add(slot);
            Parent?.PushUndo(() => { slots.Remove(slot); });
        }
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
    private global::System.Threading.Tasks.Task Delete(object? parameter, global::System.Threading.CancellationToken ct)
    {
        if (Parent is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree tree)
        {
            var affectedLinks = new global::System.Collections.Generic.List<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink>();
            var linkRemovalActions = new global::System.Collections.Generic.List<global::System.Action>();

            var links = new global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink[tree.Links.Count];
            tree.Links.CopyTo(links, 0);

            foreach (var link in links)
            {
                if (link.Sender?.Parent == this || link.Processor?.Parent == this)
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

            int nodeIndex = tree.Nodes.IndexOf(this);
            var nodeRemovalAction = new global::System.Action(() =>
            {
                tree.Nodes.Insert(nodeIndex, this);
                foreach (var slot in Slots)
                {
                    slot.Parent = this;
                }
            });

            tree.Nodes.Remove(this);

            tree.PushUndo(() =>
            {
                nodeRemovalAction();
                foreach (var undoAction in linkRemovalActions)
                {
                    undoAction();
                }
            });
        }
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
    private global::System.Threading.Tasks.Task Broadcast(object? parameter, global::System.Threading.CancellationToken ct)
    {
        var senders = global::System.Linq.Enumerable.ToArray(
            global::System.Linq.Enumerable.Where(slots, s => s.State == global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Sender)
        );
        foreach (var slot in senders)
        {
            foreach (var target in slot.Targets)
            {
                target.ExecuteCommand.Execute(parameter);
            }
        }
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
    private async global::System.Threading.Tasks.Task Execute(object? parameter, global::System.Threading.CancellationToken ct)
    {
        await ExecuteNodeTask(parameter,ct);
    }
    private global::System.Threading.Tasks.Task Undo(object? parameter, global::System.Threading.CancellationToken ct)
    {
        Parent?.UndoCommand.Execute(null);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CreateSlotCommand = null;
    public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CreateSlotCommand
    {
        get
        {
            _buffer_CreateSlotCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: CreateSlot,
                canExecute: _ => true);
            return _buffer_CreateSlotCommand;
        }
    }
    private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_DeleteCommand = null;
    public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand DeleteCommand
    {
        get
        {
            _buffer_DeleteCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Delete,
                canExecute: _ => true);
            return _buffer_DeleteCommand;
        }
    }
    private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_BroadcastCommand = null;
    public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand BroadcastCommand
    {
        get
        {
            _buffer_BroadcastCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Broadcast,
                canExecute: _ => true);
            return _buffer_BroadcastCommand;
        }
    }
    private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ExecuteCommand = null;
    public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ExecuteCommand
    {
        get
        {
            _buffer_ExecuteCommand ??= new global::VeloxDev.Core.MVVM.{{command}}(
                executeAsync: Execute,
                canExecute: _ => true);
            return _buffer_ExecuteCommand;
        }
    }
    private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_UndoCommand = null;
    public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand UndoCommand
    {
        get
        {
            _buffer_UndoCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Undo,
                canExecute: _ => true);
            return _buffer_UndoCommand;
        }
    }
""";
    }
}
