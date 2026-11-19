using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class ScaleTests
{
    [TestMethod]
    public void Constructor_DefaultValues_AllOne()
    {
        var s = new Scale();
        Assert.AreEqual(1d, s.Horizontal);
        Assert.AreEqual(1d, s.Vertical);
    }

    [TestMethod]
    public void Constructor_WithValues_SetsCorrectly()
    {
        var s = new Scale(1.5, 2.5);
        Assert.AreEqual(1.5, s.Horizontal);
        Assert.AreEqual(2.5, s.Vertical);
    }

    [TestMethod]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Scale(1, 2);
        var b = new Scale(1, 2);
        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Scale(1, 2);
        var b = new Scale(3, 4);
        Assert.IsFalse(a.Equals(b));
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void Equals_Null_ReturnsFalse()
    {
        var a = new Scale(1, 2);
        Assert.IsFalse(a.Equals((Scale?)null));
        Assert.IsFalse(a.Equals((object?)null));
    }

    [TestMethod]
    public void OperatorAdd_ReturnsSum()
    {
        var result = new Scale(1, 2) + new Scale(0.5, 0.25);
        Assert.AreEqual(1.5, result.Horizontal);
        Assert.AreEqual(2.25, result.Vertical);
    }

    [TestMethod]
    public void OperatorSubtract_ReturnsDifference()
    {
        var result = new Scale(2, 3) - new Scale(0.5, 1);
        Assert.AreEqual(1.5, result.Horizontal);
        Assert.AreEqual(2d, result.Vertical);
    }

    [TestMethod]
    public void Clone_ReturnsEqualButDistinctInstance()
    {
        var a = new Scale(2, 4);
        var clone = (Scale)a.Clone();
        Assert.AreEqual(a, clone);
        Assert.AreNotSame(a, clone);
    }

    [TestMethod]
    public void ToString_ReturnsExpectedFormat()
    {
        var s = new Scale(1.5, 2.5);
        Assert.AreEqual("Scale(1.5,2.5)", s.ToString());
    }
}
