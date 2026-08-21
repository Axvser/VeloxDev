using Microsoft.Maui.Converters;
using Microsoft.Maui.Graphics.Converters;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace VeloxDev.DynamicTheme
{
    public class DoubleConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // Use MAUI's recommended type conversion.
                if (parameters[0] is string strValue)
                {
                    if (double.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
                    {
                        return result;
                    }
                }

                // Handle other types.
                return parameters[0] switch
                {
                    double val => val,
                    int i => (double)i,
                    float f => (double)f,
                    _ => System.Convert.ToDouble(parameters[0], CultureInfo.InvariantCulture)
                };
            }
            catch
            {
                return null;
            }
        }
    }

    public class PointConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // Use MAUI's built-in point parsing.
                if (parameters[0] is string strValue)
                {
                    // Convert using PointTypeConverter.
                    var converter = new PointTypeConverter();
                    return converter.ConvertFromInvariantString(strValue);
                }

                // Multi-parameter construction.
                if (parameters.Length >= 2)
                {
                    double x = System.Convert.ToDouble(parameters[0], CultureInfo.InvariantCulture);
                    double y = System.Convert.ToDouble(parameters[1], CultureInfo.InvariantCulture);
                    return new Point(x, y);
                }

                return null;
            }
            catch { return null; }
        }
    }

    public class ThicknessConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // Use MAUI's built-in thickness parsing.
                if (parameters[0] is string strValue)
                {
                    var converter = new ThicknessTypeConverter();
                    return converter.ConvertFromInvariantString(strValue);
                }

                // Multi-parameter construction.
                return parameters.Length switch
                {
                    1 => new Thickness(System.Convert.ToDouble(parameters[0], CultureInfo.InvariantCulture)),
                    2 => new Thickness(
                        System.Convert.ToDouble(parameters[0], CultureInfo.InvariantCulture),
                        System.Convert.ToDouble(parameters[1], CultureInfo.InvariantCulture)),
                    4 => new Thickness(
                        System.Convert.ToDouble(parameters[0], CultureInfo.InvariantCulture),
                        System.Convert.ToDouble(parameters[1], CultureInfo.InvariantCulture),
                        System.Convert.ToDouble(parameters[2], CultureInfo.InvariantCulture),
                        System.Convert.ToDouble(parameters[3], CultureInfo.InvariantCulture)),
                    _ => null,
                };
            }
            catch { return null; }
        }
    }

    public class CornerRadiusConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // Use MAUI's built-in corner-radius parsing.
                if (parameters[0] is string strValue)
                {
                    var converter = new CornerRadiusTypeConverter();
                    return converter.ConvertFromInvariantString(strValue);
                }

                // Multi-parameter construction.
                return parameters.Length switch
                {
                    1 => new CornerRadius(System.Convert.ToDouble(parameters[0], CultureInfo.InvariantCulture)),
                    4 => new CornerRadius(
                        System.Convert.ToDouble(parameters[0], CultureInfo.InvariantCulture),
                        System.Convert.ToDouble(parameters[1], CultureInfo.InvariantCulture),
                        System.Convert.ToDouble(parameters[2], CultureInfo.InvariantCulture),
                        System.Convert.ToDouble(parameters[3], CultureInfo.InvariantCulture)),
                    _ => null,
                };
            }
            catch { return null; }
        }
    }

    public class ColorConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // Use MAUI's built-in color parsing.
                if (parameters[0] is string colorString)
                {
                    var converter = new ColorTypeConverter();
                    return converter.ConvertFromInvariantString(colorString);
                }

                // Integer value (ARGB)
                if (parameters[0] is int argb)
                {
                    return Color.FromInt(argb);
                }

                // Individual components
                if (parameters.Length >= 3)
                {
                    float a = parameters.Length >= 4 ?
                        System.Convert.ToSingle(parameters[0], CultureInfo.InvariantCulture) : 1f;
                    float r = System.Convert.ToSingle(parameters[parameters.Length >= 4 ? 1 : 0], CultureInfo.InvariantCulture);
                    float g = System.Convert.ToSingle(parameters[parameters.Length >= 4 ? 2 : 1], CultureInfo.InvariantCulture);
                    float b = System.Convert.ToSingle(parameters[parameters.Length >= 4 ? 3 : 2], CultureInfo.InvariantCulture);
                    return Color.FromRgba(r, g, b, a);
                }

                return null;
            }
            catch { return null; }
        }
    }

    public class BrushConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1)
                return null;

            try
            {
                // 1. Pass a brush directly
                if (parameters[0] is Brush brush)
                    return brush;

                // 2. Resource-key lookup - uses MAUI's official resource lookup mechanism
                if (parameters[0] is string resourceKey)
                {
                    // Get the target element (resource lookup context).
                    var targetElement = GetTargetElement(parameters);

                    // Use MAUI's official resource lookup.
                    object? resource = null;

                    // First try element-level resource lookup.
                    if (targetElement != null)
                    {
                        resource = FindElementResource(targetElement, resourceKey);
                    }

                    // If not found, try application-level resources.
                    resource ??= FindApplicationResource(resourceKey);

                    if (resource is Brush foundBrush)
                    {
                        return foundBrush;
                    }

                    Debug.WriteLine($"Brush resource '{resourceKey}' not found");
                }

                // 3. Color string parsing
                if (parameters[0] is string colorString)
                {
                    if (Color.TryParse(colorString, out var color))
                    {
                        return new SolidColorBrush(color);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Brush conversion error: {ex.Message}");
                return null;
            }
        }

        #region Resource Helpers

        // Get the target element.
        private static IElement? GetTargetElement(object?[] parameters)
        {
            // 1. Try to get the explicit target from the parameters (usually the control itself).
            if (parameters.Length > 1 && parameters[1] is IElement explicitTarget)
            {
                return explicitTarget;
            }

            // 2. Try to get the current page (using MAUI's official method for the current context).
            return Shell.Current?.CurrentPage ?? Application.Current?.Windows[0].Page;
        }

        // MAUI's officially recommended element-level resource lookup.
        private static object? FindElementResource(IElement element, string key)
        {
            // Check the element's own resources.
            if (element is VisualElement visualElement &&
                ThemeResourceLookup.TryFindInResourceDictionary(visualElement.Resources, key, out var resource))
            {
                return resource;
            }

            // Walk up the parent chain looking for the resource.
            if (element is Element mauiElement && mauiElement.Parent is IElement parent)
            {
                return FindElementResource(parent, key);
            }

            return null;
        }

        // MAUI's officially recommended application-level resource lookup.
        private static object? FindApplicationResource(string key)
        {
            if (ThemeResourceLookup.TryFindApplicationResource(key, out var resource))
            {
                return resource;
            }

            return null;
        }

        #endregion
    }

    public class ObjectConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            // Parameter validation
            if (parameters == null || parameters.Length < 1 || parameters[0] is not string strValue)
                return null;

            try
            {
                // 1. Try resource lookup
                if (ThemeResourceLookup.TryFindApplicationResource(strValue, out var resource) &&
                    targetType.IsInstanceOfType(resource))
                {
                    return resource;
                }

                // 2. Use the .NET type converter
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);
                if (converter?.CanConvertFrom(typeof(string)) == true)
                {
                    return converter.ConvertFromInvariantString(strValue);
                }

                // 3. Special-case MAUI-specific types
                if (targetType == typeof(Point))
                {
                    return new PointTypeConverter().ConvertFromInvariantString(strValue);
                }
                else if (targetType == typeof(Thickness))
                {
                    return new ThicknessTypeConverter().ConvertFromInvariantString(strValue);
                }
                else if (targetType == typeof(CornerRadius))
                {
                    return new CornerRadiusTypeConverter().ConvertFromInvariantString(strValue);
                }
                else if (targetType == typeof(Color))
                {
                    return new ColorTypeConverter().ConvertFromInvariantString(strValue);
                }
                else if (typeof(Brush).IsAssignableFrom(targetType))
                {
                    var brushConverter = new BrushConverter();
                    return brushConverter.Convert(targetType, propertyName, parameters);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class ThemeResourceLookup
    {
        public static bool TryFindApplicationResource(string key, out object? value)
        {
            value = null;
            return TryFindInResourceDictionary(Application.Current?.Resources, key, out value);
        }

        public static bool TryFindInResourceDictionary(ResourceDictionary? dictionary, string key, out object? value)
        {
            value = null;
            if (dictionary is null)
            {
                return false;
            }

            if (dictionary.TryGetValue(key, out value))
            {
                return true;
            }

            foreach (var mergedDictionary in dictionary.MergedDictionaries)
            {
                if (TryFindInResourceDictionary(mergedDictionary, key, out value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
