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

    // ── Grid offset helpers ──────────────────────────────────────────────────

    [TestMethod]
    public void GridWorldLeft_ScrollMinusContentOffset_Difference()
    {
        Assert.AreEqual(90d, WorkflowSurfaceMath.GridWorldLeft(120, 30));
        Assert.AreEqual(90d, WorkflowSurfaceMath.GridWorldTop(120, 30));
    }

    [TestMethod]
    public void GridWorldLeft_NegativeContentOffset_ScrollSpaceWorldLeft()
    {
        // Overscrolled origin: content offset negative means world 0 sits below the scroll origin,
        // so the visible world-left is positive scroll minus that offset.
        Assert.AreEqual(150d, WorkflowSurfaceMath.GridWorldLeft(120, -30));
    }

    [TestMethod]
    public void GridX_ContentRectOrigin_WorldToScreen()
    {
        // worldLeft 100, ruler-less content area (contentRect.X == 0): screen = world − worldLeft.
        Assert.AreEqual(60d, WorkflowSurfaceMath.GridX(160, 100, 0));
    }

    [TestMethod]
    public void GridX_RulerInsetContentRect_AddsBandTranslation()
    {
        // Ruler reserve of 36 on the left: world 40 lands at 36 + (40 − worldLeft).
        Assert.AreEqual(76d, WorkflowSurfaceMath.GridX(40, 0, 36));
        Assert.AreEqual(76d, WorkflowSurfaceMath.GridY(40, 0, 36));
    }

    [TestMethod]
    public void GridFirstLine_PositiveWorldLeft_NextGridLineDown()
    {
        Assert.AreEqual(80d, WorkflowSurfaceMath.GridFirstLine(93, 40));
        Assert.AreEqual(120d, WorkflowSurfaceMath.GridFirstLine(120, 40));
    }

    [TestMethod]
    public void GridFirstLine_NegativeWorldLeft_AlignedBelowWorldLeft()
    {
        // floor(−93/40) = −3 → −120; −5/40 floors to −1 → −40. Matches the inline
        // Math.Floor(worldLeft/GridStep)*GridStep formula every decorator previously carried.
        Assert.AreEqual(-120d, WorkflowSurfaceMath.GridFirstLine(-93, 40));
        Assert.AreEqual(-40d, WorkflowSurfaceMath.GridFirstLine(-5, 40));
        Assert.AreEqual(-40d, WorkflowSurfaceMath.GridFirstLine(-40, 40));
    }

    // ── Clamp / scroll-max helpers ───────────────────────────────────────────

    [TestMethod]
    public void ClampValue_BelowRange_ReturnsMin()
    {
        Assert.AreEqual(0d, WorkflowSurfaceMath.ClampValue(-5, 0, 100));
    }

    [TestMethod]
    public void ClampValue_AboveRange_ReturnsMax()
    {
        Assert.AreEqual(100d, WorkflowSurfaceMath.ClampValue(150, 0, 100));
    }

    [TestMethod]
    public void ClampValue_InsideRange_ReturnsValue()
    {
        Assert.AreEqual(50d, WorkflowSurfaceMath.ClampValue(50, 0, 100));
    }

    [TestMethod]
    public void ClampValue_MinAboveMax_ResolvesToMin()
    {
        // Degenerate range must not throw; the formula Math.Max(min, Math.Min(v, max)) collapses to min.
        Assert.AreEqual(100d, WorkflowSurfaceMath.ClampValue(5, 100, 10));
    }

    [TestMethod]
    public void ScrollMax_ExtentLargerThanViewport_Difference()
    {
        Assert.AreEqual(200d, WorkflowSurfaceMath.ScrollMax(1000, 800));
    }

    [TestMethod]
    public void ScrollMax_ContentSmallerThanViewport_Zero()
    {
        Assert.AreEqual(0d, WorkflowSurfaceMath.ScrollMax(100, 300));
    }

    // ── Minimap mapping ──────────────────────────────────────────────────────

    [TestMethod]
    public void MinimapFit_HeightBinding_ScalesAndCentersWidth()
    {
        // scale 600/200 = 3 binds (width draw 1000 would allow 10); content 100×200 → 300×600 in a
        // 1000×600 draw, so the width leftover (700) is centered → originX 360, originY = padding.
        var (ox, oy, scale) = WorkflowSurfaceMath.MinimapFit(100, 200, 1000, 600, 10);
        Assert.AreEqual(3d, scale);
        Assert.AreEqual(360d, ox);
        Assert.AreEqual(10d, oy);
    }

    [TestMethod]
    public void MinimapFit_EmptyContent_GuardsScaleAgainstDivideByZero()
    {
        // Math.Max(1, content) floor means a zero-size workflow fits at the min draw scale, never NaN.
        var (ox, oy, scale) = WorkflowSurfaceMath.MinimapFit(0, 0, 1000, 600, 10);
        Assert.AreEqual(600d, scale);
        Assert.AreEqual(510d, ox);
        Assert.AreEqual(310d, oy);
    }

    [TestMethod]
    public void MinimapLocal_WorldToMinimap_ThroughFit()
    {
        var (lx, ly) = WorkflowSurfaceMath.MinimapLocal(50, 80, 5, 6, 10, 20, 2);
        Assert.AreEqual(100d, lx);
        Assert.AreEqual(168d, ly);
    }

    [TestMethod]
    public void MinimapViewportRect_ScaledSizeAndClampedPosition()
    {
        // Viewport local origin lands at 360, but size 300×600 leaves only 0 px on the right/bottom,
        // so both axes clamp the position inward to keep the block inside the 1000×600 minimap.
        var (x, y, w, h) = WorkflowSurfaceMath.MinimapViewportRect(
            360, 10, 3, 0, 0, 100, 200, 0, 0, 1000, 600, 4);
        Assert.AreEqual(360d, x);
        Assert.AreEqual(0d, y);
        Assert.AreEqual(300d, w);
        Assert.AreEqual(600d, h);
    }

    [TestMethod]
    public void MinimapViewportRect_TinyViewport_FloorsToMinRectSize()
    {
        var (x, y, w, h) = WorkflowSurfaceMath.MinimapViewportRect(
            0, 0, 1, 40, 40, 1, 1, 0, 0, 100, 100, 4);
        Assert.AreEqual(4d, w);
        Assert.AreEqual(4d, h);
        Assert.AreEqual(40d, x);
        Assert.AreEqual(40d, y);
    }

    [TestMethod]
    public void MinimapToWorld_InvertsLocalMapping()
    {
        // (local − origin)/scale + contentLeft round-trips the MinimapLocal sample.
        var (wx, wy) = WorkflowSurfaceMath.MinimapToWorld(100, 168, 10, 20, 2, 5, 6);
        Assert.AreEqual(50d, wx);
        Assert.AreEqual(80d, wy);
    }

    [TestMethod]
    public void MinimapToScroll_CentersWorldOnViewport()
    {
        // scroll = world − viewport/2 + contentOffset; may be negative — the caller feeds the result
        // into ClampScrollOffset, which expands the canvas for the overshoot.
        var (sx, sy) = WorkflowSurfaceMath.MinimapToScroll(300, 200, 800, 600, 40, 30);
        Assert.AreEqual(-60d, sx);
        Assert.AreEqual(-70d, sy);
    }

    [TestMethod]
    public void MinThumbSize_TinyThumb_EnforcesMinimum()
    {
        Assert.AreEqual(2d, WorkflowSurfaceMath.MinThumbSize(4, 0.25, 2));
        Assert.AreEqual(1d, WorkflowSurfaceMath.MinThumbSize(4, 0.25, 1));
    }

    [TestMethod]
    public void MinThumbSize_ScaledSize_WhenAboveMinimum()
    {
        Assert.AreEqual(5d, WorkflowSurfaceMath.MinThumbSize(10, 0.5, 4));
    }

    // ── Screen ↔ world conversion ───────────────────────────────────────────

    [TestMethod]
    public void ToWorld_ScrollMinusActualOffset_WorldSpace()
    {
        Assert.AreEqual(90d, WorkflowSurfaceMath.ToWorld(120, 30));
        // Overscrolled origin (negative offset): world 0 sits below the scroll origin.
        Assert.AreEqual(80d, WorkflowSurfaceMath.ToWorld(50, -30));
    }

    [TestMethod]
    public void ToScreen_AddsActualOffset()
    {
        var layout = new CanvasLayout { NegativeOffset = new Offset(40, 30) };
        var screen = WorkflowSurfaceMath.ToScreen(60, 50, layout);
        Assert.AreEqual(100d, screen.Horizontal);
        Assert.AreEqual(80d, screen.Vertical);
    }

    [TestMethod]
    public void ToWorldAnchor_SubtractsActualOffsetAndKeepsLayer()
    {
        var layout = new CanvasLayout { NegativeOffset = new Offset(40, 30) };
        var anchor = WorkflowSurfaceMath.ToWorldAnchor(200, 160, 2, layout);
        Assert.AreEqual(160d, anchor.Horizontal);
        Assert.AreEqual(130d, anchor.Vertical);
        Assert.AreEqual(2, anchor.Layer);
    }

    // ── Overscroll clamp (canvas expansion) ─────────────────────────────────

    [TestMethod]
    public void ClampScrollOffset_NegativeDesired_ReturnsZeroExpandsNegativeOffset()
    {
        var layout = new CanvasLayout();
        var scroll = WorkflowSurfaceMath.ClampScrollOffset(-50, 1000, layout, horizontal: true);
        Assert.AreEqual(0d, scroll);
        Assert.AreEqual(50d, layout.NegativeOffset.Horizontal);
        Assert.AreEqual(0d, layout.NegativeOffset.Vertical);
        Assert.AreEqual(0d, layout.PositiveOffset.Horizontal);
    }

    [TestMethod]
    public void ClampScrollOffset_PastMax_ReturnsMaxExpandsPositiveOffsetOnAxis()
    {
        var layout = new CanvasLayout();
        var scroll = WorkflowSurfaceMath.ClampScrollOffset(1100, 1000, layout, horizontal: false);
        Assert.AreEqual(1000d, scroll);
        Assert.AreEqual(0d, layout.PositiveOffset.Horizontal);
        Assert.AreEqual(100d, layout.PositiveOffset.Vertical);
        Assert.AreEqual(0d, layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void ClampScrollOffset_WithinRange_ReturnsDesiredWithoutMutation()
    {
        var layout = new CanvasLayout();
        var scroll = WorkflowSurfaceMath.ClampScrollOffset(400, 1000, layout, horizontal: true);
        Assert.AreEqual(400d, scroll);
        Assert.AreEqual(0d, layout.NegativeOffset.Horizontal);
        Assert.AreEqual(0d, layout.PositiveOffset.Horizontal);
    }

    [TestMethod]
    public void ClampScrollOffset_OvershootBelowThreshold_ClampsWithoutExpanding()
    {
        var layout = new CanvasLayout();
        Assert.AreEqual(0d, WorkflowSurfaceMath.ClampScrollOffset(-5, 1000, layout, horizontal: true, threshold: 20));
        Assert.AreEqual(0d, layout.NegativeOffset.Horizontal);
        Assert.AreEqual(1000d, WorkflowSurfaceMath.ClampScrollOffset(1004, 1000, layout, horizontal: false, threshold: 20));
        Assert.AreEqual(0d, layout.PositiveOffset.Vertical);
    }

    // ── Slot anchors ────────────────────────────────────────────────────────

    [TestMethod]
    public void SlotAnchorFromVisualCenter_SubtractsActualOffset()
    {
        var layout = new CanvasLayout { NegativeOffset = new Offset(40, 30) };
        var anchor = WorkflowSurfaceMath.SlotAnchorFromVisualCenter(200, 160, 3, layout);
        Assert.AreEqual(160d, anchor.Horizontal);
        Assert.AreEqual(130d, anchor.Vertical);
        Assert.AreEqual(3, anchor.Layer);
    }

    [TestMethod]
    public void SlotAnchorFromNode_AddsLocalOffsetToNodeAnchor()
    {
        var anchor = WorkflowSurfaceMath.SlotAnchorFromNode(100, 50, 10, -5, 1);
        Assert.AreEqual(110d, anchor.Horizontal);
        Assert.AreEqual(45d, anchor.Vertical);
        Assert.AreEqual(1, anchor.Layer);
    }

    [TestMethod]
    public void SlotAnchorFromCanvasLocal_IsIdentityOnCanvasCoordinates()
    {
        var anchor = WorkflowSurfaceMath.SlotAnchorFromCanvasLocal(200, 160, 4);
        Assert.AreEqual(200d, anchor.Horizontal);
        Assert.AreEqual(160d, anchor.Vertical);
        Assert.AreEqual(4, anchor.Layer);
    }

    [TestMethod]
    public void SlotAnchor_VariantChoiceShiftsByActualOffset()
    {
        // Picking the wrong variant is the documented silent-offset bug: a canvas-local center fed
        // through the screen-space contract lands ActualOffset away from the node's slot.
        var layout = new CanvasLayout { NegativeOffset = new Offset(40, 30) };
        var canvasLocal = WorkflowSurfaceMath.SlotAnchorFromCanvasLocal(200, 160, 3);
        var visual = WorkflowSurfaceMath.SlotAnchorFromVisualCenter(200, 160, 3, layout);
        Assert.AreEqual(200d, canvasLocal.Horizontal);
        Assert.AreEqual(160d, visual.Horizontal);
        Assert.AreEqual(40d, canvasLocal.Horizontal - visual.Horizontal);
    }

    // ── WorkflowBounds ──────────────────────────────────────────────────────

    [TestMethod]
    public void WorkflowBounds_FromNode_ExposesEdges()
    {
        var b = WorkflowBounds.FromNode(10, 20, 30, 40);
        Assert.AreEqual(10d, b.Left);
        Assert.AreEqual(20d, b.Top);
        Assert.AreEqual(30d, b.Width);
        Assert.AreEqual(40d, b.Height);
        Assert.AreEqual(40d, b.Right);
        Assert.AreEqual(60d, b.Bottom);
        Assert.IsFalse(b.IsEmpty);
    }

    [TestMethod]
    public void WorkflowBounds_ZeroWidth_IsEmpty()
    {
        var b = WorkflowBounds.FromNode(0, 0, 0, 10);
        Assert.IsTrue(b.IsEmpty);
        var negative = WorkflowBounds.FromNode(0, 0, -5, 10);
        Assert.IsTrue(negative.IsEmpty);
    }

    [TestMethod]
    public void WorkflowBounds_FromNodes_SpansAllInputs()
    {
        var b = WorkflowBounds.FromNodes(
        [
            (X: -20d, Y: -10d, W: 10d, H: 10d),
            (X: 10d, Y: 5d, W: 5d, H: 5d),
        ]);
        Assert.AreEqual(-20d, b.Left);
        Assert.AreEqual(-10d, b.Top);
        Assert.AreEqual(15d, b.Right);   // -20+10 == -10 < 10+5 == 15
        Assert.AreEqual(10d, b.Bottom);  // -10+10 == 0 < 5+5 == 10
        Assert.AreEqual(35d, b.Width);
        Assert.AreEqual(20d, b.Height);
    }

    [TestMethod]
    public void WorkflowBounds_FromNodes_EmptyIsEmptyBounds()
    {
        var b = WorkflowBounds.FromNodes(System.Array.Empty<(double X, double Y, double W, double H)>());
        Assert.IsTrue(b.IsEmpty);
        Assert.AreEqual(0d, b.Width);
        Assert.AreEqual(0d, b.Height);
    }

    [TestMethod]
    public void WorkflowBounds_Union_OverlappingMergesBounding()
    {
        var b = WorkflowBounds.Union(
            WorkflowBounds.FromNode(0, 0, 10, 10),
            WorkflowBounds.FromNode(5, 5, 10, 10));
        Assert.AreEqual(0d, b.Left);
        Assert.AreEqual(0d, b.Top);
        Assert.AreEqual(15d, b.Width);
        Assert.AreEqual(15d, b.Height);
    }

    [TestMethod]
    public void WorkflowBounds_Union_EmptySideReturnsOther()
    {
        var other = WorkflowBounds.FromNode(3, 4, 5, 6);
        var b = WorkflowBounds.Union(default, other);
        Assert.AreEqual(3d, b.Left);
        Assert.AreEqual(4d, b.Top);
        Assert.AreEqual(5d, b.Width);
        Assert.AreEqual(6d, b.Height);
    }
}
