namespace VeloxDev.Core.Generator.Templates.Workflow;

public static class TreeTemplate
{
    public const string Normal = $$"""
     private void InitializeWorkflow()
     {
         nodes.CollectionChanged += OnNodesCollectionChanged;
     }

     private global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot? actualSender = null;

     private readonly global::System.Collections.Concurrent.ConcurrentStack<Action> undos = [];

     private global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink virtualLink = new global::VeloxDev.Core.WorkflowSystem.LinkContext() { Processor = new global::VeloxDev.Core.WorkflowSystem.SlotContext() };
     private global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> nodes = [];
     private global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> links = [];
     public bool isEnabled = true;
     public string uID = string.Empty;
     public string name = string.Empty;

     public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink VirtualLink
     {
         get => virtualLink;
         set
         {
             if (object.Equals(virtualLink, value)) return;
             var old = virtualLink;
             OnPropertyChanging(nameof(VirtualLink));
             OnVirtualLinkChanging(old, value);
             virtualLink = value;
             OnVirtualLinkChanged(old, value);
             OnPropertyChanged(nameof(VirtualLink));
         }
     }
     partial void OnVirtualLinkChanging(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink newValue);
     partial void OnVirtualLinkChanged(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink oldValue, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink newValue);
     public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> Nodes
     {
         get => nodes;
         set
         {
             if (object.Equals(nodes, value)) return;
             var old = nodes;
             OnPropertyChanging(nameof(Nodes));
             OnNodesChanging(old, value);
             nodes = value;
             old.CollectionChanged -= OnNodesCollectionChanged;
             value.CollectionChanged += OnNodesCollectionChanged;
             foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node in value)
             {
                 node.Parent = this;
             }
             OnNodesChanged(old, value);
             OnPropertyChanged(nameof(Nodes));
         }
     }
     partial void OnNodesChanging(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
     partial void OnNodesChanged(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> newValue);
     public global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> Links
     {
         get => links;
         set
         {
             if (object.Equals(links, value)) return;
             var old = links;
             OnPropertyChanging(nameof(Links));
             OnLinksChanging(old, value);
             links = value;
             OnLinksChanged(old, value);
             OnPropertyChanged(nameof(Links));
         }
     }
     partial void OnLinksChanging(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> newValue);
     partial void OnLinksChanged(global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> oldValue, global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> newValue);
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

     private void OnNodesCollectionChanged(object? sender, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
     {
         if (e.NewItems != null)
         {
             foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node in e.NewItems)
             {
                 node.Parent = this;
                 OnNodeAdded(node);
             }
         }
         if (e.OldItems != null)
         {
             foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node in e.OldItems)
             {
                 node.Parent = null;
                 OnNodeRemoved(node);
             }
         }
     }
     partial void OnNodeAdded(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node);
     partial void OnNodeRemoved(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node);

     private global::System.Threading.Tasks.Task CreateNode(object? parameter, global::System.Threading.CancellationToken ct)
     {
         if (parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node)
         {
             nodes.Add(node);
             PushUndo(() => { nodes.Remove(node); });
         }
         return global::System.Threading.Tasks.Task.CompletedTask;
     }
     private global::System.Threading.Tasks.Task SetMouse(object? parameter, global::System.Threading.CancellationToken ct)
     {
         if (parameter is global::VeloxDev.Core.WorkflowSystem.Anchor anchor && VirtualLink.Processor is not null)
         {
             VirtualLink.Processor.Anchor = anchor;
         }
         return global::System.Threading.Tasks.Task.CompletedTask;
     }
     private global::System.Threading.Tasks.Task SetSender(object? parameter, global::System.Threading.CancellationToken ct)
     {
         if (parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot)
         {
             actualSender = slot;
             VirtualLink.Sender = slot;
             VirtualLink.IsEnabled = true;
         }
         return global::System.Threading.Tasks.Task.CompletedTask;
     }
     private global::System.Threading.Tasks.Task SetProcessor(object? parameter, global::System.Threading.CancellationToken ct)
     {
         if (parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot && actualSender != null)
         {
             // 检查是否允许连接
             if (actualSender.Capacity.HasFlag(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Sender) &&
                 slot.Capacity.HasFlag(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Processor))
             {
                 // 创建新连接
                 var newLink = new global::VeloxDev.Core.WorkflowSystem.LinkContext
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
         return global::System.Threading.Tasks.Task.CompletedTask;
     }
     private global::System.Threading.Tasks.Task Undo(object? parameter, global::System.Threading.CancellationToken ct)
     {
         if (undos.TryPop(out var recipient))
         {
             recipient.Invoke();
         }
         return global::System.Threading.Tasks.Task.CompletedTask;
     }

     private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CreateNodeCommand = null;
     public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CreateNodeCommand
     {
         get
         {
             _buffer_CreateNodeCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                 executeAsync: CreateNode,
                 canExecute: _ => true);
             return _buffer_CreateNodeCommand;
         }
     }
     private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetMouseCommand = null;
     public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetMouseCommand
     {
         get
         {
             _buffer_SetMouseCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                 executeAsync: SetMouse,
                 canExecute: _ => true);
             return _buffer_SetMouseCommand;
         }
     }
     private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetSenderCommand = null;
     public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetSenderCommand
     {
         get
         {
             _buffer_SetSenderCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                 executeAsync: SetSender,
                 canExecute: _ => true);
             return _buffer_SetSenderCommand;
         }
     }
     private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetProcessorCommand = null;
     public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetProcessorCommand
     {
         get
         {
             _buffer_SetProcessorCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                 executeAsync: SetProcessor,
                 canExecute: _ => true);
             return _buffer_SetProcessorCommand;
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

     public void PushUndo(Action undo)
     {
         undos.Push(undo);
     }
     public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink? FindLink(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode sender, global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode processor)
     {
         return Links.FirstOrDefault(link =>
             link.Sender?.Parent == sender &&
             link.Processor?.Parent == processor);
     }
 """;
}
