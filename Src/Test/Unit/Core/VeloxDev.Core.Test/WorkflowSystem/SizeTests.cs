using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class SizeTests
{
    [TestMethod]
    public void Constructor_DefaultValues_AllZero()
    {
        var s = new Size();
        Assert.AreEqual(0d, s.Width);
        Assert.AreEqual(0d, s.Height);
    }

    [TestMethod]
    public void Constructor_WithValues_SetsCorrectly()
    {
        var s = new Size(100.5, 200.3);
        Assert.AreEqual(100.5, s.Width);
        Assert.AreEqual(200.3, s.Height);
    }

    [TestMethod]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Size(10, 20);
        var b = new Size(10, 20);
        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Size(10, 20);
        var b = new Size(30, 40);
        Assert.IsFalse(a.Equals(b));
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void Equals_Null_ReturnsFalse()
    {
        var a = new Size(10, 20);
        Assert.IsFalse(a.Equals((Size?)null));
        Assert.IsFalse(a.Equals((object?)null));
    }

    [TestMethod]
    public void OperatorAdd_ReturnsSum()
    {
        var result = new Size(10, 20) + new Size(5, 15);
        Assert.AreEqual(15d, result.Width);
        Assert.AreEqual(35d, result.Height);
    }

    [TestMethod]
    public void OperatorSubtract_ReturnsDifference()
    {
        var result = new Size(10, 20) - new Size(3, 7);
        Assert.AreEqual(7d, result.Width);
        Assert.AreEqual(13d, result.Height);
    }

    [TestMethod]
    public void Clone_ReturnsEqualButDistinctInstance()
    {
        var a = new Size(50, 100);
        var clone = (Size)a.Clone();
        Assert.AreEqual(a, clone);
        Assert.AreNotSame(a, clone);
    }

    [TestMethod]
    public void ToString_ReturnsExpectedFormat()
    {
        var s = new Size(1.5, 2.5);
        Assert.AreEqual("Size(1.5,2.5)", s.ToString());
    }
}
