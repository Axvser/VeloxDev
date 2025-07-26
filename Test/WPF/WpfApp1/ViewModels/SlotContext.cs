namespace WpfApp1.ViewModels;

public sealed partial class SlotContext : VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot
{
    private System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> targets = [];
    private System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> sources = [];
    private VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? parent = null;
    private VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity capacity = VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Universal;
    private VeloxDev.Core.Interfaces.WorkflowSystem.SlotState state = VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
    private VeloxDev.Core.WorkflowSystem.Anchor anchor = new();
    private VeloxDev.Core.WorkflowSystem.Anchor offset = new();
    private VeloxDev.Core.WorkflowSystem.Size size = new();
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

    public System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> Targets
    {
        get => targets;
        set
        {
            if (Equals(targets, value)) return;
            var old = targets;
            OnPropertyChanging(nameof(Targets));
            OnTargetsChanging(old, value);
            targets = value;
            OnTargetsChanged(old, value);
            OnPropertyChanged(nameof(Targets));
        }
    }
    partial void OnTargetsChanging(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    partial void OnTargetsChanged(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    public System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> Sources
    {
        get => sources;
        set
        {
            if (Equals(sources, value)) return;
            var old = sources;
            OnPropertyChanging(nameof(Sources));
            OnSourcesChanging(old, value);
            sources = value;
            OnSourcesChanged(old, value);
            OnPropertyChanged(nameof(Sources));
        }
    }
    partial void OnSourcesChanging(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    partial void OnSourcesChanged(System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, System.Collections.ObjectModel.ObservableCollection<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    public VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? Parent
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
    partial void OnParentChanging(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? newValue);
    partial void OnParentChanged(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? newValue);
    public VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity Capacity
    {
        get => capacity;
        set
        {
            if (Equals(capacity, value)) return;
            var old = capacity;
            OnPropertyChanging(nameof(Capacity));
            OnCapacityChanging(old, value);
            capacity = value;
            OnCapacityChanged(old, value);
            OnPropertyChanged(nameof(Capacity));
        }
    }
    partial void OnCapacityChanging(VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity newValue);
    partial void OnCapacityChanged(VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity newValue);
    public VeloxDev.Core.Interfaces.WorkflowSystem.SlotState State
    {
        get => state;
        set
        {
            if (Equals(state, value)) return;
            var old = state;
            OnPropertyChanging(nameof(State));
            OnStateChanging(old, value);
            state = value;
            OnStateChanged(old, value);
            OnPropertyChanged(nameof(State));
        }
    }
    partial void OnStateChanging(VeloxDev.Core.Interfaces.WorkflowSystem.SlotState oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.SlotState newValue);
    partial void OnStateChanged(VeloxDev.Core.Interfaces.WorkflowSystem.SlotState oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.SlotState newValue);
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
            OnAnchorChanged(old, value);
            OnPropertyChanged(nameof(Anchor));
        }
    }
    partial void OnAnchorChanging(VeloxDev.Core.WorkflowSystem.Anchor oldValue, VeloxDev.Core.WorkflowSystem.Anchor newValue);
    partial void OnAnchorChanged(VeloxDev.Core.WorkflowSystem.Anchor oldValue, VeloxDev.Core.WorkflowSystem.Anchor newValue);
    public VeloxDev.Core.WorkflowSystem.Anchor Offset
    {
        get => offset;
        set
        {
            if (Equals(offset, value)) return;
            var old = offset;
            OnPropertyChanging(nameof(Offset));
            OnOffsetChanging(old, value);
            offset = value;
            OnOffsetChanged(old, value);
            OnPropertyChanged(nameof(Offset));
        }
    }
    partial void OnOffsetChanging(VeloxDev.Core.WorkflowSystem.Anchor oldValue, VeloxDev.Core.WorkflowSystem.Anchor newValue);
    partial void OnOffsetChanged(VeloxDev.Core.WorkflowSystem.Anchor oldValue, VeloxDev.Core.WorkflowSystem.Anchor newValue);
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

    private Task Delete(object? parameter, CancellationToken ct)
    {
        if (Parent?.Parent is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree tree)
        {
            List<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> removed_targets = [];
            List<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> removed_sources = [];
            List<VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> removed_links = [];
            foreach (var target in Targets)
            {
                var link = tree.FindLink(Parent, target);
                if (link != null)
                {
                    tree.Links.Remove(link);
                    removed_links.Add(link);
                    removed_targets.Add(target);
                }
            }
            foreach (var target in removed_targets)
            {
                Targets.Remove(target);
            }
            foreach (var source in Sources)
            {
                var link = tree.FindLink(source, Parent);
                if (link != null)
                {
                    tree.Links.Remove(link);
                    removed_links.Add(link);
                    removed_sources.Add(source);
                }
            }
            foreach (var source in removed_sources)
            {
                Sources.Remove(source);
            }
            tree.PushUndo(() =>
            {
                foreach (var rm in removed_sources)
                {
                    Sources.Add(rm);
                }
                foreach (var rm in removed_targets)
                {
                    Targets.Add(rm);
                }
                foreach (var rm in removed_links)
                {
                    tree.Links.Add(rm);
                }
            });
        }
        return Task.CompletedTask;
    }
    private Task Connecting(object? parameter, CancellationToken ct)
    {
        if (Parent?.Parent is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree tree)
        {
            tree.SetSenderCommand.Execute(parameter);
        }
        return Task.CompletedTask;
    }
    private Task Connected(object? parameter, CancellationToken ct)
    {
        if (Parent?.Parent is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree tree)
        {
            tree.SetProcessorCommand.Execute(parameter);
        }
        return Task.CompletedTask;
    }
    private Task Undo(object? parameter, CancellationToken ct)
    {
        Parent?.Parent?.UndoCommand?.Execute(null);
        return Task.CompletedTask;
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
    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ConnectingCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ConnectingCommand
    {
        get
        {
            _buffer_ConnectingCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Connecting,
                canExecute: _ => true);
            return _buffer_ConnectingCommand;
        }
    }
    private VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ConnectedCommand = null;
    public VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ConnectedCommand
    {
        get
        {
            _buffer_ConnectedCommand ??= new VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Connected,
                canExecute: _ => true);
            return _buffer_ConnectedCommand;
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
