namespace VeloxDev.Core.Generator.Templates.Workflow;

public static class SlotTemplate
{
    public const string Normal = $$"""
    private global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> targets = [];
    private global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> sources = [];
    private global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? parent = null;
    private global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity capacity = global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Universal;
    private global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState state = global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
    private global::VeloxDev.Core.WorkflowSystem.Anchor anchor = new();
    private global::VeloxDev.Core.WorkflowSystem.Anchor offset = new();
    private global::VeloxDev.Core.WorkflowSystem.Size size = new();
    private bool isEnabled = true;
    private string uID = string.Empty;
    private string name = string.Empty;

    public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> Targets
    {
        get => targets;
        set
        {
            if (object.Equals(targets, value)) return;
            var old = targets;
            OnPropertyChanging(nameof(Targets));
            OnTargetsChanging(old, value);
            targets = value;
            OnTargetsChanged(old, value);
            OnPropertyChanged(nameof(Targets));
        }
    }
    partial void OnTargetsChanging(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    partial void OnTargetsChanged(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> Sources
    {
        get => sources;
        set
        {
            if (object.Equals(sources, value)) return;
            var old = sources;
            OnPropertyChanging(nameof(Sources));
            OnSourcesChanging(old, value);
            sources = value;
            OnSourcesChanged(old, value);
            OnPropertyChanged(nameof(Sources));
        }
    }
    partial void OnSourcesChanging(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    partial void OnSourcesChanged(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
    public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? Parent
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
    partial void OnParentChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? newValue);
    partial void OnParentChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode? newValue);
    public global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity Capacity
    {
        get => capacity;
        set
        {
            if (object.Equals(capacity, value)) return;
            var old = capacity;
            OnPropertyChanging(nameof(Capacity));
            OnCapacityChanging(old, value);
            capacity = value;
            OnCapacityChanged(old, value);
            OnPropertyChanged(nameof(Capacity));
        }
    }
    partial void OnCapacityChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity newValue);
    partial void OnCapacityChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity newValue);
    public global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState State
    {
        get => state;
        set
        {
            if (object.Equals(state, value)) return;
            var old = state;
            OnPropertyChanging(nameof(State));
            OnStateChanging(old, value);
            state = value;
            OnStateChanged(old, value);
            OnPropertyChanged(nameof(State));
        }
    }
    partial void OnStateChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState newValue);
    partial void OnStateChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState newValue);
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
            OnAnchorChanged(old, value);
            OnPropertyChanged(nameof(Anchor));
        }
    }
    partial void OnAnchorChanging(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue, global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
    partial void OnAnchorChanged(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue, global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
    public global::VeloxDev.Core.WorkflowSystem.Anchor Offset
    {
        get => offset;
        set
        {
            if (object.Equals(offset, value)) return;
            var old = offset;
            OnPropertyChanging(nameof(Offset));
            OnOffsetChanging(old, value);
            offset = value;
            OnOffsetChanged(old, value);
            OnPropertyChanged(nameof(Offset));
        }
    }
    partial void OnOffsetChanging(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue, global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
    partial void OnOffsetChanged(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue, global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
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

    private global::System.Threading.Tasks.Task Delete(object? parameter, global::System.Threading.CancellationToken ct)
    {
        if (Parent?.Parent is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree tree)
        {
            List<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> removed_targets = [];
            List<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> removed_sources = [];
            List<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> removed_links = [];
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
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
    private global::System.Threading.Tasks.Task Connecting(object? parameter, global::System.Threading.CancellationToken ct)
    {
        if (Parent?.Parent is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree tree)
        {
            tree.SetSenderCommand.Execute(this);
        }
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
    private global::System.Threading.Tasks.Task Connected(object? parameter, global::System.Threading.CancellationToken ct)
    {
        if (Parent?.Parent is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree tree)
        {
            tree.SetProcessorCommand.Execute(this);
        }
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
    private global::System.Threading.Tasks.Task Undo(object? parameter, global::System.Threading.CancellationToken ct)
    {
        Parent?.Parent?.UndoCommand?.Execute(null);
        return global::System.Threading.Tasks.Task.CompletedTask;
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
    private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ConnectingCommand = null;
    public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ConnectingCommand
    {
        get
        {
            _buffer_ConnectingCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Connecting,
                canExecute: _ => true);
            return _buffer_ConnectingCommand;
        }
    }
    private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ConnectedCommand = null;
    public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ConnectedCommand
    {
        get
        {
            _buffer_ConnectedCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                executeAsync: Connected,
                canExecute: _ => true);
            return _buffer_ConnectedCommand;
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
