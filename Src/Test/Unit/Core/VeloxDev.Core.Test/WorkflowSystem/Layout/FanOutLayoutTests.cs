using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.Layout;

/// <summary>
/// The default fan-out rule: children sit in one column to the right of their parent, stacked with the row
/// gap and centred on the parent's vertical centre. At tolerance zero the result is canonical — it does not
/// depend on where the user happened to leave the children.
/// </summary>
[TestClass]
public class FanOutLayoutTests
{
    private static WorkflowLayoutOptions Options(double tolerance = 0d) => new()
    {
        RowGap = 10,
        ColumnGap = 64,
        TileSize = 64,
        PositionTolerance = tolerance,
    };

    private static (TreeDefaultViewModel Tree, NodeDefaultViewModel Parent, NodeDefaultViewModel[] Children)
        TreeWithThreeChildren(params (double X, double Y)[] positions)
    {
        var tree = new TreeDefaultViewModel();
        var parent = LayoutTestKit.AddNode(tree, 0, 100);
        var children = new NodeDefaultViewModel[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            children[i] = LayoutTestKit.AddNode(tree, positions[i].X, positions[i].Y, height: 20);
            LayoutTestKit.AddLink(tree, parent, children[i]);
        }

        return (tree, parent, children);
    }

    [TestMethod]
    public void CanonicalPlacementIgnoresWhereTheUserLeftTheChildren()
    {
        var (firstTree, _, _) = TreeWithThreeChildren((0, 0), (500, -300), (1000, 900));
        var (secondTree, _, _) = TreeWithThreeChildren((900, 700), (50, 50), (300, -500));

        var first = LayoutTestKit.Solve(firstTree, out _, Options());
        var second = LayoutTestKit.Solve(secondTree, out _, Options());

        // Parent is (0,100), 100x40 => right 100, centre 120. Three 20-high children with two 10 gaps span
        // 80, so the canonical column is x=164 and y=80/110/140 — independent of the starting positions.
        for (var i = 1; i <= 3; i++)
        {
            Assert.AreEqual(164d, first.Placements[i].X, 1e-6, $"first tree x({i})");
            Assert.AreEqual(164d, second.Placements[i].X, 1e-6, $"second tree x({i})");
        }

        Assert.AreEqual(80d, first.Placements[1].Y, 1e-6);
        Assert.AreEqual(110d, first.Placements[2].Y, 1e-6);
        Assert.AreEqual(140d, first.Placements[3].Y, 1e-6);
        Assert.AreEqual(first.Placements[1].Y, second.Placements[1].Y, 1e-6);
        Assert.AreEqual(first.Placements[2].Y, second.Placements[2].Y, 1e-6);
        Assert.AreEqual(first.Placements[3].Y, second.Placements[3].Y, 1e-6);
    }

    [TestMethod]
    public void AGroupAlreadyWithinToleranceIsLeftAlone()
    {
        var (tree, _, _) = TreeWithThreeChildren((168, 84), (168, 114), (168, 144));

        var solution = LayoutTestKit.Solve(tree, out _, Options(tolerance: 8));

        Assert.AreEqual(168d, solution.Placements[1].X, 1e-6);
        Assert.AreEqual(84d, solution.Placements[1].Y, 1e-6);
        Assert.AreEqual(114d, solution.Placements[2].Y, 1e-6);
        Assert.AreEqual(144d, solution.Placements[3].Y, 1e-6);
    }

    [TestMethod]
    public void AGroupWithAPinnedChildIsNotCanonicalised()
    {
        var (tree, _, children) = TreeWithThreeChildren((164, 0), (164, 200), (164, 400));
        var options = Options();
        options.Pinned = node => ReferenceEquals(node, children[1]);

        var solution = LayoutTestKit.Solve(tree, out _, options);

        Assert.AreEqual(0d, solution.Placements[1].Y, 1e-6);
        Assert.AreEqual(200d, solution.Placements[2].Y, 1e-6, "the pinned child must not move");
        Assert.AreEqual(400d, solution.Placements[3].Y, 1e-6);
    }
}
