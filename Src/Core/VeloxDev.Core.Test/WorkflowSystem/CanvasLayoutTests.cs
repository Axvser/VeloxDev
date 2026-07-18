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
}
