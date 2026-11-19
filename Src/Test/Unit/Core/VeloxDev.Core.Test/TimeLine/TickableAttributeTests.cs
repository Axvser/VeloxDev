using VeloxDev.TimeLine;

namespace VeloxDev.Core.Test.TimeLine;

[TestClass]
public class TickableAttributeTests
{
    [TestMethod]
    public void AttributeUsage_ClassOnly()
    {
        var usage = (AttributeUsageAttribute?)Attribute.GetCustomAttribute(
            typeof(TickableAttribute), typeof(AttributeUsageAttribute));
        Assert.IsNotNull(usage);
        Assert.AreEqual(AttributeTargets.Class, usage.ValidOn);
        Assert.IsFalse(usage.AllowMultiple);
        Assert.IsFalse(usage.Inherited);
    }

    [TestMethod]
    public void CanInstantiate()
    {
        var attr = new TickableAttribute();
        Assert.IsNotNull(attr);
    }
}
