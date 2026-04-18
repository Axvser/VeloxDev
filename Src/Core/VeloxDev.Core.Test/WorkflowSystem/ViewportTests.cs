using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class ViewportTests
{
    [TestMethod]
    public void Constructor_SetsAllFields()
    {
        var v = new Viewport(10, 20, 100, 200);
        Assert.AreEqual(10d, v.Horizontal);
        Assert.AreEqual(20d, v.Vertical);
        Assert.AreEqual(100d, v.Width);
        Assert.AreEqual(200d, v.Height);
    }

    [TestMethod]
    public void Right_ReturnsHorizontalPlusWidth()
    {
        var v = new Viewport(10, 0, 100, 50);
        Assert.AreEqual(110d, v.Right);
    }

    [TestMethod]
    public void Bottom_ReturnsVerticalPlusHeight()
    {
        var v = new Viewport(0, 20, 50, 200);
        Assert.AreEqual(220d, v.Bottom);
    }

    [TestMethod]
    public void IsEmpty_ZeroWidth_ReturnsTrue()
    {
        Assert.IsTrue(new Viewport(0, 0, 0, 100).IsEmpty);
    }

    [TestMethod]
    public void IsEmpty_ZeroHeight_ReturnsTrue()
    {
        Assert.IsTrue(new Viewport(0, 0, 100, 0).IsEmpty);
    }

    [TestMethod]
    public void IsEmpty_NegativeSize_ReturnsTrue()
    {
        Assert.IsTrue(new Viewport(0, 0, -1, 100).IsEmpty);
    }

    [TestMethod]
    public void IsEmpty_ValidSize_ReturnsFalse()
    {
        Assert.IsFalse(new Viewport(0, 0, 100, 100).IsEmpty);
    }

    [TestMethod]
    public void IntersectsWith_Overlapping_ReturnsTrue()
    {
        var a = new Viewport(0, 0, 100, 100);
        var b = new Viewport(50, 50, 100, 100);
        Assert.IsTrue(a.IntersectsWith(b));
        Assert.IsTrue(b.IntersectsWith(a));
    }

    [TestMethod]
    public void IntersectsWith_NonOverlapping_ReturnsFalse()
    {
        var a = new Viewport(0, 0, 50, 50);
        var b = new Viewport(100, 100, 50, 50);
        Assert.IsFalse(a.IntersectsWith(b));
    }

    [TestMethod]
    public void IntersectsWith_Adjacent_ReturnsFalse()
    {
        var a = new Viewport(0, 0, 50, 50);
        var b = new Viewport(50, 0, 50, 50);
        Assert.IsFalse(a.IntersectsWith(b));
    }

    [TestMethod]
    public void Contains_Point_Inside_ReturnsTrue()
    {
        var v = new Viewport(10, 20, 100, 200);
        Assert.IsTrue(v.Contains(50, 100));
    }

    [TestMethod]
    public void Contains_Point_Outside_ReturnsFalse()
    {
        var v = new Viewport(10, 20, 100, 200);
        Assert.IsFalse(v.Contains(0, 0));
        Assert.IsFalse(v.Contains(200, 300));
    }

    [TestMethod]
    public void Contains_Point_OnEdge_IncludesTopLeft()
    {
        var v = new Viewport(10, 20, 100, 200);
        Assert.IsTrue(v.Contains(10, 20));
    }

    [TestMethod]
    public void Contains_Viewport_FullyInside_ReturnsTrue()
    {
        var outer = new Viewport(0, 0, 200, 200);
        var inner = new Viewport(10, 10, 50, 50);
        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Viewport_PartiallyOutside_ReturnsFalse()
    {
        var outer = new Viewport(0, 0, 100, 100);
        var inner = new Viewport(50, 50, 100, 100);
        Assert.IsFalse(outer.Contains(inner));
    }

    [TestMethod]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Viewport(1, 2, 3, 4);
        var b = new Viewport(1, 2, 3, 4);
        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Viewport(1, 2, 3, 4);
        var b = new Viewport(5, 6, 7, 8);
        Assert.IsFalse(a.Equals(b));
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void ToString_ReturnsExpectedFormat()
    {
        var v = new Viewport(1, 2, 3, 4);
        Assert.AreEqual("Viewport(1, 2, 3, 4)", v.ToString());
    }
}
