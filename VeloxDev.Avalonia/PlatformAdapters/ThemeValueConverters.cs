using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Utilities;
using System;
using System.ComponentModel;
using System.Globalization;
using VeloxDev.Core.Interfaces.DynamicTheme;

namespace VeloxDev.Avalonia.PlatformAdapters
{
    public class DoubleConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            // 使用Avalonia内置类型转换系统
            if (TypeUtilities.TryConvert(targetType, parameters[0], CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            return parameters[0] switch
            {
                double d => d,
                int i => (double)i,
                float f => (double)f,
                string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) => parsed,
                _ => null
            };
        }
    }

    public class PointConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            // 使用Avalonia内置的点解析
            if (parameters[0] is string strValue)
            {
                return Point.Parse(strValue);
            }

            // 多参数构造
            try
            {
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
            if (parameters == null || parameters.Length < 1) return null;

            // 使用Avalonia内置的厚度解析
            if (parameters[0] is string strValue)
            {
                return Thickness.Parse(strValue);
            }

            // 多参数构造
            try
            {
                return parameters.Length switch
                {
                    1 => new Thickness(System.Convert.ToDouble(parameters[0])),
                    2 => new Thickness(
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

            // 使用Avalonia内置的圆角解析
            if (parameters[0] is string strValue)
            {
                return CornerRadius.Parse(strValue);
            }

            // 多参数构造
            try
            {
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

            // 使用Avalonia内置的颜色解析
            if (parameters[0] is string colorString)
            {
                if (Color.TryParse(colorString, out var color))
                {
                    return color;
                }
            }

            try
            {
                // 整数值 (ARGB)
                if (parameters[0] is int argb)
                {
                    return Color.FromUInt32((uint)argb);
                }

                // 单独分量
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
                // 1. 直接传递笔刷
                if (parameters[0] is IBrush brush)
                    return brush;

                // 2. 资源键查找
                if (parameters[0] is string resourceKey)
                {
                    // 使用Avalonia内置的资源查找
                    var app = Application.Current;
                    if (app != null)
                    {
                        if (app.TryFindResource(resourceKey, out var resource) && resource is IBrush brushResource)
                        {
                            return brushResource;
                        }
                    }
                }

                // 3. 颜色字符串
                if (parameters[0] is string colorString)
                {
                    // 使用内置颜色解析
                    if (Color.TryParse(colorString, out var color1))
                    {
                        return new SolidColorBrush(color1);
                    }
                }

                // 4. 委托给颜色转换器
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
            // 参数验证
            if (parameters == null || parameters.Length != 1 || parameters[0] is not string strValue)
                return null;

            try
            {
                // 1. 尝试资源查找
                var app = Application.Current;
                if (app != null)
                {
                    if (app.TryFindResource(strValue, out var resource) && targetType.IsInstanceOfType(resource))
                    {
                        return resource;
                    }
                }

                // 2. 使用Avalonia内置类型转换系统
                if (TypeUtilities.TryConvert(targetType, strValue, CultureInfo.InvariantCulture, out var result))
                {
                    return result;
                }

                // 3. 特殊处理Brush类型
                if (typeof(IBrush).IsAssignableFrom(targetType))
                {
                    var brushConverter = new BrushConverter();
                    return brushConverter.Convert(targetType, propertyName, parameters);
                }

                // 4. 使用.NET类型转换器作为回退
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);
                if (converter.CanConvertFrom(typeof(string)))
                {
                    return converter.ConvertFromString(null, CultureInfo.InvariantCulture, strValue);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
