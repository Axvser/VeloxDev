namespace WpfApp1.ViewModels;

public sealed partial class LinkContext : VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink
{
    private VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? sender = null;
    private VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? processor = null;
    public bool isEnabled = false;
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

    public VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? Sender
    {
        get => sender;
        set
        {
            if (Equals(sender, value)) return;
            var old = sender;
            OnPropertyChanging(nameof(Sender));
            OnSenderChanging(old, value);
            sender = value;
            IsEnabled = value != null && Processor != null;
            OnSenderChanged(old, value);
            OnPropertyChanged(nameof(Sender));
        }
    }
    partial void OnSenderChanging(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? newValue);
    partial void OnSenderChanged(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? newValue);
    public VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? Processor
    {
        get => processor;
        set
        {
            if (Equals(processor, value)) return;
            var old = processor;
            OnPropertyChanging(nameof(Processor));
            OnProcessorChanging(old, value);
            processor = value;
            IsEnabled = Sender != null && value != null;
            OnProcessorChanged(old, value);
            OnPropertyChanged(nameof(Processor));
        }
    }
    partial void OnProcessorChanging(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? newValue);
    partial void OnProcessorChanged(VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? oldValue, VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? newValue);
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
        if (Sender is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot sender &&
            Sender.Parent is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode s_node &&
            Processor is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot processor &&
            Processor.Parent is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode p_node &&
            Sender.Parent.Parent is VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowTree tree)
        {
            var rm = tree.FindLink(s_node, p_node);
            if (rm != null)
            {
                tree.Links.Remove(rm);
                sender.Targets.Remove(p_node);
                processor.Sources.Remove(s_node);
                tree.PushUndo(() =>
                {
                    processor.Sources.Add(s_node);
                    sender.Targets.Add(p_node);
                    tree.Links.Add(rm);
                });
            }
        }
        return Task.CompletedTask;
    }
    private Task Undo(object? parameter, CancellationToken ct)
    {
        Sender?.Parent?.Parent?.UndoCommand?.Execute(null);
        return Task.CompletedTask;
    }
}
