using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.Layout;

/// <summary>Builds small trees and solves them without touching any adapter or view.</summary>
internal static class LayoutTestKit
{
    public static NodeDefaultViewModel AddNode(
        TreeDefaultViewModel tree, double x, double y, double width = 100d, double height = 40d)
    {
        var node = new NodeDefaultViewModel
        {
            Parent = tree,
            Anchor = new Anchor(x, y, 0),
            Size = new Size(width, height),
        };
        tree.Nodes.Add(node);
        return node;
    }

    public static LinkDefaultViewModel AddLink(
        TreeDefaultViewModel tree, IWorkflowNodeViewModel sender, IWorkflowNodeViewModel receiver)
    {
        var link = new LinkDefaultViewModel
        {
            Sender = new SlotDefaultViewModel { Parent = sender },
            Receiver = new SlotDefaultViewModel { Parent = receiver },
        };
        tree.Links.Add(link);
        return link;
    }

    public static WorkflowLayoutSolution Solve(
        TreeDefaultViewModel tree,
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
        TreeDefaultViewModel tree, WorkflowLayoutProblem problem, WorkflowLayoutSolution solution)
        => new WorkflowLayoutApplier(tree).Apply(problem, solution);
}
