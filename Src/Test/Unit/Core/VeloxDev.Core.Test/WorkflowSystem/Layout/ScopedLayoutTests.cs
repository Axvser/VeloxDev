using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.Layout;

/// <summary>
/// A layout can be scoped to the subgraph reachable from one node. The selected nodes may move; everything
/// else remains a fixed obstacle and is never written. The start node stays fixed by default.
/// </summary>
[TestClass]
public class ScopedLayoutTests
{
    private static WorkflowLayoutOptions Options(IWorkflowNodeViewModel start, WorkflowLayoutTraversal traversal)
        => new()
        {
            StartNode = start,
            Traversal = traversal,
            PinStartNode = true,
            PositionTolerance = 0,
            RowGap = 8,
            ColumnGap = 64,
            TileSize = 64,
        };

    [TestMethod]
    public void DownstreamScopeMovesTheChild_ButNotTheStartOrAnUnrelatedNode()
    {
        var tree = new TreeDefaultViewModel();
        var start = LayoutTestKit.AddNode(tree, 0, 0);
        var child = LayoutTestKit.AddNode(tree, 0, 0);
        var unrelated = LayoutTestKit.AddNode(tree, 5000, 0);
        LayoutTestKit.AddLink(tree, start, child);

        var solution = LayoutTestKit.Solve(tree, out var problem, Options(start, WorkflowLayoutTraversal.Downstream));

        Assert.AreEqual(start.Anchor.Horizontal, solution.Placements[0].X, 1e-9, "the start node is the anchor");
        Assert.AreEqual(start.Anchor.Vertical, solution.Placements[0].Y, 1e-9);
        Assert.AreEqual(164d, solution.Placements[1].X, 1e-6,
            "the child is placed one column to the right of the start");
        Assert.AreEqual(start.Anchor.Vertical, solution.Placements[1].Y, 1e-6,
            "the child centre meets the start centre");
        Assert.AreEqual(unrelated.Anchor.Horizontal, solution.Placements[2].X, 1e-9,
            "a node outside the reachable subgraph is a fixed obstacle");
        Assert.AreEqual(unrelated.Anchor.Vertical, solution.Placements[2].Y, 1e-9);
        Assert.IsTrue(problem.Nodes[2].IsPinned);
    }

    [TestMethod]
    public void UpstreamScope_MovesTheParent_AndLeavesTheDownstreamChildAlone()
    {
        var tree = new TreeDefaultViewModel();
        var parent = LayoutTestKit.AddNode(tree, 0, 0);
        var start = LayoutTestKit.AddNode(tree, 0, 0);
        var downstream = LayoutTestKit.AddNode(tree, 0, 0);
        LayoutTestKit.AddLink(tree, parent, start);
        LayoutTestKit.AddLink(tree, start, downstream);

        var solution = LayoutTestKit.Solve(tree, out _, Options(start, WorkflowLayoutTraversal.Upstream));

        Assert.AreEqual(start.Anchor.Horizontal, solution.Placements[1].X, 1e-9, "the start node stays fixed");
        Assert.AreEqual(start.Anchor.Vertical, solution.Placements[1].Y, 1e-9);
        Assert.AreEqual(48d, solution.Placements[0].Y, 1e-6, "the upstream parent is separated from the start");
        Assert.AreEqual(downstream.Anchor.Horizontal, solution.Placements[2].X, 1e-9,
            "upstream traversal does not include the downstream child");
        Assert.AreEqual(downstream.Anchor.Vertical, solution.Placements[2].Y, 1e-9);
    }

    private sealed class RecordingSolver : IWorkflowLayoutSolver
    {
        private readonly DefaultWorkflowLayoutSolver _inner = new();

        public IWorkflowTreeViewModel? Tree { get; private set; }
        public WorkflowLayoutOptions? Options { get; private set; }

        public WorkflowLayoutResult Arrange(IWorkflowTreeViewModel tree, WorkflowLayoutOptions options)
        {
            Tree = tree;
            Options = options;
            return _inner.Arrange(tree, options);
        }
    }

    [TestMethod]
    public void NodeArrangeUsesTheSolverRegisteredOnTheTreeHelper()
    {
        var tree = new TreeDefaultViewModel();
        var start = LayoutTestKit.AddNode(tree, 0, 0);
        var child = LayoutTestKit.AddNode(tree, 0, 0);
        LayoutTestKit.AddLink(tree, start, child);

        var solver = new RecordingSolver();
        ((IWorkflowLayoutHost)tree.GetHelper()).LayoutSolver = solver;

        var result = start.Arrange(new WorkflowLayoutOptions
        {
            PositionTolerance = 0,
            RowGap = 8,
            ColumnGap = 64,
            TileSize = 64,
        });

        Assert.AreSame(tree, solver.Tree);
        Assert.AreSame(start, solver.Options?.StartNode);
        Assert.IsTrue(result.Applied, "the registered solver performs the default apply");
    }

    [TestMethod]
    public void NodeArrangeWithoutATreeThrows()
    {
        var detached = new NodeDefaultViewModel();

        try
        {
            detached.Arrange();
            Assert.Fail("a detached node has no helper to reach");
        }
        catch (ArgumentException)
        {
        }
    }

    [TestMethod]
    public void StartNodeMustBelongToTheTree()
    {
        var tree = new TreeDefaultViewModel();
        LayoutTestKit.AddNode(tree, 0, 0);
        var foreign = LayoutTestKit.AddNode(new TreeDefaultViewModel(), 0, 0);

        try
        {
            WorkflowLayoutProblemBuilder.Build(tree, Options(foreign, WorkflowLayoutTraversal.Downstream));
            Assert.Fail("a start node outside the tree must be rejected");
        }
        catch (ArgumentException)
        {
        }
    }
}
