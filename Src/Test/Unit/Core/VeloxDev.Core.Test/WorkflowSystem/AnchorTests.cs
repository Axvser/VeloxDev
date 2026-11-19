using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class AnchorTests
{
    [TestMethod]
    public void Constructor_DefaultValues_AllZero()
    {
        var anchor = new Anchor();
        Assert.AreEqual(0d, anchor.Horizontal);
        Assert.AreEqual(0d, anchor.Vertical);
        Assert.AreEqual(0, anchor.Layer);
    }

    [TestMethod]
    public void Constructor_WithValues_SetsCorrectly()
    {
        var anchor = new Anchor(10.5, 20.3, 3);
        Assert.AreEqual(10.5, anchor.Horizontal);
        Assert.AreEqual(20.3, anchor.Vertical);
        Assert.AreEqual(3, anchor.Layer);
    }

    [TestMethod]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Anchor(1, 2, 3);
        var b = new Anchor(1, 2, 3);
        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Anchor(1, 2, 3);
        var b = new Anchor(4, 5, 6);
        Assert.IsFalse(a.Equals(b));
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void Equals_Null_ReturnsFalse()
    {
        var a = new Anchor(1, 2, 3);
        Assert.IsFalse(a.Equals((Anchor?)null));
        Assert.IsFalse(a.Equals((object?)null));
    }

    [TestMethod]
    public void Equals_NonAnchorObject_ReturnsFalse()
    {
        var a = new Anchor(1, 2, 3);
        Assert.IsFalse(a.Equals("not an anchor"));
    }

    [TestMethod]
    public void OperatorAdd_ReturnsSum()
    {
        var a = new Anchor(1, 2, 3);
        var b = new Anchor(10, 20, 30);
        var result = a + b;
        Assert.AreEqual(11d, result.Horizontal);
        Assert.AreEqual(22d, result.Vertical);
        Assert.AreEqual(33, result.Layer);
    }

    [TestMethod]
    public void OperatorSubtract_ReturnsDifference()
    {
        var a = new Anchor(10, 20, 30);
        var b = new Anchor(1, 2, 3);
        var result = a - b;
        Assert.AreEqual(9d, result.Horizontal);
        Assert.AreEqual(18d, result.Vertical);
        Assert.AreEqual(27, result.Layer);
    }

    [TestMethod]
    public void Clone_ReturnsEqualButDistinctInstance()
    {
        var a = new Anchor(5, 10, 2);
        var clone = (Anchor)a.Clone();
        Assert.AreEqual(a, clone);
        Assert.AreNotSame(a, clone);
    }

    [TestMethod]
    public void ToString_ReturnsExpectedFormat()
    {
        var a = new Anchor(1.5, 2.5, 3);
        Assert.AreEqual("Anchor(1.5,2.5,3)", a.ToString());
    }
}
