using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.StandardEx;

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
            dic.TryGetValue(component.Receiver, out var link) &&
            link == component)
        {
            tree.GetHelper().Submit(new WorkflowActionPair(
                () => RemoveLinkFromTree(tree, component),
                () => RestoreLinkToTree(tree, component)));
        }
    }

    internal static void RemoveLinkFromTree(IWorkflowTreeViewModel tree, IWorkflowLinkViewModel component)
    {
        var sender = component.Sender;
        var receiver = component.Receiver;
        sender.Targets.Remove(receiver);
        receiver.Sources.Remove(sender);
        if (tree.LinksMap.TryGetValue(sender, out var receivers))
        {
            receivers.Remove(receiver);
            if (receivers.Count == 0) tree.LinksMap.Remove(sender);
        }
        tree.Links.Remove(component);
        component.IsVisible = false;
        sender.GetHelper().UpdateState();
        receiver.GetHelper().UpdateState();
    }

    internal static void RestoreLinkToTree(IWorkflowTreeViewModel tree, IWorkflowLinkViewModel component)
    {
        var sender = component.Sender;
        var receiver = component.Receiver;
        if (!tree.LinksMap.ContainsKey(sender)) tree.LinksMap[sender] = [];
        tree.LinksMap[sender][receiver] = component;
        if (!tree.Links.Contains(component)) tree.Links.Add(component);
        if (!sender.Targets.Contains(receiver)) sender.Targets.Add(receiver);
        if (!receiver.Sources.Contains(sender)) receiver.Sources.Add(sender);
        component.IsVisible = true;
        sender.GetHelper().UpdateState();
        receiver.GetHelper().UpdateState();
    }
}
