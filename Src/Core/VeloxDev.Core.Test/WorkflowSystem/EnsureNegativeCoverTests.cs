using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

// ── Tests ────────────────────────────────────────────────────────────────────

[TestClass]
public class EnsureNegativeCoverTests
{
    [TestMethod]
    public void EnsureNegativeCover_NullTree_ReturnsFalse()
    {
        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(null));
    }

    [TestMethod]
    public void EnsureNegativeCover_EmptyNodes_NoOp()
    {
        var tree = new StubTree();
        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_PositiveOnlyContent_StrictNoOp()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(140, 140, 0) });
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(30, -0, 0) });

        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_NegativeContent_GrowsToNegMinAndSyncsActualOffset()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(-140, -120, 0) });
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(140, 140, 0) });

        Assert.IsTrue(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(140d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(120d, tree.Layout.NegativeOffset.Vertical);
        // CanvasLayout.Update re-raises ActualOffset == NegativeOffset in the same tick.
        Assert.AreEqual(140d, tree.Layout.ActualOffset.Horizontal);
        Assert.AreEqual(120d, tree.Layout.ActualOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_SingleAxisNegative_GrowsOnlyThatAxis()
    {
        var tree = new StubTree();
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(-90, 140, 0) });

        Assert.IsTrue(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(90d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(0d, tree.Layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_ExistingCoverAlreadyEnough_NeverShrinks()
    {
        var tree = new StubTree();
        tree.Layout.NegativeOffset = new Offset(500, 500);
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(-140, -120, 0) });

        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(500d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(500d, tree.Layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void EnsureNegativeCover_DeepZoomGrowth_IsMonotonicAndTracksNewMin()
    {
        var tree = new StubTree();
        // Scale 0.5: world −140 collapses to −280.
        tree.Nodes.Add(new StubNode { Anchor = new Anchor(-280, -240, 0) });
        Assert.IsTrue(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(280d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(240d, tree.Layout.NegativeOffset.Vertical);

        // Scale 0.1: same world node collapses further to −1400.
        tree.Nodes[0].Anchor = new Anchor(-1400, -1200, 0);
        Assert.IsTrue(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(1400d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(1200d, tree.Layout.NegativeOffset.Vertical);

        // Zoom back out (collapsed min grows toward positive): cover stays put, never shrinks.
        tree.Nodes[0].Anchor = new Anchor(-70, -60, 0);
        Assert.IsFalse(WorkflowSurfaceMath.EnsureNegativeCover(tree));
        Assert.AreEqual(1400d, tree.Layout.NegativeOffset.Horizontal);
        Assert.AreEqual(1200d, tree.Layout.NegativeOffset.Vertical);
    }
}
