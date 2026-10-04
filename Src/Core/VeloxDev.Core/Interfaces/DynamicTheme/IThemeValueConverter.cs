namespace VeloxDev.DynamicTheme
{
    /// <summary>Converts a stored theme value into the type a property expects.</summary>
    public interface IThemeValueConverter
    {
        /// <summary>Converts a stored theme value into <paramref name="targetType"/>.</summary>
        /// <param name="targetType">The type the property expects.</param>
        /// <param name="propertyName">The property the value belongs to.</param>
        /// <param name="parameters">The extra arguments the theme value carries.</param>
        /// <returns>The converted value, or <see langword="null"/> when it cannot be converted.</returns>
        public object? Convert(Type targetType, string propertyName, object?[] parameters);
    }
}
