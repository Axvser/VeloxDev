using VeloxDev.DynamicTheme;

namespace VeloxDev.Core.Test.DynamicTheme;

[TestClass]
public class ThemeBasicsTests
{
    [TestMethod]
    public void Dark_ImplementsITheme()
    {
        Assert.IsTrue(typeof(ITheme).IsAssignableFrom(typeof(Dark)));
    }

    [TestMethod]
    public void Light_ImplementsITheme()
    {
        Assert.IsTrue(typeof(ITheme).IsAssignableFrom(typeof(Light)));
    }

    [TestMethod]
    public void ThemeManager_DefaultCurrent_IsDark()
    {
        Assert.AreEqual(typeof(Dark), ThemeManager.Current);
    }

    [TestMethod]
    public void ThemeManager_SetCurrent_Changes()
    {
        var original = ThemeManager.Current;
        try
        {
            ThemeManager.SetCurrent<Light>();
            Assert.AreEqual(typeof(Light), ThemeManager.Current);
        }
        finally
        {
            // Restore
            if (original == typeof(Dark))
                ThemeManager.SetCurrent<Dark>();
            else
                ThemeManager.SetCurrent<Light>();
        }
    }

    [TestMethod]
    public void StartModel_DefaultIsCache()
    {
        Assert.AreEqual(StartModel.Cache, ThemeManager.StartModel);
    }

    [TestMethod]
    public void StartModel_FlagsEnum()
    {
        var combined = StartModel.Reflect | StartModel.Cache;
        Assert.IsTrue(combined.HasFlag(StartModel.Reflect));
        Assert.IsTrue(combined.HasFlag(StartModel.Cache));
    }

    [TestMethod]
    public void ThemeConfigAttribute_2Themes_CanBeInstantiated()
    {
        // Verify generic attribute can be constructed
        var attr = new ThemeConfigAttribute<DummyConverter, Dark, Light>("TestProp", [1, 2], [3, 4]);
        Assert.IsNotNull(attr);
    }

    private class DummyConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters) => parameters?[0];
    }
}
