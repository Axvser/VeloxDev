using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.Layout;

/// <summary>
/// The canonical alignment beyond fan-out: a single child sits on its parent's centre line one column to the
/// right, and a fan-in merge sits on the overall centre line of its parents one column after the rightmost one.
/// </summary>
[TestClass]
public class HierarchyAlignmentTests
{
    private static WorkflowLayoutOptions Options(double tolerance = 0d) => new()
    {
        RowGap = 10,
        ColumnGap = 64,
        TileSize = 64,
        PositionTolerance = tolerance,
    };

    [TestMethod]
    public void SingleChild_IsCentredAndPlacedToTheRightOfItsParent()
    {
        var tree = new TreeDefaultViewModel();
        var parent = LayoutTestKit.AddNode(tree, 0, 0);
        var child = LayoutTestKit.AddNode(tree, 300, 200, height: 20);
        LayoutTestKit.AddLink(tree, parent, child);

        var solution = LayoutTestKit.Solve(tree, out _, Options());

        Assert.AreEqual(10d, solution.Placements[1].Y, 1e-6,
            "the child centre meets the parent centre (20)");
        Assert.AreEqual(164d, solution.Placements[1].X, 1e-6,
            "the child starts one column to the right of the parent");
    }

    [TestMethod]
    public void Chain_CascadesTheCentreAndTheColumnDownstream()
    {
        var tree = new TreeDefaultViewModel();
        var first = LayoutTestKit.AddNode(tree, 0, 0);
        var second = LayoutTestKit.AddNode(tree, 300, 200, height: 20);
        var third = LayoutTestKit.AddNode(tree, 600, 400, height: 20);
        LayoutTestKit.AddLink(tree, first, second);
        LayoutTestKit.AddLink(tree, second, third);

        var solution = LayoutTestKit.Solve(tree, out _, Options());

        Assert.AreEqual(10d, solution.Placements[1].Y, 1e-6);
        Assert.AreEqual(164d, solution.Placements[1].X, 1e-6);
        Assert.AreEqual(10d, solution.Placements[2].Y, 1e-6,
            "the third node follows the second node's aligned centre");
        Assert.AreEqual(328d, solution.Placements[2].X, 1e-6,
            "the third node starts one column to the right of the second");
    }

    [TestMethod]
    public void FanIn_IsCentredOnTheParentsBoundingBox()
    {
        var tree = new TreeDefaultViewModel();
        var tall = LayoutTestKit.AddNode(tree, 0, 0);                    // 0..40
        var shortParent = LayoutTestKit.AddNode(tree, 0, 100, height: 20); // 100..120
        var merge = LayoutTestKit.AddNode(tree, 400, 500, height: 20);
        LayoutTestKit.AddLink(tree, tall, merge);
        LayoutTestKit.AddLink(tree, shortParent, merge);

        var solution = LayoutTestKit.Solve(tree, out _, Options());

        // Bounding box 0..120 has its centre at 60; a 20-high merge starts at 50 and one column after both parents.
        Assert.AreEqual(50d, solution.Placements[2].Y, 1e-6);
        Assert.AreEqual(164d, solution.Placements[2].X, 1e-6);
    }

    [TestMethod]
    public void FanInAfterFanOut_MeetsTheGroupCentre()
    {
        var tree = new TreeDefaultViewModel();
        var source = LayoutTestKit.AddNode(tree, 0, 0);                  // centre 20
        var first = LayoutTestKit.AddNode(tree, 300, 50, height: 20);
        var second = LayoutTestKit.AddNode(tree, 300, 250, height: 20);
        var merge = LayoutTestKit.AddNode(tree, 600, 500, height: 20);
        LayoutTestKit.AddLink(tree, source, first);
        LayoutTestKit.AddLink(tree, source, second);
        LayoutTestKit.AddLink(tree, first, merge);
        LayoutTestKit.AddLink(tree, second, merge);

        var solution = LayoutTestKit.Solve(tree, out _, Options());

        // Fan-out spans -5..45 around the source centre; the merge follows that same centre and column.
        Assert.AreEqual(-5d, solution.Placements[1].Y, 1e-6);
        Assert.AreEqual(25d, solution.Placements[2].Y, 1e-6);
        Assert.AreEqual(10d, solution.Placements[3].Y, 1e-6);
        Assert.AreEqual(328d, solution.Placements[3].X, 1e-6);
    }

    [TestMethod]
    public void Tolerance_PreservesAClosePlacement_AndAlignsAFarOne()
    {
        var closeTree = new TreeDefaultViewModel();
        var closeParent = LayoutTestKit.AddNode(closeTree, 0, 0);
        var closeChild = LayoutTestKit.AddNode(closeTree, 164, 14, height: 20);
        LayoutTestKit.AddLink(closeTree, closeParent, closeChild);

        var close = LayoutTestKit.Solve(closeTree, out _, Options(tolerance: 8));

        var farTree = new TreeDefaultViewModel();
        var farParent = LayoutTestKit.AddNode(farTree, 0, 0);
        var farChild = LayoutTestKit.AddNode(farTree, 164, 30, height: 20);
        LayoutTestKit.AddLink(farTree, farParent, farChild);

        var far = LayoutTestKit.Solve(farTree, out _, Options(tolerance: 8));

        Assert.AreEqual(14d, close.Placements[1].Y, 1e-6, "4 units off the centre is inside tolerance 8");
        Assert.AreEqual(10d, far.Placements[1].Y, 1e-6, "20 units off the centre is aligned");
    }

    [TestMethod]
    public void Tolerance_IsAppliedPerAxis()
    {
        var tree = new TreeDefaultViewModel();
        var parent = LayoutTestKit.AddNode(tree, 0, 0);
        var child = LayoutTestKit.AddNode(tree, 200, 14, height: 20);
        LayoutTestKit.AddLink(tree, parent, child);

        var solution = LayoutTestKit.Solve(tree, out _, Options(tolerance: 8));

        Assert.AreEqual(164d, solution.Placements[1].X, 1e-6,
            "36 units off the column is beyond tolerance and is aligned");
        Assert.AreEqual(14d, solution.Placements[1].Y, 1e-6,
            "4 units off the centre is inside tolerance and is preserved");
    }

    [TestMethod]
    public void PinnedNode_IsNotAligned()
    {
        var tree = new TreeDefaultViewModel();
        var first = LayoutTestKit.AddNode(tree, 0, 0);
        var second = LayoutTestKit.AddNode(tree, 0, 200);
        var merge = LayoutTestKit.AddNode(tree, 400, 1000);
        LayoutTestKit.AddLink(tree, first, merge);
        LayoutTestKit.AddLink(tree, second, merge);

        var options = Options();
        options.Pinned = node => ReferenceEquals(node, merge);

        var solution = LayoutTestKit.Solve(tree, out _, options);

        Assert.AreEqual(1000d, solution.Placements[2].Y, 1e-9, "a pinned merge keeps its position");
        Assert.AreEqual(400d, solution.Placements[2].X, 1e-9);
    }
}
