using System.Reflection;

namespace VeloxDev.DynamicTheme
{
    /// <summary>An object whose property values follow the active theme.</summary>
    public interface IThemeObject
    {
        /// <summary>Applies the object's initial theme values.</summary>
        public void InitializeTheme();

        /// <summary>Runs the theme-changing hooks as a theme transition begins.</summary>
        /// <param name="oldValue">The theme being left, or <see langword="null"/>.</param>
        /// <param name="newValue">The theme being entered, or <see langword="null"/>.</param>
        public void ExecuteThemeChanging(Type? oldValue, Type? newValue);

        /// <summary>Runs the theme-changed hooks after a theme transition.</summary>
        /// <param name="oldValue">The theme that was left, or <see langword="null"/>.</param>
        /// <param name="newValue">The theme that was entered, or <see langword="null"/>.</param>
        public void ExecuteThemeChanged(Type? oldValue, Type? newValue);

        /// <summary>Sets <paramref name="propertyName"/> for theme <typeparamref name="T"/>.</summary>
        /// <param name="propertyName">The property to set.</param>
        /// <param name="newValue">The value to write.</param>
        public void SetThemeValue<T>(string propertyName, object? newValue) where T : ITheme;

        /// <summary>Restores <paramref name="propertyName"/> to its cached value for theme <typeparamref name="T"/>.</summary>
        /// <param name="propertyName">The property to restore.</param>
        public void RestoreThemeValue<T>(string propertyName) where T : ITheme;

        /// <summary>Returns the static (per-type) theme cache.</summary>
        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetStaticThemeCache();

        /// <summary>Returns the active (per-instance) theme cache.</summary>
        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetActiveThemeCache();
    }
}
