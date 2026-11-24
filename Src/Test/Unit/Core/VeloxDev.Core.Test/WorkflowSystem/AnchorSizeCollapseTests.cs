using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class AnchorSizeCollapseTests
{
    [TestMethod]
    public void AnchorCollapse_NullScale_Identity()
    {
        var a = new Anchor(140, 140, 3);
        var result = a.Collapse(null);
        Assert.AreSame(a, result);
        Assert.AreEqual(140, result.Horizontal);
        Assert.AreEqual(140, result.Vertical);
        Assert.AreEqual(3, result.Layer);
    }

    [TestMethod]
    public void AnchorCollapse_DefaultScale_Identity()
    {
        var a = new Anchor(140, 140, 3);
        var result = a.Collapse(new Scale(1, 1));
        Assert.AreSame(a, result);
    }

    [TestMethod]
    public void AnchorCollapse_Scale2_HalvesTowardOrigin()
    {
        var a = new Anchor(140, 140, 3);
        var result = a.Collapse(new Scale(2, 2));
        Assert.AreEqual(70, result.Horizontal);
        Assert.AreEqual(70, result.Vertical);
        Assert.AreEqual(3, result.Layer);
    }

    [TestMethod]
    public void AnchorCollapse_NegativeQuadrant_CollapsesTowardOrigin()
    {
        // Upper-left quadrant: negative anchors move toward (0,0) — e.g. -140/2 = -70 (closer to origin).
        var a = new Anchor(-140, -140, 0);
        var result = a.Collapse(new Scale(2, 2));
        Assert.AreEqual(-70, result.Horizontal);
        Assert.AreEqual(-70, result.Vertical);
    }

    [TestMethod]
    public void AnchorCollapse_NonUniform_AppliesEachAxis()
    {
        var a = new Anchor(140, 100, 0);
        var result = a.Collapse(new Scale(2, 0.5));
        Assert.AreEqual(70, result.Horizontal);   // 140 / 2
        Assert.AreEqual(200, result.Vertical);    // 100 / 0.5
    }

    [TestMethod]
    public void AnchorCollapse_ZeroScale_FallsBackToIdentity()
    {
        var a = new Anchor(140, 140, 0);
        var result = a.Collapse(new Scale(0, 0));
        Assert.AreEqual(140, result.Horizontal);
        Assert.AreEqual(140, result.Vertical);
    }

    [TestMethod]
    public void AnchorExpand_Scale2_Doubles()
    {
        var a = new Anchor(70, 70, 3);
        var result = a.Expand(new Scale(2, 2));
        Assert.AreEqual(140, result.Horizontal);
        Assert.AreEqual(140, result.Vertical);
        Assert.AreEqual(3, result.Layer);
    }

    [TestMethod]
    public void AnchorExpand_NonUniform_AppliesEachAxis()
    {
        var a = new Anchor(70, 200, 1);
        var result = a.Expand(new Scale(2, 0.5));
        Assert.AreEqual(140, result.Horizontal);
        Assert.AreEqual(100, result.Vertical);
        Assert.AreEqual(1, result.Layer);
    }

    [TestMethod]
    public void AnchorExpand_NullOrZeroScale_ReturnsSame()
    {
        var a = new Anchor(140, 140, 3);
        Assert.AreSame(a, a.Expand(null));
        Assert.AreSame(a, a.Expand(new Scale(1, 1)));
        Assert.AreSame(a, a.Expand(new Scale(0, 0)));
    }

    [TestMethod]
    public void AnchorExpand_UndoesCollapse()
    {
        var original = new Anchor(-140, 100, 7);
        var scale = new Scale(2, 0.5);
        var view = original.Collapse(scale);
        var world = view.Expand(scale);

        Assert.AreEqual(original.Horizontal, world.Horizontal);
        Assert.AreEqual(original.Vertical, world.Vertical);
        Assert.AreEqual(original.Layer, world.Layer);
    }

    [TestMethod]
    public void SizeExpand_UndoesCollapse()
    {
        var original = new Size(260, 180);
        var scale = new Scale(2, 0.5);
        var view = original.Collapse(scale);
        var world = view.Expand(scale);

        Assert.AreEqual(original.Width, world.Width);
        Assert.AreEqual(original.Height, world.Height);
    }

    [TestMethod]
    public void NodeWorldExtensions_RecoverTheStoredValue()
    {
        var tree = new TreeDefaultViewModel();
        tree.Layout.Scale = new Scale(2, 0.5);
        var node = new NodeDefaultViewModel
        {
            Parent = tree,
            Anchor = new Anchor(-140, 100, 7),
            Size = new Size(260, 180),
        };

        Assert.AreEqual(-70d, node.Anchor.Horizontal, 1e-9);
        Assert.AreEqual(200d, node.Anchor.Vertical, 1e-9);

        var world = node.GetWorldAnchor();
        Assert.AreEqual(-140d, world.Horizontal, 1e-9);
        Assert.AreEqual(100d, world.Vertical, 1e-9);
        Assert.AreEqual(7, world.Layer);

        var size = node.GetWorldSize();
        Assert.AreEqual(260d, size.Width, 1e-9);
        Assert.AreEqual(180d, size.Height, 1e-9);
    }

    [TestMethod]
    public void SizeCollapse_Scale2_Halves()
    {
        var s = new Size(260, 180);
        var result = s.Collapse(new Scale(2, 2));
        Assert.AreEqual(130, result.Width);
        Assert.AreEqual(90, result.Height);
    }

    [TestMethod]
    public void SizeCollapse_NonUniform_AppliesEachAxis()
    {
        var s = new Size(260, 180);
        var result = s.Collapse(new Scale(2, 0.5));
        Assert.AreEqual(130, result.Width);
        Assert.AreEqual(360, result.Height);
    }

    [TestMethod]
    public void SizeCollapse_Identity_ReturnsSame()
    {
        var s = new Size(260, 180);
        Assert.AreSame(s, s.Collapse(new Scale(1, 1)));
        Assert.AreSame(s, s.Collapse(null));
    }
}
