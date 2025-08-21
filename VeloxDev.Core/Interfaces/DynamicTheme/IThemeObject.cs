using System.Reflection;

namespace VeloxDev.Core.Interfaces.DynamicTheme
{
    public interface IThemeObject
    {
        public void InitializeTheme();

        public void ExecuteThemeChanging(Type? oldValue, Type? newValue);
        public void ExecuteThemeChanged(Type? oldValue, Type? newValue);

        public void EditThemeValue<T>(string propertyName, object? newValue) where T : ITheme;
        public void RestoreThemeValue<T>(string propertyName) where T : ITheme;

        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetStaticCache();
        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetActiveCache();
    }
}
