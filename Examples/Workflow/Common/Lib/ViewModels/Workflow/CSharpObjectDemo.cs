using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.CSharp;

namespace Demo.ViewModels;

public static class CSharpObjectDemo
{
    private const double NodeWidth = 320;
    private const double NodeHeight = 530;
    private const double HorizontalGap = 70;

    public static IReadOnlyList<CSharpObject> AddPipeline(
        IWorkflowTreeViewModel tree,
        double left = 180,
        double top = 1220)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var nodes = CreatePipeline(left, top);
        foreach (var node in nodes)
        {
            tree.GetHelper().CreateNode(node);
        }

        for (var index = 0; index < nodes.Count - 1; index++)
        {
            Connect(tree, nodes[index], nodes[index + 1]);
        }

        return nodes;
    }

    public static bool RunLatestPipeline(IWorkflowTreeViewModel tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var start = tree.Nodes
            .OfType<CSharpObject>()
            .LastOrDefault(node =>
                node.SelectedMethodMember?.Role
                    == CSharpObjectMethodRole.Start);
        if (start is null) return false;

        start.WorkCommand.Execute(null);
        return true;
    }

    public static IReadOnlyList<CSharpObject> AddNextPipeline(
        IWorkflowTreeViewModel tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var pipelineCount =
            (tree.Nodes.OfType<CSharpObject>().Count() + 3) / 4;
        return AddPipeline(
            tree,
            left: 180,
            top: 1220 + (pipelineCount * 620));
    }

    private static IReadOnlyList<CSharpObject> CreatePipeline(
        double left,
        double top)
    {
        var offset = NodeWidth + HorizontalGap;
        var start = CreateNode<CSharpScriptStartHost>(
            left,
            top,
            nameof(CSharpScriptStartHost.Start));
        SetValue(start, nameof(CSharpScriptStartHost.Scenario), "Order import");
        SetValue(start, nameof(CSharpScriptStartHost.Seed), "17");
        SetValue(start, nameof(CSharpScriptStartHost.ItemCount), "6");
        SetCollection(
            start,
            nameof(CSharpScriptStartHost.Sources),
            "web",
            "mobile",
            "partner");

        var enrich = CreateNode<CSharpScriptEnrichmentHost>(
            left + offset,
            top,
            nameof(CSharpScriptEnrichmentHost.EnrichAsync));
        SetValue(enrich, nameof(CSharpScriptEnrichmentHost.Prefix), "VEL");
        SetValue(enrich, nameof(CSharpScriptEnrichmentHost.DelayMilliseconds), "260");
        SetCollection(
            enrich,
            nameof(CSharpScriptEnrichmentHost.Tags),
            "reflection",
            "async",
            "configured");

        var score = CreateNode<CSharpScriptScoringHost>(
            left + (offset * 2),
            top,
            nameof(CSharpScriptScoringHost.Score));
        SetValue(score, nameof(CSharpScriptScoringHost.Multiplier), "1.8");
        SetValue(score, nameof(CSharpScriptScoringHost.Bonus), "12");
        SetValue(score, nameof(CSharpScriptScoringHost.PassScore), "60");

        var terminal = CreateNode<CSharpScriptTerminalHost>(
            left + (offset * 3),
            top,
            nameof(CSharpScriptTerminalHost.CompleteAsync));
        SetValue(terminal, nameof(CSharpScriptTerminalHost.Destination), "demo://completed");
        SetValue(terminal, nameof(CSharpScriptTerminalHost.DelayMilliseconds), "180");

        return [start, enrich, score, terminal];
    }

    private static CSharpObject CreateNode<THost>(
        double left,
        double top,
        string methodName)
        where THost : class
    {
        var node = new CSharpObject
        {
            HostType = typeof(THost).FullName!,
            Size = new Size(NodeWidth, NodeHeight),
            Anchor = new Anchor(left, top, 0)
        };

        var method = node.Methods.FirstOrDefault(candidate =>
            candidate.Name == methodName);
        if (method is null)
        {
            throw new InvalidOperationException(
                $"Unable to discover '{typeof(THost).FullName}.{methodName}'.");
        }

        node.SelectedMethod = method.Signature;
        return node;
    }

    private static void SetCollection(
        CSharpObject node,
        string path,
        params string[] values)
    {
        var member = node.Collections.FirstOrDefault(candidate =>
            candidate.Path == path);
        if (member is null) return;

        member.Items.Clear();
        for (var index = 0; index < values.Length; index++)
        {
            member.Items.Add(new CollectionItem
            {
                Index = index,
                Value = values[index]
            });
        }
    }

    private static void SetValue(
        CSharpObject node,
        string path,
        string value)
    {
        var member = node.Values.FirstOrDefault(candidate =>
            candidate.Path == path);
        if (member is not null) member.Value = value;
    }

    private static void Connect(
        IWorkflowTreeViewModel tree,
        CSharpObject sender,
        CSharpObject receiver)
    {
        tree.GetHelper().SendConnection(sender.OutputSlot!);
        tree.GetHelper().ReceiveConnection(receiver.InputSlot!);
    }
}
