using VeloxDev.WorkflowSystem;

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
