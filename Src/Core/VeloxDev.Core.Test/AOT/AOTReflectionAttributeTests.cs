using VeloxDev.AOT;

namespace VeloxDev.Core.Test.AOT;

[TestClass]
public class AOTReflectionAttributeTests
{
    [TestMethod]
    public void DefaultValues()
    {
        var attr = new AOTReflectionAttribute();
        Assert.AreEqual("Auto", attr.Namespace);
        Assert.IsTrue(attr.IncludeConstructors);
        Assert.IsTrue(attr.IncludeMethods);
        Assert.IsTrue(attr.IncludeProperties);
        Assert.IsTrue(attr.IncludeFields);
    }

    [TestMethod]
    public void CustomValues()
    {
        var attr = new AOTReflectionAttribute(
            Namespace: "MyNamespace",
            Constructors: false,
            Methods: true,
            Properties: false,
            Fields: true);
        Assert.AreEqual("MyNamespace", attr.Namespace);
        Assert.IsFalse(attr.IncludeConstructors);
        Assert.IsTrue(attr.IncludeMethods);
        Assert.IsFalse(attr.IncludeProperties);
        Assert.IsTrue(attr.IncludeFields);
    }

    [TestMethod]
    public void AttributeUsage_ClassAndStruct()
    {
        var usage = (AttributeUsageAttribute?)Attribute.GetCustomAttribute(
            typeof(AOTReflectionAttribute), typeof(AttributeUsageAttribute));
        Assert.IsNotNull(usage);
        Assert.IsTrue(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.IsTrue(usage.ValidOn.HasFlag(AttributeTargets.Struct));
        Assert.IsFalse(usage.AllowMultiple);
        Assert.IsFalse(usage.Inherited);
    }
}
