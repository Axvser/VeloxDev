using VeloxDev.DynamicTheme;

namespace VeloxDev.Razor.Headless.Tests.Tests;

// Razor 只有四个转换器；一次覆盖完。
[TestClass]
public sealed class ThemeValueConverterTests
{
    [TestMethod]
    public void Scalars_Convert()
    {
        Assert.AreEqual(1.5d, new DoubleConverter().Convert(typeof(double), "P", ["1.5"]));
        Assert.AreEqual(3, new IntConverter().Convert(typeof(int), "P", ["3"]));
        Assert.IsTrue((bool)new BoolConverter().Convert(typeof(bool), "P", ["true"])!);
        Assert.IsFalse((bool)new BoolConverter().Convert(typeof(bool), "P", ["false"])!);
        Assert.IsTrue((bool)new BoolConverter().Convert(typeof(bool), "P", [1])!);
        Assert.AreEqual("x", new StringConverter().Convert(typeof(string), "P", ["x"]));
    }

    [TestMethod]
    public void Converters_ReturnNullForMissingInput()
    {
        Assert.IsNull(new DoubleConverter().Convert(typeof(double), "P", null!));
        Assert.IsNull(new IntConverter().Convert(typeof(int), "P", null!));
        Assert.IsNull(new BoolConverter().Convert(typeof(bool), "P", null!));
        Assert.IsNull(new StringConverter().Convert(typeof(string), "P", null!));
        Assert.IsNull(new BoolConverter().Convert(typeof(bool), "P", ["not-bool"]));
    }
}
