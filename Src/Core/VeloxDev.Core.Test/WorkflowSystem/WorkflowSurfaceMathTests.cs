using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class WorkflowSurfaceMathTests
{
    private const double W = 260;
    private const double H = 180;

    [TestMethod]
    public void ScaleCollapse_LowerRight_ScalesAboutOrigin()
    {
        // Origin sits at node-local (-140,-140); collapsing by 1/2 about it.
        var (sx, sy, cx, cy) = WorkflowSurfaceMath.ScaleCollapse(140, 140, 2, 2);
        Assert.AreEqual(0.5, sx);
        Assert.AreEqual(0.5, sy);
        Assert.AreEqual(-140d, cx);
        Assert.AreEqual(-140d, cy);
    }

    [TestMethod]
    public void ScaleCollapse_UpperLeft_CenterIsPositive()
    {
        var (sx, sy, cx, cy) = WorkflowSurfaceMath.ScaleCollapse(-140, -140, 2, 2);
        Assert.AreEqual(0.5, sx);
        Assert.AreEqual(0.5, sy);
        Assert.AreEqual(140d, cx);
        Assert.AreEqual(140d, cy);
    }

    [TestMethod]
    public void ScaleCollapse_IdentityScale_HasUnitFactor()
    {
        var (sx, sy, _, _) = WorkflowSurfaceMath.ScaleCollapse(140, 140, 1, 1);
        Assert.AreEqual(1d, sx);
        Assert.AreEqual(1d, sy);
    }

    [TestMethod]
    public void ScaleCollapse_ZeroScale_FallsBackToIdentity()
    {
        var (sx, sy, cx, cy) = WorkflowSurfaceMath.ScaleCollapse(140, 140, 0, 0);
        Assert.AreEqual(1d, sx);
        Assert.AreEqual(1d, sy);
        Assert.AreEqual(-140d, cx);
        Assert.AreEqual(-140d, cy);
    }

    [TestMethod]
    public void ScaleVisualBounds_IdentityScale_EqualsModelBounds()
    {
        var (l, t, w, h) = WorkflowSurfaceMath.ScaleVisualBounds(140, 140, W, H, 1, 1);
        Assert.AreEqual(140d, l);
        Assert.AreEqual(140d, t);
        Assert.AreEqual(W, w);
        Assert.AreEqual(H, h);
    }

    [TestMethod]
    public void ScaleVisualBounds_LowerRight_CollapsesTowardOrigin()
    {
        // scale 2 halves the anchor distance and the size: node shrinks toward (0,0).
        var (l, t, w, h) = WorkflowSurfaceMath.ScaleVisualBounds(140, 140, W, H, 2, 2);
        Assert.AreEqual(70d, l);
        Assert.AreEqual(70d, t);
        Assert.AreEqual(W / 2, w);
        Assert.AreEqual(H / 2, h);
    }

    [TestMethod]
    public void ScaleVisualBounds_UpperLeft_CollapsesTowardOrigin()
    {
        var (l, t, w, h) = WorkflowSurfaceMath.ScaleVisualBounds(-140, -140, W, H, 2, 2);
        Assert.AreEqual(-70d, l);
        Assert.AreEqual(-70d, t);
        Assert.AreEqual(W / 2, w);
        Assert.AreEqual(H / 2, h);
    }

    [TestMethod]
    public void ScaleVisualBounds_NonUniformScale_UsesEachAxis()
    {
        var (l, t, w, h) = WorkflowSurfaceMath.ScaleVisualBounds(140, 140, W, H, 2, 0.5);
        Assert.AreEqual(70d, l);
        Assert.AreEqual(280d, t);
        Assert.AreEqual(W / 2, w);
        Assert.AreEqual(H / 0.5, h);
    }
}
