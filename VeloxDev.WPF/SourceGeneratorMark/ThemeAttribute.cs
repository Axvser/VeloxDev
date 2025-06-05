using VeloxDev.WPF.StructuralDesign.Theme;

namespace VeloxDev.WPF.SourceGeneratorMark
{
    /// <summary>
    /// ✨ View >>> Adds a theme-animation behavior for the specified property in the View layer
    /// </summary>
    /// <param name="propertyName"> The name of the property to be themed.</param>
    /// <param name="themeArguments"> The arguments used to build theme value.</param>
    /// <param name="themeType"> The type of the theme to be applied.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ThemeAttribute(string propertyName, Type themeType, object?[] themeArguments) : Attribute, IThemeAttribute
    {
        public string PropertyName => propertyName;
        public Type ThemeType => themeType;
        public object?[] Parameters => themeArguments;
    }
}