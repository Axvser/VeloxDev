using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class CellKeyTests
{
    [TestMethod]
    public void Constructor_SetsValues()
    {
        var key = new CellKey(3, 7);
        Assert.AreEqual(3, key.X);
        Assert.AreEqual(7, key.Y);
    }

    [TestMethod]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new CellKey(1, 2);
        var b = new CellKey(1, 2);
        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new CellKey(1, 2);
        var b = new CellKey(3, 4);
        Assert.IsFalse(a.Equals(b));
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void Equals_BoxedObject_Works()
    {
        var a = new CellKey(5, 10);
        object b = new CellKey(5, 10);
        Assert.IsTrue(a.Equals(b));
    }

    [TestMethod]
    public void Equals_NonCellKeyObject_ReturnsFalse()
    {
        var a = new CellKey(1, 2);
        Assert.IsFalse(a.Equals("not a cellkey"));
        Assert.IsFalse(a.Equals(null));
    }

    [TestMethod]
    public void ToString_ReturnsExpectedFormat()
    {
        var key = new CellKey(3, 7);
        Assert.AreEqual("CellKey(3, 7)", key.ToString());
    }
}
