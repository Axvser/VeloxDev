using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.StandardEx;

public static class WorkflowLinkEx
{
    public static IReadOnlyCollection<IVeloxCommand> GetStandardCommands
        (this IWorkflowLinkViewModel component)
        => 
        [
            component.DeleteCommand
        ];
    
    public static void StandardDelete(this IWorkflowLinkViewModel component)
    {
        if (component.Sender?.Parent?.Parent is null) return;
        var tree = component.Sender.Parent.Parent;

        if (tree.LinksMap.TryGetValue(component.Sender, out var dic) &&
            dic.TryGetValue(component.Receiver, out var link))
        {
            if (link == component)
            {
                tree.GetHelper().Submit(new WorkflowActionPair(
                    () =>
                    {
                        component.Sender.Targets.Remove(component.Receiver);
                        component.Receiver.Sources.Remove(component.Sender);
                        tree.LinksMap[component.Sender].Remove(component.Receiver);
                        tree.Links.Remove(component);
                        component.IsVisible = false;
                        component.Sender.GetHelper().UpdateState();
                        component.Receiver.GetHelper().UpdateState();
                    },
                    () =>
                    {
                        component.Sender.Targets.Add(component.Receiver);
                        component.Receiver.Sources.Add(component.Sender);
                        tree.LinksMap[component.Sender].Add(component.Receiver, component);
                        tree.Links.Add(component);
                        component.IsVisible = true;
                        component.Sender.GetHelper().UpdateState();
                        component.Receiver.GetHelper().UpdateState();
                    }));
            }
        }
    }
}
