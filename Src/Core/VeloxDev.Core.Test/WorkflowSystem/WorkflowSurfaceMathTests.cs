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

    // ── Viewport-center pivot helpers ────────────────────────────────────────

    [TestMethod]
    public void ScrollCenter_ComputesViewportCenter()
    {
        var (cx, cy) = WorkflowSurfaceMath.ScrollCenter(100, 50, 800, 600);
        Assert.AreEqual(500d, cx);
        Assert.AreEqual(350d, cy);
    }

    [TestMethod]
    public void WorldAtViewportCenter_OriginOffsetScaleOne_EqualsScrollCenter()
    {
        // No canvas offset, scale 1, origin pivot: world == scroll, so the viewport-center world point
        // is just the scroll center. WorldOrigin mode (the default in the ctor used below).
        var layout = new CanvasLayout { ZoomCenter = ZoomCenter.WorldOrigin }; // ActualOffset (0,0), Scale (1,1), CollapsePivot (0,0)
        var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(100, 50, 800, 600, layout);
        Assert.AreEqual(500d, wx);
        Assert.AreEqual(350d, wy);
    }

    [TestMethod]
    public void WorldAtViewportCenter_AccountsForOffsetScaleAndPivot()
    {
        // world = (screenCenter − ActualOffset)·scale. The canvas geometry is the same in both modes:
        // ActualOffset == NegativeOffset regardless of scale (zoom never translates the canvas).
        var layout = new CanvasLayout
        {
            NegativeOffset = new Offset(40, 30), // ActualOffset == NegativeOffset
            Scale = new Scale(2, 2),
            ZoomCenter = ZoomCenter.ViewportCenter,
            CollapsePivot = new Anchor(60, 50, 0),
        };
        var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(100, 50, 800, 600, layout);
        Assert.AreEqual((500d - 40) * 2, wx); // 920
        Assert.AreEqual((350d - 30) * 2, wy); // 640
    }

    [TestMethod]
    public void PivotCenterScroll_CentersPivotWorldPoint()
    {
        // scroll = pivot/scale + ActualOffset − viewport/2, ActualOffset == NegativeOffset.
        var layout = new CanvasLayout
        {
            NegativeOffset = new Offset(40, 30),
            Scale = new Scale(2, 2),
            ZoomCenter = ZoomCenter.ViewportCenter,
            CollapsePivot = new Anchor(100, 80, 0),
        };
        var (sx, sy) = WorkflowSurfaceMath.PivotCenterScroll(100, 80, layout, 800, 600);
        Assert.AreEqual(100 / 2.0 + 40 - 400, sx);   // -310
        Assert.AreEqual(80 / 2.0 + 30 - 300, sy);    // -230
    }

    [TestMethod]
    public void LayoutPivot_ViewportCenter_ReturnsCollapsePivot()
    {
        var layout = new CanvasLayout
        {
            ZoomCenter = ZoomCenter.ViewportCenter,
            CollapsePivot = new Anchor(270, 190, 0),
        };
        var pivot = WorkflowSurfaceMath.LayoutPivot(layout);
        Assert.AreEqual(270d, pivot.Horizontal);
        Assert.AreEqual(190d, pivot.Vertical);
    }

    [TestMethod]
    public void LayoutPivot_WorldOrigin_IsOrigin()
    {
        var layout = new CanvasLayout
        {
            ZoomCenter = ZoomCenter.WorldOrigin,
            CollapsePivot = new Anchor(270, 190, 0),
        };
        var pivot = WorkflowSurfaceMath.LayoutPivot(layout);
        Assert.AreEqual(0d, pivot.Horizontal);
        Assert.AreEqual(0d, pivot.Vertical);
    }
}
