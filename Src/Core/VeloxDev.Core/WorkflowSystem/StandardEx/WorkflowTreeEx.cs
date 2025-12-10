using System.ComponentModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem.Templates;

namespace VeloxDev.Core.WorkflowSystem.StandardEx;

public static class WorkflowTreeEx
{
    public static IReadOnlyCollection<IVeloxCommand> GetStandardCommands
        (this IWorkflowTreeViewModel component)
        =>
        [
            component.CreateNodeCommand,
            component.SetPointerCommand,
            component.ResetVirtualLinkCommand,
            component.ApplyConnectionCommand,
            component.ReceiveConnectionCommand,
            component.SubmitCommand,
            component.RedoCommand,
            component.UndoCommand
        ];

    public static void StandardCreateNode(this IWorkflowTreeViewModel component,IWorkflowNodeViewModel node)
    {
        var oldParent = node.Parent;
        var newParent = component;
        node.GetHelper().Delete();
        component.GetHelper().Submit(new WorkflowActionPair(
            () =>
            {
                node.Parent = newParent;
                component.Nodes.Add(node);
            },
            () =>
            {
                node.GetHelper().Delete();
                node.Parent = oldParent;
                component.Nodes.Remove(node);
            }));
    }
    
    public static void StandardSetPointer(this IWorkflowTreeViewModel component,Anchor anchor)
    {
        component.VirtualLink.Receiver.Anchor = anchor;
        component.VirtualLink.OnPropertyChanged(nameof(component.VirtualLink.Receiver));
        component.OnPropertyChanged(nameof(component.VirtualLink));
    }
}
