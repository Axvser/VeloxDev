using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.Layout;

/// <summary>Builds small trees and solves them without touching any adapter or view.</summary>
internal static class LayoutTestKit
{
    public static NodeDefaultComponent AddNode(
        TreeDefaultComponent tree, double x, double y, double width = 100d, double height = 40d)
    {
        var node = new NodeDefaultComponent
        {
            Parent = tree,
            Anchor = new Anchor(x, y, 0),
            Size = new Size(width, height),
        };
        tree.Nodes.Add(node);
        return node;
    }

    public static LinkDefaultComponent AddLink(
        TreeDefaultComponent tree, INodeComponent sender, INodeComponent receiver)
    {
        var link = new LinkDefaultComponent
        {
            Sender = new SlotDefaultComponent { Parent = sender },
            Receiver = new SlotDefaultComponent { Parent = receiver },
        };
        tree.Links.Add(link);
        return link;
    }

    public static WorkflowLayoutSolution Solve(
        TreeDefaultComponent tree,
        out WorkflowLayoutProblem problem,
        WorkflowLayoutOptions? options = null,
        bool parallel = false)
    {
        options ??= new WorkflowLayoutOptions();
        problem = WorkflowLayoutProblemBuilder.Build(tree, options);
        var pipeline = new WorkflowLayoutPipeline();
        return pipeline.Solve(problem, options, new WorkflowLayoutContext(enableParallelism: parallel, seed: 7));
    }

    public static WorkflowLayoutResult Apply(
        TreeDefaultComponent tree, WorkflowLayoutProblem problem, WorkflowLayoutSolution solution)
        => new WorkflowLayoutApplier(tree).Apply(problem, solution);
}
