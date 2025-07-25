using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
    public sealed partial class LinkContext : IWorkflowLink
    {
        [VeloxProperty]
        private IWorkflowSlot? sender = null;
        [VeloxProperty]
        private IWorkflowSlot? processor = null;
        [VeloxProperty]
        public bool isEnabled = false;
        [VeloxProperty]
        public string uID = string.Empty;
        [VeloxProperty]
        public string name = string.Empty;

        partial void OnSenderChanged(IWorkflowSlot oldValue, IWorkflowSlot newValue)
        {
            IsEnabled = newValue != null && Processor != null;
        }
        partial void OnProcessorChanged(IWorkflowSlot oldValue, IWorkflowSlot newValue)
        {
            IsEnabled = Sender != null && newValue != null;
        }

        [VeloxCommand]
        private Task Delete(object? parameter, CancellationToken ct)
        {
            if (Sender is IWorkflowSlot sender &&
                Sender.Parent is IWorkflowNode s_node &&
                Processor is IWorkflowSlot processor &&
                Processor.Parent is IWorkflowNode p_node &&
                Sender.Parent.Parent is IWorkflowTree tree)
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
        [VeloxCommand]
        private Task Undo(object? parameter, CancellationToken ct)
        {
            Sender?.Parent?.Parent?.UndoCommand?.Execute(null);
            return Task.CompletedTask;
        }
    }
}
