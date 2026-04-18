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
}
