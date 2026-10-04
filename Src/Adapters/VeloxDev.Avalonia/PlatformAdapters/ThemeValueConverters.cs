using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Utilities;
using System;
using System.Globalization;

namespace VeloxDev.DynamicTheme
{
    public class DoubleConverter : IThemeValueConverter
    {
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            // 用 Avalonia 内置的类型转换系统。
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            // 用 Avalonia 内置的 Point 解析。
            if (parameters[0] is string strValue)
            {
                return Point.Parse(strValue);
            }

            // 多参数构造。
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            // 用 Avalonia 内置的 Thickness 解析。
            if (parameters[0] is string strValue)
            {
                return Thickness.Parse(strValue);
            }

            // 多参数构造。
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            // 用 Avalonia 内置的 CornerRadius 解析。
            if (parameters[0] is string strValue)
            {
                return CornerRadius.Parse(strValue);
            }

            // 多参数构造。
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            // 用 Avalonia 内置的颜色解析。
            if (parameters[0] is string colorString)
            {
                if (Color.TryParse(colorString, out var color))
                {
                    return color;
                }
            }

            try
            {
                // 整数值（ARGB）
                if (parameters[0] is int argb)
                {
                    return Color.FromUInt32((uint)argb);
                }

                // 各分量
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
                // 1. 直接传画刷
                if (parameters[0] is IBrush brush)
                    return brush;

                // 2. 资源键查找
                if (parameters[0] is string resourceKey)
                {
                    // 用 Avalonia 内置的资源查找。
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
                    // 用内置颜色解析。
                    if (Color.TryParse(colorString, out var color1))
                    {
                        return new SolidColorBrush(color1);
                    }
                }

                // 4. 交给颜色转换器
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
                // 1. 先试资源查找
                var app = Application.Current;
                if (app != null)
                {
                    if (app.TryFindResource(strValue, out var resource) && targetType.IsInstanceOfType(resource))
                    {
                        return resource;
                    }
                }

                // 2. 用 Avalonia 内置的类型转换系统
                if (TypeUtilities.TryConvert(targetType, strValue, CultureInfo.InvariantCulture, out var result))
                {
                    return result;
                }

                // 3. 单独处理 Brush 类型
                if (typeof(IBrush).IsAssignableFrom(targetType))
                {
                    var brushConverter = new BrushConverter();
                    return brushConverter.Convert(targetType, propertyName, parameters);
                }

                // 到此为止 —— 平台自己的类型由上面几步负责，其它类型不再支持。
                // 自定义类型（含可插值的那些）由宿主自己写一个 IThemeValueConverter，声明在
                // [ThemeConfig<...>] 上：那是本模块记录在案的扩展点，也是这里不再需要反射的原因。
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
