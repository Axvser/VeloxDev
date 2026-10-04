using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VeloxDev.DynamicTheme
{
    public class DoubleConverter : IThemeValueConverter
    {
        /// <inheritdoc />
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式 1：逗号分隔字符串 x,y
                if (parameters[0] is string strValue)
                {
                    var parts = strValue.Split(',');
                    if (parts.Length == 2 &&
                        double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
                        return new Point(x, y);
                }

                // 格式 2：两个独立参数 [x, y]
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式 1：逗号分隔字符串
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

                // 格式 2：数值参数列表
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式 1：逗号分隔字符串
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

                // 格式 2：数值参数列表
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式 1：颜色名或 HEX 字符串
                if (parameters[0] is string colorString)
                {
                    var converter = new System.Windows.Media.BrushConverter();
                    var brush = converter.ConvertFromString(colorString) as SolidColorBrush;
                    return brush?.Color;
                }

                // 格式 2：整数值（ARGB）
                if (parameters[0] is int argb)
                {
                    return Color.FromArgb(
                        (byte)(argb >> 24 & 0xFF),
                        (byte)(argb >> 16 & 0xFF),
                        (byte)(argb >> 8 & 0xFF),
                        (byte)(argb & 0xFF));
                }

                // 格式 3：各分量
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式 1：直接传画刷
                if (parameters[0] is Brush brush)
                    return brush;

                // 格式 2：资源键查找
                if (parameters[0] is string resourceKey)
                {
                    if (ThemeResourceLookup.TryFindResource(resourceKey, out var resource)
                        && resource is Brush resourceBrush)
                        return resourceBrush;
                }

                // 格式 3：颜色字符串（用 WPF 的 BrushConverter）
                if (parameters[0] is string colorString)
                {
                    var converter = new System.Windows.Media.BrushConverter();
                    return converter.ConvertFromString(colorString) as Brush;
                }

                // 格式 4：颜色值（交给颜色转换器）
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            // 参数校验
            if (parameters == null || parameters.Length != 1 || parameters[0] is not string strValue)
                return null;

            try
            {
                if (ThemeResourceLookup.TryFindResource(strValue, out var resourceValue)
                    && targetType.IsInstanceOfType(resourceValue))
                {
                    return resourceValue;
                }

                // 单独处理 Brush 类型（WPF 的 BrushConverter 必须分开处理）。
                if (typeof(Brush).IsAssignableFrom(targetType))
                {
                    var brushConverter = new System.Windows.Media.BrushConverter();
                    return brushConverter.ConvertFromString(strValue);
                }

                // 取目标类型的 TypeConverter。
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);

                // 支持与文化无关的转换（数字、日期等）。
                if (converter.CanConvertFrom(typeof(string)))
                {
                    return converter.ConvertFromString(null, CultureInfo.InvariantCulture, strValue);
                }

                // 退回默认转换（对多数 WPF 内建类型有效）。
                return converter.ConvertFrom(strValue);
            }
            catch (NotSupportedException)
            {
                // 转换不支持时试资源查找。
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
        /// <summary>Tries to resolve the resource named <paramref name="key"/> from the host's resource scopes.</summary>
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