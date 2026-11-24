using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.Layout;

/// <summary>
/// The separation projection is the first, deliberately narrow, layout stage. These tests pin the hard rules
/// (overlap and minimum gap), the pin rule, fan-out alignment, and that growth is never clamped.
/// </summary>
[TestClass]
public class SeparationProjectionTests
{
    [TestMethod]
    public void OverlappingNodes_ArePushedToTheMinimumRowGap()
    {
        var tree = new TreeDefaultViewModel();
        var top = LayoutTestKit.AddNode(tree, 0, 0);
        var bottom = LayoutTestKit.AddNode(tree, 0, 10);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            ColumnGap = 24,
            TileSize = 128,
            PositionTolerance = 0,
        };

        var solution = LayoutTestKit.Solve(tree, out _, options);
        var placedTop = solution.Placements[0];
        var placedBottom = solution.Placements[1];

        Assert.AreEqual(top.Anchor.Vertical, placedTop.Y, 1e-6, "the first node is already free");
        Assert.AreEqual(placedTop.Y + 40d + 16d, placedBottom.Y, 1e-6,
            "the second node must clear the first by the row gap");
    }

    [TestMethod]
    public void HardViolation_IsFixedEvenWhenToleranceWouldPreserveThePosition()
    {
        var tree = new TreeDefaultViewModel();
        LayoutTestKit.AddNode(tree, 0, 0);
        LayoutTestKit.AddNode(tree, 0, 10);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            TileSize = 128,
            PositionTolerance = 1000,
        };

        var solution = LayoutTestKit.Solve(tree, out _, options);

        Assert.AreEqual(56d, solution.Placements[1].Y, 1e-6,
            "a hard violation is repaired regardless of how tolerant the policy is");
    }

    [TestMethod]
    public void PinnedNode_DoesNotMove_AndTheOtherNodeMovesAway()
    {
        var tree = new TreeDefaultViewModel();
        var pinned = LayoutTestKit.AddNode(tree, 0, 0);
        LayoutTestKit.AddNode(tree, 0, 10);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            TileSize = 128,
            PositionTolerance = 0,
            Pinned = node => ReferenceEquals(node, pinned),
        };

        var solution = LayoutTestKit.Solve(tree, out var problem, options);
        LayoutTestKit.Apply(tree, problem, solution);

        Assert.AreEqual(0d, pinned.Anchor.Vertical, 1e-9, "the pinned node must not move at all");
        Assert.AreEqual(56d, solution.Placements[1].Y, 1e-6, "the free node moves clear of the pin");
    }

    [TestMethod]
    public void FanOut_ChildrenAlignTheirGroupCentreWithTheParent()
    {
        var tree = new TreeDefaultViewModel();
        var parent = LayoutTestKit.AddNode(tree, 0, 80);
        var first = LayoutTestKit.AddNode(tree, 164, 0, height: 20);
        var second = LayoutTestKit.AddNode(tree, 164, 30, height: 20);
        var third = LayoutTestKit.AddNode(tree, 164, 60, height: 20);
        LayoutTestKit.AddLink(tree, parent, first);
        LayoutTestKit.AddLink(tree, parent, second);
        LayoutTestKit.AddLink(tree, parent, third);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 10,
            ColumnGap = 64,
            TileSize = 64,
            PositionTolerance = 8,
        };

        var solution = LayoutTestKit.Solve(tree, out _, options);

        // Parent centre is 80 + 40/2 = 100. Three 20-high children with two 10 gaps span 80.
        Assert.AreEqual(60d, solution.Placements[1].Y, 1e-6);
        Assert.AreEqual(90d, solution.Placements[2].Y, 1e-6);
        Assert.AreEqual(120d, solution.Placements[3].Y, 1e-6);

        var firstCentre = solution.Placements[1].Y + 10d;
        var thirdCentre = solution.Placements[3].Y + 10d;
        Assert.AreEqual(parent.Anchor.Vertical + 20d, (firstCentre + thirdCentre) / 2d, 1e-6,
            "the child group centre meets the parent centre");
    }

    [TestMethod]
    public void NegativeAndOutOfBoundsPositions_AreNotClamped_AndGrowTheBounds()
    {
        var tree = new TreeDefaultViewModel();
        var parent = LayoutTestKit.AddNode(tree, 0, -20);
        var first = LayoutTestKit.AddNode(tree, 164, 0, height: 20);
        var second = LayoutTestKit.AddNode(tree, 164, 30, height: 20);
        var third = LayoutTestKit.AddNode(tree, 164, 60, height: 20);
        var farLeft = LayoutTestKit.AddNode(tree, -200, 500);
        LayoutTestKit.AddLink(tree, parent, first);
        LayoutTestKit.AddLink(tree, parent, second);
        LayoutTestKit.AddLink(tree, parent, third);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 10,
            ColumnGap = 64,
            TileSize = 64,
            PositionTolerance = 8,
        };

        var solution = LayoutTestKit.Solve(tree, out var problem, options);

        Assert.AreEqual(-40d, solution.Placements[1].Y, 1e-6,
            "the group centre is above the origin and is not clamped");
        Assert.AreEqual(-200d, solution.Placements[4].X, 1e-9,
            "a negative left edge is preserved");
        Assert.IsTrue(solution.Cost.Bounds.Top < problem.ContentBounds.Top,
            "the solved bounds grow in the negative direction");
        Assert.IsTrue(solution.Cost.Bounds.Left <= -200d + 1e-9);
    }

    [TestMethod]
    public void ScaleIsHonoured_WhenSnapshottingAndApplying()
    {
        var tree = new TreeDefaultViewModel();
        tree.Layout.Scale = new Scale(2, 2);
        var first = LayoutTestKit.AddNode(tree, 0, 0);
        var second = LayoutTestKit.AddNode(tree, 0, 5);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            TileSize = 128,
            PositionTolerance = 0,
        };

        var solution = LayoutTestKit.Solve(tree, out var problem, options);

        Assert.AreEqual(5d, problem.Nodes[1].Y, 1e-9,
            "the snapshot converts the collapsed getter back to world coordinates");
        Assert.AreEqual(100d, problem.Nodes[1].Width, 1e-9);

        LayoutTestKit.Apply(tree, problem, solution);

        Assert.AreEqual(56d / 2d, second.Anchor.Vertical, 1e-9,
            "the applier writes world coordinates; the getter collapses them again");
        Assert.AreEqual(0d, first.Anchor.Vertical, 1e-9);
    }

    [TestMethod]
    public void OverlapAcrossDifferentColumnAndRowBands_IsRepairedGlobally()
    {
        var tree = new TreeDefaultViewModel();
        var first = LayoutTestKit.AddNode(tree, 0, 0);
        var second = LayoutTestKit.AddNode(tree, 90, 30);

        var options = new WorkflowLayoutOptions
        {
            TileSize = 20, // centres land in (2,1) and (7,2): neither a shared column nor a shared row
            RowGap = 16,
            ColumnGap = 16,
            MaxSweeps = 2,
            PositionTolerance = 0,
        };

        var solution = LayoutTestKit.Solve(tree, out _, options);

        Assert.AreEqual(0, solution.Cost.Overlaps, "the global repair clears the cross-band overlap");
        Assert.AreEqual(0d, solution.Placements[0].X, 1e-9);
        Assert.AreEqual(0d, solution.Placements[0].Y, 1e-9);
        Assert.AreEqual(116d, solution.Placements[1].X, 1e-6,
            "the larger-index node takes the minimal 26-unit push along x");
        Assert.AreEqual(30d, solution.Placements[1].Y, 1e-6);
        Assert.IsTrue(solution.Placements[1].X > second.Anchor.Horizontal,
            "the pair is separated, not clamped back to the original position");
        Assert.IsTrue(solution.Diagnostics.Any(message => message.Contains("Global overlap repair")));
        Assert.AreEqual(first.Anchor.Horizontal, solution.Placements[0].X, 1e-9);
    }

    [TestMethod]
    public void TwoPinnedOverlappingNodes_AreLeftInPlace_AndReported()
    {
        var tree = new TreeDefaultViewModel();
        var first = LayoutTestKit.AddNode(tree, 0, 0);
        var second = LayoutTestKit.AddNode(tree, 90, 30);

        var options = new WorkflowLayoutOptions
        {
            TileSize = 20,
            RowGap = 16,
            ColumnGap = 16,
            PositionTolerance = 0,
            Pinned = _ => true,
        };

        var solution = LayoutTestKit.Solve(tree, out _, options);

        Assert.AreEqual(1, solution.Cost.Overlaps, "two pins cannot be separated");
        Assert.AreEqual(first.Anchor.Horizontal, solution.Placements[0].X, 1e-9);
        Assert.AreEqual(second.Anchor.Horizontal, solution.Placements[1].X, 1e-9);
        Assert.IsTrue(solution.Diagnostics.Any(message => message.Contains("two pinned nodes")));
    }
}
