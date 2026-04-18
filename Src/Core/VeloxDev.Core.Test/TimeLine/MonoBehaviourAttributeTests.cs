using VeloxDev.TimeLine;

namespace VeloxDev.Core.Test.TimeLine;

[TestClass]
public class MonoBehaviourAttributeTests
{
    [TestMethod]
    public void AttributeUsage_ClassOnly()
    {
        var usage = (AttributeUsageAttribute?)Attribute.GetCustomAttribute(
            typeof(MonoBehaviourAttribute), typeof(AttributeUsageAttribute));
        Assert.IsNotNull(usage);
        Assert.AreEqual(AttributeTargets.Class, usage.ValidOn);
        Assert.IsFalse(usage.AllowMultiple);
        Assert.IsFalse(usage.Inherited);
    }

    [TestMethod]
    public void CanInstantiate()
    {
        var attr = new MonoBehaviourAttribute();
        Assert.IsNotNull(attr);
    }
}
