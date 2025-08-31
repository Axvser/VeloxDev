using VeloxDev.Core.Interfaces.DynamicTheme;

namespace VeloxDev.Core.DynamicTheme
{
    /// <summary>
    /// Configure the context for dynamic theme switching
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ThemeConfigAttribute<TConverter, TTheme1, TTheme2> : Attribute
        where TConverter : class, IThemeValueConverter
        where TTheme1 : ITheme
        where TTheme2 : ITheme
    {
#pragma warning disable IDE0060
#pragma warning disable IDE0290
        public ThemeConfigAttribute(string propertyName, object?[] themeContext1, object?[] themeContext2) { }
#pragma warning restore IDE0290
#pragma warning restore IDE0060
    }

    /// <summary>
    /// Configure the context for dynamic theme switching
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ThemeConfigAttribute<TConverter, TTheme1, TTheme2, TTheme3> : Attribute
        where TConverter : class, IThemeValueConverter
        where TTheme1 : ITheme
        where TTheme2 : ITheme
        where TTheme3 : ITheme
    {
#pragma warning disable IDE0060
#pragma warning disable IDE0290
        public ThemeConfigAttribute(string propertyName, object?[] themeContext1, object?[] themeContext2, object?[] themeContext3) { }
#pragma warning restore IDE0290
#pragma warning restore IDE0060
    }

    /// <summary>
    /// Configure the context for dynamic theme switching
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ThemeConfigAttribute<TConverter, TTheme1, TTheme2, TTheme3, TTheme4> : Attribute
        where TConverter : class, IThemeValueConverter
        where TTheme1 : ITheme
        where TTheme2 : ITheme
        where TTheme3 : ITheme
        where TTheme4 : ITheme
    {
#pragma warning disable IDE0060
#pragma warning disable IDE0290
        public ThemeConfigAttribute(string propertyName, object?[] themeContext1, object?[] themeContext2, object?[] themeContext3, object?[] themeContext4) { }
#pragma warning restore IDE0290
#pragma warning restore IDE0060
    }

    /// <summary>
    /// Configure the context for dynamic theme switching
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ThemeConfigAttribute<TConverter, TTheme1, TTheme2, TTheme3, TTheme4, TTheme5> : Attribute
        where TConverter : class, IThemeValueConverter
        where TTheme1 : ITheme
        where TTheme2 : ITheme
        where TTheme3 : ITheme
        where TTheme4 : ITheme
        where TTheme5 : ITheme
    {
#pragma warning disable IDE0060
#pragma warning disable IDE0290
        public ThemeConfigAttribute(string propertyName, object?[] themeContext1, object?[] themeContext2, object?[] themeContext3, object?[] themeContext4, object?[] themeContext5) { }
#pragma warning restore IDE0290
#pragma warning restore IDE0060
    }

    /// <summary>
    /// Configure the context for dynamic theme switching
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ThemeConfigAttribute<TConverter, TTheme1, TTheme2, TTheme3, TTheme4, TTheme5, TTheme6> : Attribute
        where TConverter : class, IThemeValueConverter
        where TTheme1 : ITheme
        where TTheme2 : ITheme
        where TTheme3 : ITheme
        where TTheme4 : ITheme
        where TTheme5 : ITheme
        where TTheme6 : ITheme
    {
#pragma warning disable IDE0060
#pragma warning disable IDE0290
        public ThemeConfigAttribute(string propertyName, object?[] themeContext1, object?[] themeContext2, object?[] themeContext3, object?[] themeContext4, object?[] themeContext5, object?[] themeContext6) { }
#pragma warning restore IDE0290
#pragma warning restore IDE0060
    }

    /// <summary>
    /// Configure the context for dynamic theme switching
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ThemeConfigAttribute<TConverter, TTheme1, TTheme2, TTheme3, TTheme4, TTheme5, TTheme6, TTheme7> : Attribute
        where TConverter : class, IThemeValueConverter
        where TTheme1 : ITheme
        where TTheme2 : ITheme
        where TTheme3 : ITheme
        where TTheme4 : ITheme
        where TTheme5 : ITheme
        where TTheme6 : ITheme
        where TTheme7 : ITheme
    {
#pragma warning disable IDE0060
#pragma warning disable IDE0290
        public ThemeConfigAttribute(string propertyName, object?[] themeContext1, object?[] themeContext2, object?[] themeContext3, object?[] themeContext4, object?[] themeContext5, object?[] themeContext6, object?[] themeContext7) { }
#pragma warning restore IDE0290
#pragma warning restore IDE0060
    }
}
