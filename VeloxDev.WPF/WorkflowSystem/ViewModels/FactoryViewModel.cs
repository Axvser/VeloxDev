using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.ViewModels
{
    public partial class FactoryViewModel : IWorkflowTree
    {
        public FactoryViewModel()
        {
            nodes.CollectionChanged += OnNodesCollectionChanged;
        }

        private IWorkflowSlot? actualSender = null;
        private IWorkflowSlot? actualProcessor = null;

        private readonly ConcurrentStack<Action> undos = [];

        [VeloxProperty]
        private IWorkflowLink virtualLink = new LinkContext() { Processor = new SlotContext() };
        [VeloxProperty]
        private ObservableCollection<IWorkflowNode> nodes = [];
        [VeloxProperty]
        private ObservableCollection<IWorkflowLink> links = [];
        [VeloxProperty]
        public bool isEnabled = true;
        [VeloxProperty]
        public string uID = string.Empty;
        [VeloxProperty]
        public string name = string.Empty;

        partial void OnNodesChanged(ObservableCollection<IWorkflowNode> oldValue, ObservableCollection<IWorkflowNode> newValue)
        {
            oldValue.CollectionChanged -= OnNodesCollectionChanged;
            newValue.CollectionChanged += OnNodesCollectionChanged;
            foreach (IWorkflowNode node in newValue)
            {
                node.Parent = this;
            }
        }
        private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (IWorkflowNode node in e.NewItems)
                {
                    node.Parent = this;
                }
            }
            if (e.OldItems != null)
            {
                foreach (IWorkflowNode node in e.OldItems)
                {
                    node.Parent = null;
                }
            }
        }

        [VeloxCommand]
        private Task CreateNode(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowNode node)
            {
                nodes.Add(node);
                PushUndo(() => { nodes.Remove(node); });
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task SetMouse(object? parameter, CancellationToken ct)
        {
            if (parameter is Anchor anchor && VirtualLink.Processor is not null)
            {
                VirtualLink.Processor.Anchor = anchor;
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task SetSender(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowSlot slot)
            {
                actualSender = slot;
                VirtualLink.Sender = slot;
                VirtualLink.IsEnabled = true;
            }
            return Task.CompletedTask;
        }
        [VeloxCommand]
        private Task SetProcessor(object? parameter, CancellationToken ct)
        {
            if (parameter is IWorkflowSlot slot && actualSender != null)
            {
                // 检查是否允许连接
                if (actualSender.Capacity.HasFlag(SlotCapacity.Sender) &&
                    slot.Capacity.HasFlag(SlotCapacity.Processor))
                {
                    // 创建新连接
                    var newLink = new LinkContext
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
        [VeloxCommand]
        private Task Undo(object? parameter, CancellationToken ct)
        {
            if (undos.TryPop(out var recipient))
            {
                recipient.Invoke();
            }
            return Task.CompletedTask;
        }

        public void PushUndo(Action undo)
        {
            undos.Push(undo);
        }

        public IWorkflowLink? FindLink(IWorkflowNode sender, IWorkflowNode processor)
        {
            return Links.FirstOrDefault(link =>
                link.Sender?.Parent == sender &&
                link.Processor?.Parent == processor);
        }
    }
}
