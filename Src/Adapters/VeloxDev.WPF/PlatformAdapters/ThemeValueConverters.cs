using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VeloxDev.DynamicTheme
{
    public class DoubleConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            return parameters[0] switch
            {
                double d => d,
                int i => (double)i,
                float f => (double)f,
                string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double result) => result,
                _ => null
            };
        }
    }

    public class PointConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // Format 1: comma-separated string "x,y"
                if (parameters[0] is string strValue)
                {
                    var parts = strValue.Split(',');
                    if (parts.Length == 2 &&
                        double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
                        return new Point(x, y);
                }

                // Format 2: two separate parameters [x, y]
                if (parameters.Length >= 2)
                {
                    double x = System.Convert.ToDouble(parameters[0]);
                    double y = System.Convert.ToDouble(parameters[1]);
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
                // Format 1: comma-separated string
                if (parameters[0] is string strValue)
                {
                    var parts = strValue.Split(',');
                    switch (parts.Length)
                    {
                        case 1 when double.TryParse(parts[0], out double uniform):
                            return new Thickness(uniform);
                        case 2 when double.TryParse(parts[0], out double horz) &&
                                 double.TryParse(parts[1], out double vert):
                            return new Thickness(horz, vert, horz, vert);
                        case 4 when double.TryParse(parts[0], out double left) &&
                                 double.TryParse(parts[1], out double top) &&
                                 double.TryParse(parts[2], out double right) &&
                                 double.TryParse(parts[3], out double bottom):
                            return new Thickness(left, top, right, bottom);
                    }
                }

                // Format 2: numeric parameter list
                return parameters.Length switch
                {
                    1 => new Thickness(System.Convert.ToDouble(parameters[0])),
                    2 => new Thickness(
                                                System.Convert.ToDouble(parameters[0]),
                                                System.Convert.ToDouble(parameters[1]),
                                                System.Convert.ToDouble(parameters[0]),
                                                System.Convert.ToDouble(parameters[1])),
                    4 => new Thickness(
                                                System.Convert.ToDouble(parameters[0]),
                                                System.Convert.ToDouble(parameters[1]),
                                                System.Convert.ToDouble(parameters[2]),
                                                System.Convert.ToDouble(parameters[3])),
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
                // Format 1: comma-separated string
                if (parameters[0] is string strValue)
                {
                    var parts = strValue.Split(',');
                    switch (parts.Length)
                    {
                        case 1 when double.TryParse(parts[0], out double uniform):
                            return new CornerRadius(uniform);
                        case 4 when double.TryParse(parts[0], out double tl) &&
                                 double.TryParse(parts[1], out double tr) &&
                                 double.TryParse(parts[2], out double br) &&
                                 double.TryParse(parts[3], out double bl):
                            return new CornerRadius(tl, tr, br, bl);
                    }
                }

                // Format 2: numeric parameter list
                return parameters.Length switch
                {
                    1 => new CornerRadius(System.Convert.ToDouble(parameters[0])),
                    4 => new CornerRadius(
                                                System.Convert.ToDouble(parameters[0]),
                                                System.Convert.ToDouble(parameters[1]),
                                                System.Convert.ToDouble(parameters[2]),
                                                System.Convert.ToDouble(parameters[3])),
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
                // Format 1: color name or HEX string
                if (parameters[0] is string colorString)
                {
                    var converter = new System.Windows.Media.BrushConverter();
                    var brush = converter.ConvertFromString(colorString) as SolidColorBrush;
                    return brush?.Color;
                }

                // Format 2: integer value (ARGB)
                if (parameters[0] is int argb)
                {
                    return Color.FromArgb(
                        (byte)(argb >> 24 & 0xFF),
                        (byte)(argb >> 16 & 0xFF),
                        (byte)(argb >> 8 & 0xFF),
                        (byte)(argb & 0xFF));
                }

                // Format 3: individual components
                if (parameters.Length >= 3)
                {
                    byte a = parameters.Length >= 4 ? System.Convert.ToByte(parameters[0]) : (byte)255;
                    byte r = System.Convert.ToByte(parameters[parameters.Length >= 4 ? 1 : 0]);
                    byte g = System.Convert.ToByte(parameters[parameters.Length >= 4 ? 2 : 1]);
                    byte b = System.Convert.ToByte(parameters[parameters.Length >= 4 ? 3 : 2]);
                    return Color.FromArgb(a, r, g, b);
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
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // Format 1: pass a brush directly
                if (parameters[0] is Brush brush)
                    return brush;

                // Format 2: resource-key lookup
                if (parameters[0] is string resourceKey)
                {
                    if (ThemeResourceLookup.TryFindResource(resourceKey, out var resource)
                        && resource is Brush resourceBrush)
                        return resourceBrush;
                }

                // Format 3: color string (uses WPF's BrushConverter)
                if (parameters[0] is string colorString)
                {
                    var converter = new System.Windows.Media.BrushConverter();
                    return converter.ConvertFromString(colorString) as Brush;
                }

                // Format 4: color value (delegated to the color converter)
                var colorConverter = new ColorConverter();
                if (colorConverter.Convert(typeof(Color), propertyName, parameters) is Color color)
                {
                    return new SolidColorBrush(color);
                }

                return null;
            }
            catch { return null; }
        }
    }

    public class ObjectConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            // Parameter validation
            if (parameters == null || parameters.Length != 1 || parameters[0] is not string strValue)
                return null;

            try
            {
                if (ThemeResourceLookup.TryFindResource(strValue, out var resourceValue)
                    && targetType.IsInstanceOfType(resourceValue))
                {
                    return resourceValue;
                }

                // Special-case the Brush type (WPF's BrushConverter must be handled separately).
                if (typeof(Brush).IsAssignableFrom(targetType))
                {
                    var brushConverter = new System.Windows.Media.BrushConverter();
                    return brushConverter.ConvertFromString(strValue);
                }

                // Get the target type's TypeConverter.
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);

                // Support culture-insensitive conversion (numbers, dates, etc.).
                if (converter.CanConvertFrom(typeof(string)))
                {
                    return converter.ConvertFromString(null, CultureInfo.InvariantCulture, strValue);
                }

                // Fall back to default conversion (works for most WPF built-in types).
                return converter.ConvertFrom(strValue);
            }
            catch (NotSupportedException)
            {
                // When conversion is unsupported, try resource lookup.
                if (Application.Current.TryFindResource(strValue) is object resourceValue &&
                    targetType.IsInstanceOfType(resourceValue))
                {
                    return resourceValue;
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
        public static bool TryFindResource(object key, out object? value)
        {
            value = null;

            if (Application.Current is null)
            {
                return false;
            }

            if (TryFindInDictionary(Application.Current.Resources, key, out value))
            {
                return true;
            }

            value = Application.Current.TryFindResource(key);
            return value is not null;
        }

        private static bool TryFindInDictionary(ResourceDictionary? dictionary, object key, out object? value)
        {
            value = null;
            if (dictionary is null)
            {
                return false;
            }

            if (dictionary.Contains(key))
            {
                value = dictionary[key];
                return true;
            }

            foreach (var mergedDictionary in dictionary.MergedDictionaries)
            {
                if (TryFindInDictionary(mergedDictionary, key, out value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}