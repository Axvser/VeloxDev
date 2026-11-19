using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.Layout;

/// <summary>
/// The product rule for the first version: a node that is compliant and already close to the algorithm's
/// target must keep its user-placed position. Only a deviation beyond the tolerance earns a soft move.
/// </summary>
[TestClass]
public class PositionToleranceTests
{
    private static (TreeDefaultViewModel Tree, NodeDefaultViewModel Parent, NodeDefaultViewModel First, NodeDefaultViewModel Second)
        FanOutTree(double parentTop)
    {
        var tree = new TreeDefaultViewModel();
        var parent = LayoutTestKit.AddNode(tree, 0, parentTop);
        var first = LayoutTestKit.AddNode(tree, 164, 84, height: 20);
        var second = LayoutTestKit.AddNode(tree, 164, 112, height: 20);
        LayoutTestKit.AddLink(tree, parent, first);
        LayoutTestKit.AddLink(tree, parent, second);
        return (tree, parent, first, second);
    }

    private static WorkflowLayoutOptions Options() => new()
    {
        RowGap = 8,
        ColumnGap = 64,
        TileSize = 64,
        PositionTolerance = 8,
    };

    [TestMethod]
    public void CompliantNodesWithinTolerance_DoNotMoveAtAll()
    {
        var (tree, parent, first, second) = FanOutTree(parentTop: 84); // parent centre 104

        var solution = LayoutTestKit.Solve(tree, out _, Options());

        // 扇出目标与当前位置差 4：在默认容差 8 之内。
        Assert.AreEqual(first.Anchor.Horizontal, solution.Placements[1].X, 1e-9);
        Assert.AreEqual(first.Anchor.Vertical, solution.Placements[1].Y, 1e-9);
        Assert.AreEqual(second.Anchor.Horizontal, solution.Placements[2].X, 1e-9);
        Assert.AreEqual(second.Anchor.Vertical, solution.Placements[2].Y, 1e-9);
        Assert.AreEqual(parent.Anchor.Vertical, solution.Placements[0].Y, 1e-9);
        Assert.IsFalse(solution.HasMoves);
    }

    [TestMethod]
    public void NodesBeyondTolerance_AreSoftAligned()
    {
        var (tree, parent, _, _) = FanOutTree(parentTop: 108); // parent centre 128

        var solution = LayoutTestKit.Solve(tree, out _, Options());

        Assert.AreEqual(104d, solution.Placements[1].Y, 1e-6);
        Assert.AreEqual(132d, solution.Placements[2].Y, 1e-6);

        var groupCentre = ((solution.Placements[1].Y + 10d) + (solution.Placements[2].Y + 10d)) / 2d;
        Assert.AreEqual(parent.Anchor.Vertical + 20d, groupCentre, 1e-6);
    }

    [TestMethod]
    public void ZeroTolerance_AlignsTheGroupStrictly()
    {
        var tree = new TreeDefaultViewModel();
        var parent = LayoutTestKit.AddNode(tree, 0, 84);
        var first = LayoutTestKit.AddNode(tree, 164, 84, height: 20);
        var second = LayoutTestKit.AddNode(tree, 164, 112, height: 20);
        LayoutTestKit.AddLink(tree, parent, first);
        LayoutTestKit.AddLink(tree, parent, second);

        var options = Options();
        options.PositionTolerance = 0;

        var solution = LayoutTestKit.Solve(tree, out _, options);

        Assert.AreEqual(80d, solution.Placements[1].Y, 1e-6,
            "tolerance zero means the canonical fan-out placement");
        Assert.AreEqual(108d, solution.Placements[2].Y, 1e-6);
    }
}
