using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class CanvasLayoutTests
{
    [TestMethod]
    public void DefaultValues()
    {
        var layout = new CanvasLayout();
        Assert.AreEqual(1920d, layout.OriginSize.Width);
        Assert.AreEqual(1080d, layout.OriginSize.Height);
        Assert.AreEqual(0d, layout.PositiveOffset.Horizontal);
        Assert.AreEqual(0d, layout.PositiveOffset.Vertical);
        Assert.AreEqual(0d, layout.NegativeOffset.Horizontal);
        Assert.AreEqual(0d, layout.NegativeOffset.Vertical);
    }

    [TestMethod]
    public void ActualSize_ComputedFromOriginAndOffsets()
    {
        var layout = new CanvasLayout();
        layout.PositiveOffset = new Offset(100, 50);
        layout.NegativeOffset = new Offset(200, 80);
        Assert.AreEqual(1920 + 100 + 200, layout.ActualSize.Width);
        Assert.AreEqual(1080 + 50 + 80, layout.ActualSize.Height);
    }

    [TestMethod]
    public void ActualOffset_EqualsNegativeOffset()
    {
        var layout = new CanvasLayout();
        layout.NegativeOffset = new Offset(50, 30);
        Assert.AreEqual(50d, layout.ActualOffset.Horizontal);
        Assert.AreEqual(30d, layout.ActualOffset.Vertical);
    }

    [TestMethod]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new CanvasLayout();
        var b = new CanvasLayout();
        Assert.IsTrue(a.Equals(b));
    }

    [TestMethod]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new CanvasLayout();
        var b = new CanvasLayout { PositiveOffset = new Offset(10, 10) };
        Assert.IsFalse(a.Equals(b));
    }

    [TestMethod]
    public void Equals_Null_ReturnsFalse()
    {
        var layout = new CanvasLayout();
        Assert.IsFalse(layout.Equals((CanvasLayout?)null));
        Assert.IsFalse(layout.Equals((object?)null));
    }

    [TestMethod]
    public void Equals_NonCanvasLayout_ReturnsFalse()
    {
        var layout = new CanvasLayout();
        Assert.IsFalse(layout.Equals("not a layout"));
    }

    [TestMethod]
    public void GetHashCode_SameValues_Equal()
    {
        var a = new CanvasLayout();
        var b = new CanvasLayout();
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Clone_IsEqualButIndependent()
    {
        var layout = new CanvasLayout
        {
            OriginSize = new Size(800, 600),
            PositiveOffset = new Offset(10, 20),
            NegativeOffset = new Offset(30, 40)
        };
        var clone = (CanvasLayout)layout.Clone();
        Assert.IsTrue(layout.Equals(clone));

        clone.OriginSize = new Size(1024, 768);
        Assert.IsFalse(layout.Equals(clone));
    }

    [TestMethod]
    public void OriginSizeChange_TriggersUpdate()
    {
        var layout = new CanvasLayout();
        layout.OriginSize = new Size(3000, 2000);
        Assert.AreEqual(3000d, layout.ActualSize.Width);
        Assert.AreEqual(2000d, layout.ActualSize.Height);
    }

    [TestMethod]
    public void ViewportOffset_DefaultIsZero()
    {
        var layout = new CanvasLayout();
        Assert.AreEqual(0d, layout.ViewportOffset.Horizontal);
        Assert.AreEqual(0d, layout.ViewportOffset.Vertical);
    }

    [TestMethod]
    public void ViewportOffset_CanBeSet()
    {
        var layout = new CanvasLayout();
        layout.ViewportOffset = new Offset(150, 300);
        Assert.AreEqual(150d, layout.ViewportOffset.Horizontal);
        Assert.AreEqual(300d, layout.ViewportOffset.Vertical);
    }

    [TestMethod]
    public void ViewportOffset_DoesNotAffectActualSize()
    {
        var layout = new CanvasLayout();
        layout.ViewportOffset = new Offset(200, 400);
        Assert.AreEqual(1920d, layout.ActualSize.Width);
        Assert.AreEqual(1080d, layout.ActualSize.Height);
    }

    [TestMethod]
    public void ViewportOffset_DoesNotAffectActualOffset()
    {
        var layout = new CanvasLayout();
        layout.NegativeOffset = new Offset(50, 30);
        layout.ViewportOffset = new Offset(200, 400);
        Assert.AreEqual(50d, layout.ActualOffset.Horizontal);
        Assert.AreEqual(30d, layout.ActualOffset.Vertical);
    }

    [TestMethod]
    public void Clone_PreservesViewportOffset()
    {
        var layout = new CanvasLayout
        {
            OriginSize = new Size(800, 600),
            PositiveOffset = new Offset(10, 20),
            NegativeOffset = new Offset(30, 40),
            ViewportOffset = new Offset(100, 200)
        };
        var clone = (CanvasLayout)layout.Clone();
        Assert.AreEqual(100d, clone.ViewportOffset.Horizontal);
        Assert.AreEqual(200d, clone.ViewportOffset.Vertical);

        clone.ViewportOffset = new Offset(300, 400);
        Assert.AreEqual(100d, layout.ViewportOffset.Horizontal);
        Assert.AreEqual(200d, layout.ViewportOffset.Vertical);
    }

    [TestMethod]
    public void AdaptTo_PreservesViewportOffset()
    {
        var layout = new CanvasLayout
        {
            ViewportOffset = new Offset(100, 200)
        };
        var adapted = layout.AdaptTo(new Size(3000, 2000));
        Assert.AreEqual(100d, adapted.ViewportOffset.Horizontal);
        Assert.AreEqual(200d, adapted.ViewportOffset.Vertical);
    }

    [TestMethod]
    public void Scale_DefaultIsOne()
    {
        var layout = new CanvasLayout();
        Assert.AreEqual(1d, layout.Scale.Horizontal);
        Assert.AreEqual(1d, layout.Scale.Vertical);
    }

    [TestMethod]
    public void ScaleChange_RaisesPropertyChanged_ForScale()
    {
        var layout = new CanvasLayout();
        var raised = false;
        layout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanvasLayout.Scale)) raised = true;
        };
        layout.Scale = new Scale(2, 2);
        Assert.IsTrue(raised, "the Scale PropertyChanged is the view-dirty signal the scale attached property binds to");
    }

    [TestMethod]
    public void ScaleChange_DoesNotAffectActualSizeOrOffset()
    {
        // World-origin mode is pinned here: Scale must not change the extent (nodes collapse per-node,
        // the canvas stays the same size). ViewportCenter mode does change the extent by design.
        var layout = new CanvasLayout { ZoomCenter = ZoomCenter.WorldOrigin };
        layout.NegativeOffset = new Offset(50, 30);
        var widthBefore = layout.ActualSize.Width;
        var heightBefore = layout.ActualSize.Height;
        layout.Scale = new Scale(2, 2);
        Assert.AreEqual(widthBefore, layout.ActualSize.Width);
        Assert.AreEqual(heightBefore, layout.ActualSize.Height);
        Assert.AreEqual(50d, layout.ActualOffset.Horizontal);
        Assert.AreEqual(30d, layout.ActualOffset.Vertical);
    }

    [TestMethod]
    public void Equals_DifferentScale_ReturnsFalse()
    {
        var a = new CanvasLayout();
        var b = new CanvasLayout { Scale = new Scale(2, 2) };
        Assert.IsFalse(a.Equals(b));
    }

    [TestMethod]
    public void Clone_PreservesScale()
    {
        var layout = new CanvasLayout { Scale = new Scale(1.5, 2.5) };
        var clone = (CanvasLayout)layout.Clone();
        Assert.AreEqual(new Scale(1.5, 2.5), clone.Scale);

        clone.Scale = new Scale(3, 3);
        Assert.AreEqual(1.5, layout.Scale.Horizontal);
        Assert.AreEqual(2.5, layout.Scale.Vertical);
    }

    [TestMethod]
    public void AdaptTo_PreservesScale()
    {
        var layout = new CanvasLayout { Scale = new Scale(2, 2) };
        var adapted = layout.AdaptTo(new Size(3000, 2000));
        Assert.AreEqual(2d, adapted.Scale.Horizontal);
        Assert.AreEqual(2d, adapted.Scale.Vertical);
    }

    // ── ZoomCenter + CollapsePivot ───────────────────────────────────────────

    [TestMethod]
    public void ZoomCenter_DefaultIsViewportCenter()
    {
        // The CanvasLayout default is ViewportCenter (the product default the user selected).
        var layout = new CanvasLayout();
        Assert.AreEqual(ZoomCenter.ViewportCenter, layout.ZoomCenter);
    }

    [TestMethod]
    public void ZoomCenter_CanBeSet()
    {
        var layout = new CanvasLayout { ZoomCenter = ZoomCenter.WorldOrigin };
        Assert.AreEqual(ZoomCenter.WorldOrigin, layout.ZoomCenter);
    }

    [TestMethod]
    public void CollapsePivot_DefaultIsZero()
    {
        var layout = new CanvasLayout();
        Assert.AreEqual(0d, layout.CollapsePivot.Horizontal);
        Assert.AreEqual(0d, layout.CollapsePivot.Vertical);
    }

    [TestMethod]
    public void CollapsePivot_CanBeSet()
    {
        var layout = new CanvasLayout();
        layout.CollapsePivot = new Anchor(270, 190, 0);
        Assert.AreEqual(270d, layout.CollapsePivot.Horizontal);
        Assert.AreEqual(190d, layout.CollapsePivot.Vertical);
    }

    [TestMethod]
    public void CollapsePivot_DoesNotChangeActualSize_OrUpdateIt()
    {
        var layout = new CanvasLayout();
        layout.NegativeOffset = new Offset(50, 30);
        var widthBefore = layout.ActualSize.Width;
        var heightBefore = layout.ActualSize.Height;
        var raised = false;
        layout.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CanvasLayout.ActualSize)) raised = true; };

        // The adapter writes the pivot immediately before Scale in one gesture; a pivot-only change
        // must not resize the canvas or re-raise ActualSize.
        layout.CollapsePivot = new Anchor(270, 190, 0);
        Assert.AreEqual(widthBefore, layout.ActualSize.Width);
        Assert.AreEqual(heightBefore, layout.ActualSize.Height);
        Assert.IsFalse(raised, "a pivot-only write must not re-raise ActualSize");
    }

    [TestMethod]
    public void Equals_DifferentZoomCenter_ReturnsFalse()
    {
        var a = new CanvasLayout { ZoomCenter = ZoomCenter.WorldOrigin };
        var b = new CanvasLayout { ZoomCenter = ZoomCenter.ViewportCenter };
        Assert.IsFalse(a.Equals(b));
    }

    [TestMethod]
    public void Clone_PreservesZoomCenterAndPivot()
    {
        var layout = new CanvasLayout
        {
            ZoomCenter = ZoomCenter.ViewportCenter,
            CollapsePivot = new Anchor(270, 190, 0),
        };
        var clone = (CanvasLayout)layout.Clone();
        Assert.AreEqual(ZoomCenter.ViewportCenter, clone.ZoomCenter);
        Assert.AreEqual(270d, clone.CollapsePivot.Horizontal);
        Assert.AreEqual(190d, clone.CollapsePivot.Vertical);

        clone.CollapsePivot = new Anchor(1, 1, 0);
        Assert.AreEqual(270d, layout.CollapsePivot.Horizontal);
    }

    [TestMethod]
    public void ViewportCenter_ScaleHalf_ExtentCoversCollapsedContent()
    {
        // Zoom-in auto-extends: Scale 0.5 → collapsed content grows by 1/0.5 = 2×, so the extent is
        // base×2 = [0,3840]. ActualOffset stays == NegativeOffset (zoom never translates the canvas).
        var layout = new CanvasLayout
        {
            ZoomCenter = ZoomCenter.ViewportCenter,
            CollapsePivot = new Anchor(300, 200, 0),
            Scale = new Scale(0.5, 0.5),
        };
        Assert.AreEqual(1920d * 2, layout.ActualSize.Width);   // [0,3840]
        Assert.AreEqual(1080d * 2, layout.ActualSize.Height);  // [0,2160]
        Assert.AreEqual(0d, layout.ActualOffset.Horizontal);
        Assert.AreEqual(0d, layout.ActualOffset.Vertical);
    }

    [TestMethod]
    public void ViewportCenter_ScaleTwo_KeepsOriginExtent()
    {
        // Scale 2 collapses content toward the origin (which already fits), so the extent stays base.
        // The canvas geometry is identical to WorldOrigin mode — zoom is purely a scroll change.
        var layout = new CanvasLayout
        {
            ZoomCenter = ZoomCenter.ViewportCenter,
            CollapsePivot = new Anchor(300, 200, 0),
            Scale = new Scale(2, 2),
        };
        Assert.AreEqual(1920d, layout.ActualSize.Width);    // base
        Assert.AreEqual(1080d, layout.ActualSize.Height);   // base
        Assert.AreEqual(0d, layout.ActualOffset.Horizontal);
        Assert.AreEqual(0d, layout.ActualOffset.Vertical);
    }

    [TestMethod]
    public void ViewportCenter_ScaleOne_MatchesOriginExtent()
    {
        // At scale 1 the pivot-aware extent reduces exactly to the world-origin extent: switching the
        // enum while at rest must change nothing.
        var vc = new CanvasLayout
        {
            ZoomCenter = ZoomCenter.ViewportCenter,
            CollapsePivot = new Anchor(300, 200, 0),
        };
        var origin = new CanvasLayout { ZoomCenter = ZoomCenter.WorldOrigin };
        Assert.AreEqual(origin.ActualSize.Width, vc.ActualSize.Width);
        Assert.AreEqual(origin.ActualSize.Height, vc.ActualSize.Height);
        Assert.AreEqual(origin.ActualOffset.Horizontal, vc.ActualOffset.Horizontal);
        Assert.AreEqual(origin.ActualOffset.Vertical, vc.ActualOffset.Vertical);
    }

    [TestMethod]
    public void WorldOrigin_ScaleHalf_KeepsOriginExtent()
    {
        // World-origin mode is completely unaffected: extent is base*2, offset stays NegativeOffset.
        var layout = new CanvasLayout { Scale = new Scale(0.5, 0.5) };
        Assert.AreEqual(1920 * 2, layout.ActualSize.Width);
        Assert.AreEqual(1080 * 2, layout.ActualSize.Height);
        Assert.AreEqual(0d, layout.ActualOffset.Horizontal);
        Assert.AreEqual(0d, layout.ActualOffset.Vertical);
    }
}
