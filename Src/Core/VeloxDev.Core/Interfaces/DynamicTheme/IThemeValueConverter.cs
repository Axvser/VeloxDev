namespace VeloxDev.Core.DynamicTheme
{
    public interface IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters);
    }
}
