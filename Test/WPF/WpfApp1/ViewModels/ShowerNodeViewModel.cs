namespace WpfApp1.ViewModels;

public partial class ShowerNodeViewModel : VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode
{
    private void InitializeWorkflow()
    {
        slots.CollectionChanged += OnSlotsCollectionChanged;
    }

    private VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? parent = null;
    private VeloxDev.Core.WorkflowSystem.Anchor anchor = new();
    private VeloxDev.Core.WorkflowSystem.Size size = new();
    private System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> slots = [];
    private bool isEnabled = true;
    private string uID = string.Empty;
    private string name = string.Empty;

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

    public VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? Parent
    {
        get => parent;
        set
        {
            if (Equals(parent, value)) return;
            var old = parent;
            OnPropertyChanging(nameof(Parent));
            OnParentChanging(old, value);
            parent = value;
            OnParentChanged(old, value);
            OnPropertyChanged(nameof(Parent));
        }
    }
    partial void OnParentChanging(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? newValue);
    partial void OnParentChanged(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree? newValue);
    public VeloxDev.Core.WorkflowSystem.Anchor Anchor
    {
        get => anchor;
        set
        {
            if (Equals(anchor, value)) return;
            var old = anchor;
            OnPropertyChanging(nameof(Anchor));
            OnAnchorChanging(old, value);
            anchor = value;
            foreach (VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot in slots)
            {
                slot.Anchor = value + slot.Offset + new VeloxDev.Core.WorkflowSystem.Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
            }
            OnAnchorChanged(old, value);
            OnPropertyChanged(nameof(Anchor));
        }
    }
    partial void OnAnchorChanging(VeloxDev.Core.WorkflowSystem.Anchor oldValue, VeloxDev.Core.WorkflowSystem.Anchor newValue);
    partial void OnAnchorChanged(VeloxDev.Core.WorkflowSystem.Anchor oldValue, VeloxDev.Core.WorkflowSystem.Anchor newValue);
    public VeloxDev.Core.WorkflowSystem.Size Size
    {
        get => size;
        set
        {
            if (Equals(size, value)) return;
            var old = size;
            OnPropertyChanging(nameof(Size));
            OnSizeChanging(old, value);
            size = value;
            OnSizeChanged(old, value);
            OnPropertyChanged(nameof(Size));
        }
    }
    partial void OnSizeChanging(VeloxDev.Core.WorkflowSystem.Size oldValue, VeloxDev.Core.WorkflowSystem.Size newValue);
    partial void OnSizeChanged(VeloxDev.Core.WorkflowSystem.Size oldValue, VeloxDev.Core.WorkflowSystem.Size newValue);
    public System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> Slots
    {
        get => slots;
        set
        {
            if (Equals(slots, value)) return;
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
    partial void OnSlotsChanging(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> newValue);
    partial void OnSlotsChanged(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot> newValue);
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

    private void OnSlotsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                if (e.NewItems is null) return;
                foreach (VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot in e.NewItems)
                {
                    slot.Parent = this;
                    slot.Anchor = Anchor + slot.Offset + new VeloxDev.Core.WorkflowSystem.Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
                    OnSlotAdded(slot);
                }
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                if (e.OldItems is null) return;
                foreach (VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot in e.OldItems)
                {
                    slot.Parent = null;
                    slot.Anchor = Anchor + slot.Offset + new VeloxDev.Core.WorkflowSystem.Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
                    OnSlotRemoved(slot);
                }
                break;
        }
    }
    partial void OnSlotAdded(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot);
    partial void OnSlotRemoved(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot);

    public void Execute(object? parameter)
    {
        OnExecute(parameter);
    }
    partial void OnExecute(object? parameter);

    private Task CreateSlot(object? parameter, CancellationToken ct)
    {
        if (parameter is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot)
        {
            slots.Add(slot);
            Parent?.PushUndo(() => { slots.Remove(slot); });
        }
        return Task.CompletedTask;
    }
    private Task Delete(object? parameter, CancellationToken ct)
    {
        if (Parent is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree tree)
        {
            var affectedLinks = new List<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink>();
            var linkRemovalActions = new List<Action>();

            foreach (var link in tree.Links.ToList())
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
            var nodeRemovalAction = new Action(() =>
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
        return Task.CompletedTask;
    }
    private Task Broadcast(object? parameter, CancellationToken ct)
    {
        var senders = slots.Where(s => s.State == VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Sender).ToArray();
        foreach (var slot in senders)
        {
            foreach (var target in slot.Targets)
            {
                target.Execute(parameter);
            }
        }
        return Task.CompletedTask;
    }
    private Task Execute(object? parameter, CancellationToken ct)
    {
        Execute(parameter);
        return Task.CompletedTask;
    }
    private Task Undo(object? parameter, CancellationToken ct)
    {
        Parent?.UndoCommand.Execute(null);
        return Task.CompletedTask;
    }

    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CreateSlotCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CreateSlotCommand
    {
        get
        {
            _buffer_CreateSlotCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: CreateSlot,
                canExecute: _ => true);
            return _buffer_CreateSlotCommand;
        }
    }
    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_DeleteCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand DeleteCommand
    {
        get
        {
            _buffer_DeleteCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Delete,
                canExecute: _ => true);
            return _buffer_DeleteCommand;
        }
    }
    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_BroadcastCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand BroadcastCommand
    {
        get
        {
            _buffer_BroadcastCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Broadcast,
                canExecute: _ => true);
            return _buffer_BroadcastCommand;
        }
    }
    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ExecuteCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ExecuteCommand
    {
        get
        {
            _buffer_ExecuteCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Execute,
                canExecute: _ => true);
            return _buffer_ExecuteCommand;
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
}
