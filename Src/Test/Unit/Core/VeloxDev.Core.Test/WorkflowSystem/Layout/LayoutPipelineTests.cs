using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.Layout;

/// <summary>Pipeline-level contracts: determinism, dry runs, the single undo entry, and crossing diagnostics.</summary>
[TestClass]
public class LayoutPipelineTests
{
    [TestMethod]
    public void SerialAndParallel_ProduceTheSamePlacement()
    {
        var tree = new TreeDefaultViewModel();
        for (var i = 0; i < 120; i++)
        {
            LayoutTestKit.AddNode(tree, (i % 12) * 40, (i / 12) * 30);
        }

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            ColumnGap = 24,
            TileSize = 128,
            MaxSweeps = 4,
            ParallelThreshold = 1,
            PositionTolerance = 0,
        };

        var serial = LayoutTestKit.Solve(tree, out _, options, parallel: false);
        var parallel = LayoutTestKit.Solve(tree, out _, options, parallel: true);

        Assert.AreEqual(serial.Placements.Count, parallel.Placements.Count);
        for (var i = 0; i < serial.Placements.Count; i++)
        {
            Assert.AreEqual(serial.Placements[i].X, parallel.Placements[i].X, 0d, $"x of node {i}");
            Assert.AreEqual(serial.Placements[i].Y, parallel.Placements[i].Y, 0d, $"y of node {i}");
        }
        Assert.AreEqual(serial.Cost.Overlaps, parallel.Cost.Overlaps);
        Assert.AreEqual(serial.Cost.Displacement, parallel.Cost.Displacement, 0d);
    }

    [TestMethod]
    public void DryRun_DoesNotWriteTheModel()
    {
        var tree = new TreeDefaultViewModel();
        LayoutTestKit.AddNode(tree, 0, 0);
        var second = LayoutTestKit.AddNode(tree, 0, 10);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            TileSize = 128,
            PositionTolerance = 0,
            DryRun = true,
        };

        var solution = LayoutTestKit.Solve(tree, out var problem, options);
        var result = LayoutTestKit.Apply(tree, problem, solution);

        Assert.IsTrue(result.DryRun);
        Assert.IsFalse(result.Applied);
        Assert.AreEqual(0, result.AppliedCount);
        Assert.AreEqual(10d, second.Anchor.Vertical, 1e-9, "a dry run never touches an anchor");
    }

    [TestMethod]
    public void Apply_IsOneUndoStep_ThatRestoresEveryAnchor()
    {
        var tree = new TreeDefaultViewModel();
        var first = LayoutTestKit.AddNode(tree, 0, 0);
        var second = LayoutTestKit.AddNode(tree, 0, 10);
        var third = LayoutTestKit.AddNode(tree, 0, 20);

        var originalFirst = new Anchor(first.Anchor.Horizontal, first.Anchor.Vertical, first.Anchor.Layer);
        var originalSecond = new Anchor(second.Anchor.Horizontal, second.Anchor.Vertical, second.Anchor.Layer);
        var originalThird = new Anchor(third.Anchor.Horizontal, third.Anchor.Vertical, third.Anchor.Layer);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            TileSize = 128,
            PositionTolerance = 0,
        };

        var solution = LayoutTestKit.Solve(tree, out var problem, options);
        var result = LayoutTestKit.Apply(tree, problem, solution);

        Assert.IsTrue(result.Applied);
        Assert.AreEqual(2, result.AppliedCount, "the first node was already in place");
        Assert.AreEqual(56d, second.Anchor.Vertical, 1e-9);
        Assert.AreEqual(112d, third.Anchor.Vertical, 1e-9);

        // A single Ctrl+Z restores the whole layout, including both moved nodes.
        tree.GetHelper().Undo();

        Assert.AreEqual(originalFirst.Horizontal, first.Anchor.Horizontal, 1e-9);
        Assert.AreEqual(originalFirst.Vertical, first.Anchor.Vertical, 1e-9);
        Assert.AreEqual(originalSecond.Vertical, second.Anchor.Vertical, 1e-9);
        Assert.AreEqual(originalThird.Vertical, third.Anchor.Vertical, 1e-9);
    }

    [TestMethod]
    public void CrossingsFollowSolvedPositions_NotPublishedCurves()
    {
        var tree = new TreeDefaultViewModel();
        var a = LayoutTestKit.AddNode(tree, 0, 0);
        var b = LayoutTestKit.AddNode(tree, 300, 300);
        var c = LayoutTestKit.AddNode(tree, 0, 300);
        var d = LayoutTestKit.AddNode(tree, 300, 0);
        var firstLink = LayoutTestKit.AddLink(tree, a, b);
        var secondLink = LayoutTestKit.AddLink(tree, c, d);

        var options = new WorkflowLayoutOptions
        {
            TileSize = 128,
            RowGap = 32,
            ColumnGap = 64,
            PositionTolerance = 0,
            // 代价测试要固定几何：对齐阶段会把单子边拉成同一条中心线，原来的交叉就没了。
            Pinned = _ => true,
        };

        var crossing = LayoutTestKit.Solve(tree, out _, options);
        Assert.IsTrue(crossing.Cost.CostMeasured);
        Assert.AreEqual(1, crossing.Cost.Crossings, "the solved centre lines cross once");
        Assert.IsTrue(crossing.Cost.CrossingsApproximate);

        // Pre-layout curves must not answer for the solved geometry. These two curves cross, so a cost
        // implementation that still read them would report a crossing even after the nodes move apart.
        ((ILinkHitTestable)firstLink.GetHelper()).SetCurve(LinkCurve.FromPoints([50d, 350d], [0d, 440d]));
        ((ILinkHitTestable)secondLink.GetHelper()).SetCurve(LinkCurve.FromPoints([50d, 350d], [320d, 420d]));

        d.Anchor = new Anchor(300, 400, 0);

        var separated = LayoutTestKit.Solve(tree, out _, options);
        Assert.AreEqual(0, separated.Cost.Crossings,
            "the count follows the solved positions, not the published curve");
        Assert.IsTrue(separated.Cost.CrossingsApproximate);
    }

    [TestMethod]
    public void NonFiniteGeometry_IsSkipped_AndIndicesStayAligned()
    {
        var tree = new TreeDefaultViewModel();
        LayoutTestKit.AddNode(tree, 0, 0);
        var unmeasured = new NodeDefaultViewModel
        {
            Parent = tree,
            Anchor = new Anchor(double.NaN, 0, 0),
            Size = new Size(100, 40),
        };
        tree.Nodes.Add(unmeasured);
        var third = LayoutTestKit.AddNode(tree, 0, 10);
        var huge = new NodeDefaultViewModel
        {
            Parent = tree,
            Anchor = new Anchor(0, 500, 0),
            Size = new Size(double.PositiveInfinity, 40),
        };
        tree.Nodes.Add(huge);

        // Edges touching a skipped node are dropped with it.
        LayoutTestKit.AddLink(tree, unmeasured, third);
        LayoutTestKit.AddLink(tree, third, huge);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            TileSize = 128,
            PositionTolerance = 0,
        };

        var solution = LayoutTestKit.Solve(tree, out var problem, options);

        Assert.AreEqual(2, problem.Nodes.Count);
        Assert.AreEqual(2, problem.Sources.Count, "applier indices stay aligned with the filtered nodes");
        CollectionAssert.AreEqual(new[] { 1, 3 }, problem.SkippedIndices.ToArray());
        Assert.AreEqual(0, problem.Edges.Count, "edges touching a skipped node are dropped");
        Assert.IsTrue(solution.Diagnostics.Any(message => message.Contains("not finite")));
        Assert.AreEqual(2, solution.Placements.Count);
        Assert.AreEqual(56d, solution.Placements[1].Y, 1e-6);

        var result = LayoutTestKit.Apply(tree, problem, solution);
        Assert.IsTrue(result.Applied);
        Assert.AreEqual(56d, third.Anchor.Vertical, 1e-9);
        Assert.IsTrue(double.IsNaN(unmeasured.Anchor.Horizontal), "a skipped node is never written");
        Assert.AreEqual(500d, huge.Anchor.Vertical, 1e-9);
    }

    [TestMethod]
    public void LargeGraph_CostStaysLocalAndOverlapIsZero()
    {
        var tree = new TreeDefaultViewModel();
        var nodes = new List<NodeDefaultViewModel>();
        for (var row = 0; row < 20; row++)
        {
            for (var column = 0; column < 25; column++)
            {
                nodes.Add(LayoutTestKit.AddNode(tree, column * 200d, row * 100d, 100d, 40d));
            }
        }

        for (var i = 0; i < 200; i++)
        {
            LayoutTestKit.AddLink(tree, nodes[i], nodes[i + 1]);
        }

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            ColumnGap = 24,
            TileSize = 128,
            MaxSweeps = 2,
            PositionTolerance = 0,
        };

        var solution = LayoutTestKit.Solve(tree, out _, options);

        Assert.AreEqual(500, solution.Placements.Count);
        Assert.IsTrue(solution.Cost.CostMeasured);
        Assert.AreEqual(0, solution.Cost.Overlaps, "the grid layout has no overlapping rectangles");
        Assert.IsTrue(solution.Cost.Crossings >= 0);

        foreach (var placement in solution.Placements)
        {
            Assert.IsFalse(double.IsNaN(placement.X));
            Assert.IsFalse(double.IsNaN(placement.Y));
        }
    }

    [TestMethod]
    public void Apply_SkipsANodeWhoseAnchorChangedSinceTheSnapshot()
    {
        var tree = new TreeDefaultViewModel();
        LayoutTestKit.AddNode(tree, 0, 0);
        var second = LayoutTestKit.AddNode(tree, 0, 10);
        var third = LayoutTestKit.AddNode(tree, 0, 20);

        var options = new WorkflowLayoutOptions
        {
            RowGap = 16,
            TileSize = 128,
            PositionTolerance = 0,
        };

        var solution = LayoutTestKit.Solve(tree, out var problem, options);

        // Someone edits the model between Solve and Apply.
        second.Anchor = new Anchor(0, 200, 0);

        var result = LayoutTestKit.Apply(tree, problem, solution);

        Assert.IsTrue(result.Applied);
        Assert.AreEqual(1, result.AppliedCount, "only the node still matching the snapshot is written");
        Assert.AreEqual(1, result.SkippedCount, "the concurrently moved node is protected");
        Assert.AreEqual(200d, second.Anchor.Vertical, 1e-9);
        Assert.AreEqual(112d, third.Anchor.Vertical, 1e-9);

        tree.GetHelper().Undo();
        Assert.AreEqual(200d, second.Anchor.Vertical, 1e-9, "undo only owns the applied node");
        Assert.AreEqual(20d, third.Anchor.Vertical, 1e-9);
    }
}
