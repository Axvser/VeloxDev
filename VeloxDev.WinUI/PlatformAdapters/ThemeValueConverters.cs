#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Globalization;
using VeloxDev.Core.Interfaces.DynamicTheme;
using Windows.Foundation;
using Windows.UI;

namespace VeloxDev.WinUI.PlatformAdapters
{
    namespace VeloxDev.WinUI.PlatformAdapters
    {
        public class DoubleConverter : IThemeValueConverter
        {
            public object? Convert(Type targetType, string propertyName, object?[] parameters)
            {
                if (parameters == null || parameters.Length < 1)
                    return null;

                try
                {
                    return System.Convert.ToDouble(parameters[0], CultureInfo.InvariantCulture);
                }
                catch
                {
                    if (parameters[0] is string s &&
                        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                        return d;
                    return null;
                }
            }
        }

        public class PointConverter : IThemeValueConverter
        {
            public object? Convert(Type targetType, string propertyName, object?[] parameters)
            {
                if (parameters == null || parameters.Length < 1)
                    return null;

                try
                {
                    if (parameters[0] is string str)
                    {
                        var parts = str.Split(',', StringSplitOptions.TrimEntries);
                        if (parts.Length == 2)
                            return new Point(double.Parse(parts[0], CultureInfo.InvariantCulture),
                                             double.Parse(parts[1], CultureInfo.InvariantCulture));
                    }

                    if (parameters.Length >= 2)
                    {
                        double x = System.Convert.ToDouble(parameters[0]);
                        double y = System.Convert.ToDouble(parameters[1]);
                        return new Point(x, y);
                    }
                }
                catch { }

                return null;
            }
        }

        public class ThicknessConverter : IThemeValueConverter
        {
            public object? Convert(Type targetType, string propertyName, object?[] parameters)
            {
                if (parameters == null || parameters.Length < 1)
                    return null;

                try
                {
                    if (parameters[0] is string str)
                    {
                        var parts = str.Split(',', StringSplitOptions.TrimEntries);
                        return parts.Length switch
                        {
                            1 => new Thickness(double.Parse(parts[0], CultureInfo.InvariantCulture)),
                            4 => new Thickness(
                                double.Parse(parts[0], CultureInfo.InvariantCulture),
                                double.Parse(parts[1], CultureInfo.InvariantCulture),
                                double.Parse(parts[2], CultureInfo.InvariantCulture),
                                double.Parse(parts[3], CultureInfo.InvariantCulture)),
                            _ => null
                        };
                    }

                    return parameters.Length switch
                    {
                        1 => new Thickness(System.Convert.ToDouble(parameters[0])),
                        4 => new Thickness(
                            System.Convert.ToDouble(parameters[0]),
                            System.Convert.ToDouble(parameters[1]),
                            System.Convert.ToDouble(parameters[2]),
                            System.Convert.ToDouble(parameters[3])),
                        _ => null
                    };
                }
                catch
                {
                    return null;
                }
            }
        }

        public class CornerRadiusConverter : IThemeValueConverter
        {
            public object? Convert(Type targetType, string propertyName, object?[] parameters)
            {
                if (parameters == null || parameters.Length < 1)
                    return null;

                try
                {
                    if (parameters[0] is string str)
                    {
                        var parts = str.Split(',', StringSplitOptions.TrimEntries);
                        return parts.Length switch
                        {
                            1 => new CornerRadius(double.Parse(parts[0], CultureInfo.InvariantCulture)),
                            4 => new CornerRadius(double.Parse(parts[0], CultureInfo.InvariantCulture),
                                                  double.Parse(parts[1], CultureInfo.InvariantCulture),
                                                  double.Parse(parts[2], CultureInfo.InvariantCulture),
                                                  double.Parse(parts[3], CultureInfo.InvariantCulture)),
                            _ => null
                        };
                    }

                    return parameters.Length switch
                    {
                        1 => new CornerRadius(System.Convert.ToDouble(parameters[0])),
                        4 => new CornerRadius(System.Convert.ToDouble(parameters[0]),
                                              System.Convert.ToDouble(parameters[1]),
                                              System.Convert.ToDouble(parameters[2]),
                                              System.Convert.ToDouble(parameters[3])),
                        _ => null
                    };
                }
                catch { return null; }
            }
        }

        public class ColorConverter : IThemeValueConverter
        {
            public object? Convert(Type targetType, string propertyName, object?[] parameters)
            {
                if (parameters == null || parameters.Length < 1)
                    return null;

                try
                {
                    if (parameters[0] is string str)
                    {
                        // 支持 #AARRGGBB 或 #RRGGBB
                        str = str.Trim();
                        if (str.StartsWith('#'))
                        {
                            str = str[1..];
                        }

                        if (str.Length == 6)
                        {
                            var r = byte.Parse(str[..2], NumberStyles.HexNumber);
                            var g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
                            var b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);
                            return Color.FromArgb(255, r, g, b);
                        }
                        if (str.Length == 8)
                        {
                            var a = byte.Parse(str[..2], NumberStyles.HexNumber);
                            var r = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
                            var g = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);
                            var b = byte.Parse(str.Substring(6, 2), NumberStyles.HexNumber);
                            return Color.FromArgb(a, r, g, b);
                        }
                    }

                    if (parameters.Length >= 3)
                    {
                        byte a = parameters.Length >= 4 ? System.Convert.ToByte(parameters[0]) : (byte)255;
                        int offset = parameters.Length >= 4 ? 1 : 0;
                        byte r = System.Convert.ToByte(parameters[offset]);
                        byte g = System.Convert.ToByte(parameters[offset + 1]);
                        byte b = System.Convert.ToByte(parameters[offset + 2]);
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
                if (parameters == null || parameters.Length < 1)
                    return null;

                try
                {
                    if (parameters[0] is Brush brush)
                        return brush;

                    if (parameters[0] is string str)
                    {
                        // 先尝试资源查找
                        if (Application.Current?.Resources.TryGetValue(str, out var resource) == true)
                        {
                            if (resource is Brush b)
                                return b;
                        }

                        // 再尝试颜色解析
                        var colorConv = new ColorConverter();
                        if (colorConv.Convert(typeof(Color), propertyName, [str]) is Color c)
                            return new SolidColorBrush(c);
                    }

                    var colorConverter = new ColorConverter();
                    if (colorConverter.Convert(typeof(Color), propertyName, parameters) is Color color)
                        return new SolidColorBrush(color);
                }
                catch { }

                return null;
            }
        }

        public class ObjectConverter : IThemeValueConverter
        {
            public object? Convert(Type targetType, string propertyName, object?[] parameters)
            {
                if (parameters == null || parameters.Length != 1 || parameters[0] is not string str)
                    return null;

                try
                {
                    // 尝试资源查找
                    if (Application.Current?.Resources.TryGetValue(str, out var resource) == true &&
                        targetType.IsInstanceOfType(resource))
                    {
                        return resource;
                    }

                    // 尝试Brush
                    if (typeof(Brush).IsAssignableFrom(targetType))
                    {
                        var brushConverter = new BrushConverter();
                        return brushConverter.Convert(targetType, propertyName, parameters);
                    }

                    // 通用转换器
                    var converter = TypeDescriptor.GetConverter(targetType);
                    if (converter.CanConvertFrom(typeof(string)))
                        return converter.ConvertFromString(null, CultureInfo.InvariantCulture, str);
                }
                catch { }

                return null;
            }
        }
    }

}
