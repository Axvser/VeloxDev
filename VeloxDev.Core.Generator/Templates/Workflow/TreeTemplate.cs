namespace VeloxDev.Core.Generator.Templates.Workflow;

public static class TreeTemplate
{
    public static string FromTypeConfig(string? slotstring, string? linkstring)
    {
        string slotTypeName = slotstring ?? "global::VeloxDev.Core.WorkflowSystem.SlotContext";
        string linkTypeName = linkstring ?? "global::VeloxDev.Core.WorkflowSystem.LinkContext";

        return $$"""
     private void InitializeWorkflow()
     {
         nodes.CollectionChanged += OnNodesCollectionChanged;
         links.CollectionChanged += OnLinksCollectionChanged;
     }

     private readonly global::System.Collections.Concurrent.ConcurrentStack<global::System.Action> undos = new();

     private global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink virtualLink =
         new {{linkTypeName}}()
         {
             Processor = new {{slotTypeName}}()
         };

     private global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode> nodes = [];
     private global::System.Collections.ObjectModel.ObservableCollection<global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink> links = [];
     private bool isEnabled = true;
     private string uID = string.Empty;
     private string name = string.Empty;

     public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink VirtualLink
     {
         get => virtualLink;
         set
         {
             if (object.Equals(virtualLink, value)) return;
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
             if (object.Equals(nodes, value)) return;
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
             if (object.Equals(links, value)) return;
             var old = links;
             OnPropertyChanging(nameof(Links));
             links = value;
             old.CollectionChanged -= OnLinksCollectionChanged;
             value.CollectionChanged += OnLinksCollectionChanged;
             OnPropertyChanged(nameof(Links));
         }
     }

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

     public bool CanUndo => !undos.IsEmpty;

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

     private void OnLinksCollectionChanged(object? sender,global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
     {
         if (e.NewItems != null)
         {
             foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink link in e.NewItems)
             {
                 OnLinkAdded(link);
             }
         }

         if (e.OldItems != null)
         {
             foreach (global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink link in e.OldItems)
             {
                 OnLinkRemoved(link);
             }
         }
     }
     partial void OnLinkAdded(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink link);
     partial void OnLinkRemoved(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink link);

     private global::System.Threading.Tasks.Task CreateNode(object? parameter, global::System.Threading.CancellationToken ct)
     {
         if (parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node)
         {
             OnNodeCreated(node);
             nodes.Add(node);
             PushUndo(() => nodes.Remove(node));
         }
         return global::System.Threading.Tasks.Task.CompletedTask;
     }
     partial void OnNodeCreated(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node);

     private global::System.Threading.Tasks.Task SetPointer(object? parameter, global::System.Threading.CancellationToken ct)
     {
         if (parameter is global::VeloxDev.Core.WorkflowSystem.Anchor anchor && VirtualLink.Processor != null)
         {
             var old = VirtualLink.Processor.Anchor;
             OnPointerChanging(old, anchor);
             VirtualLink.Processor.Anchor = anchor;
             OnPointerChanged(old, anchor);
         }
         return global::System.Threading.Tasks.Task.CompletedTask;
     }
     partial void OnPointerChanging(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue, global::VeloxDev.Core.WorkflowSystem.Anchor newValue);
     partial void OnPointerChanged(global::VeloxDev.Core.WorkflowSystem.Anchor oldValue, global::VeloxDev.Core.WorkflowSystem.Anchor newValue);

     private global::System.Threading.Tasks.Task SetSender(object? parameter, global::System.Threading.CancellationToken ct)
     {
         if (parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot)
         {
             // 强制状态修复
             if (slot.State == global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.PreviewSender)
             {
                 // 检查是否真实存在虚拟连接
                 if (VirtualLink.Sender != slot)
                 {
                     slot.State = global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
                 }
             }

             if (!slot.Capacity.HasFlag(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Sender))
                 return global::System.Threading.Tasks.Task.CompletedTask;

             // 原子化状态处理
             switch (slot.State)
             {
                 case global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy:
                     // 新连接：确保清理旧连接
                     RemoveAllSlotConnections(slot);
                     slot.State = global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.PreviewSender;
                     VirtualLink.Sender = slot;
                     break;

                 case global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Sender:
                     // 点击已激活发送者：转为就绪态
                     RemoveAllSlotConnections(slot);
                     slot.State = global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
                     VirtualLink.Sender = null;
                     break;

                 case global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Processor:
                     // 处理器转发送者：先清理再转换
                     RemoveAllSlotConnections(slot);
                     slot.State = global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.PreviewSender;
                     VirtualLink.Sender = slot;
                     break;

                 case global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.PreviewSender:
                     // 保持状态等待连接完成
                     break;
             }
         }
         return global::System.Threading.Tasks.Task.CompletedTask;
     }

     private global::System.Threading.Tasks.Task SetProcessor(object? parameter, global::System.Threading.CancellationToken ct)
     {
         if (VirtualLink.Sender is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot sender &&
             parameter is global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot processor)
         {
             try
             {
                 // 验证连接有效性
                 if (sender.Parent == processor.Parent ||
                     !processor.Capacity.HasFlag(global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Processor))
                 {
                     sender.State = global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
                     ResetConnection();
                     return global::System.Threading.Tasks.Task.CompletedTask;
                 }

                 // 清理处理器现有连接
                 RemoveAllSlotConnections(processor);

                 // 检查重复连接
                 var existingLink = global::System.Linq.Enumerable.FirstOrDefault(Links, l =>
                     l.Sender?.Parent == sender.Parent &&
                     l.Processor?.Parent == processor.Parent);

                 if (existingLink != null)
                 {
                     Links.Remove(existingLink);
                     PushUndo(() => Links.Add(existingLink));
                 }

                 // 创建新连接
                 var newLink = new {{linkTypeName}}
                 {
                     Sender = sender,
                     Processor = processor
                 };
                 OnLinkCreated(newLink);
                 Links.Add(newLink);
                 sender.Targets.Add(processor.Parent!);
                 processor.Sources.Add(sender.Parent!);

                 // 更新状态
                 sender.State = global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Sender;
                 processor.State = global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Processor;

                 // 撤销支持
                 PushUndo(() => {
                     Links.Remove(newLink);
                     sender.Targets.Remove(processor.Parent!);
                     processor.Sources.Remove(sender.Parent!);
                     sender.State = global::System.Linq.Enumerable.Any(Links, l => l.Sender == sender)
                         ? global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Sender
                         : global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
                     processor.State = global::System.Linq.Enumerable.Any(Links, l => l.Processor == processor)
                         ? global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Processor
                         : global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
                 });
             }
             finally
             {
                 // 确保最终清除虚拟连接
                 ResetConnection();
             }
         }
         return global::System.Threading.Tasks.Task.CompletedTask;
     }
     partial void OnLinkCreated(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink link);

     private void RemoveAllSlotConnections(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowSlot slot)
     {
         var affectedLinks = global::System.Linq.Enumerable.ToList(
             global::System.Linq.Enumerable.Where(Links, l => l.Sender == slot || l.Processor == slot)
         );
         if (affectedLinks.Count == 0) return;

         var undoActions = new global::System.Collections.Generic.List<global::System.Action>();
         foreach (var link in affectedLinks)
         {
             link.Sender?.Targets.Remove(link.Processor?.Parent!);
             link.Processor?.Sources.Remove(link.Sender?.Parent!);
             Links.Remove(link);

             undoActions.Add(() => {
                 Links.Add(link);
                 link.Sender?.Targets.Add(link.Processor?.Parent!);
                 link.Processor?.Sources.Add(link.Sender?.Parent!);
             });
         }

         PushUndo(() => {
             foreach (var action in undoActions) action();
             slot.State = global::System.Linq.Enumerable.Any(affectedLinks, l => l.Sender == slot)
                 ? global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Sender
                 : global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
         });

         // 立即更新受影响插槽状态
         foreach (var link in affectedLinks)
         {
             if (link.Sender != null && link.Sender != slot)
             {
                 link.Sender.State = global::System.Linq.Enumerable.Any(Links, l => l.Sender == link.Sender)
                     ? global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Sender
                     : global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
             }
             if (link.Processor != null && link.Processor != slot)
             {
                 link.Processor.State = global::System.Linq.Enumerable.Any(Links, l => l.Processor == link.Processor)
                     ? global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Processor
                     : global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
             }
         }
     }

     private void ResetConnection()
     {
         if (VirtualLink.Sender != null)
         {
             VirtualLink.Sender.State = global::System.Linq.Enumerable.Any(Links, l => l.Sender == VirtualLink.Sender)
                 ? global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.Sender
                 : global::VeloxDev.Core.Interfaces.WorkflowSystem.SlotState.StandBy;
             VirtualLink.Sender = null;
         }
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

     private async global::System.Threading.Tasks.Task ResetState(object? parameter,
         global::System.Threading.CancellationToken ct)
     {
         foreach (var node in Nodes)
         {
             OnNodeReseting(node);
             node.WorkCommand.Lock();
             node.BroadcastCommand.Lock();
         }

         foreach (var node in Nodes)
         {
             await node.WorkCommand.InterruptAsync();
             await node.BroadcastCommand.InterruptAsync();
             node.WorkCommand.UnLock();
             node.BroadcastCommand.UnLock();
             OnNodeReseted(node);
         }

         foreach (var link in Links)
         {
             OnLinkReseted(link);
         }
     }
     partial void OnNodeReseting(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node);
     partial void OnNodeReseted(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode node);
     partial void OnLinkReseted(global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink link);

     public global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowLink? FindLink(
         global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode sender,
         global::VeloxDev.Core.Interfaces.WorkflowSystem.IWorkflowNode processor)
     {
         return global::System.Linq.Enumerable.FirstOrDefault(Links, link =>
             link.Sender?.Parent == sender &&
             link.Processor?.Parent == processor);
     }

     private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_CreateNodeCommand;
     public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand CreateNodeCommand =>
         _buffer_CreateNodeCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
             executeAsync: CreateNode,
             canExecute: _ => true);

     private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_SetPointerCommand;
     public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand SetPointerCommand =>
         _buffer_SetPointerCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
             executeAsync: SetPointer,
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

     private global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand? _buffer_ResetStateCommand = null;
     public global::VeloxDev.Core.Interfaces.MVVM.IVeloxCommand ResetStateCommand
     {
         get
         {
             _buffer_ResetStateCommand ??= new global::VeloxDev.Core.MVVM.VeloxCommand(
                 executeAsync: ResetState,
                 canExecute: _ => true);
             return _buffer_ResetStateCommand;
         }
     }
""";
    }
}
