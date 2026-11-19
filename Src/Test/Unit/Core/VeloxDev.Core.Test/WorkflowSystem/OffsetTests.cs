using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class OffsetTests
{
    [TestMethod]
    public void Constructor_DefaultValues_AllZero()
    {
        var o = new Offset();
        Assert.AreEqual(0d, o.Horizontal);
        Assert.AreEqual(0d, o.Vertical);
    }

    [TestMethod]
    public void Constructor_WithValues_SetsCorrectly()
    {
        var o = new Offset(15.5, 25.3);
        Assert.AreEqual(15.5, o.Horizontal);
        Assert.AreEqual(25.3, o.Vertical);
    }

    [TestMethod]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Offset(1, 2);
        var b = new Offset(1, 2);
        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Offset(1, 2);
        var b = new Offset(3, 4);
        Assert.IsFalse(a.Equals(b));
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void Equals_Null_ReturnsFalse()
    {
        var a = new Offset(1, 2);
        Assert.IsFalse(a.Equals((Offset?)null));
        Assert.IsFalse(a.Equals((object?)null));
    }

    [TestMethod]
    public void OperatorAdd_ReturnsSum()
    {
        var result = new Offset(10, 20) + new Offset(5, 15);
        Assert.AreEqual(15d, result.Horizontal);
        Assert.AreEqual(35d, result.Vertical);
    }

    [TestMethod]
    public void OperatorSubtract_ReturnsDifference()
    {
        var result = new Offset(10, 20) - new Offset(3, 7);
        Assert.AreEqual(7d, result.Horizontal);
        Assert.AreEqual(13d, result.Vertical);
    }

    [TestMethod]
    public void Clone_ReturnsEqualButDistinctInstance()
    {
        var a = new Offset(50, 100);
        var clone = (Offset)a.Clone();
        Assert.AreEqual(a, clone);
        Assert.AreNotSame(a, clone);
    }

    [TestMethod]
    public void ToString_ReturnsExpectedFormat()
    {
        var o = new Offset(1.5, 2.5);
        Assert.AreEqual("Offset(1.5,2.5)", o.ToString());
    }
}
