using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using VeloxDev.Core.Interfaces.DynamicTheme;

namespace VeloxDev.WPF.PlatformAdapters
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
                // 格式1: 逗号分隔的字符串 "x,y"
                if (parameters[0] is string strValue)
                {
                    var parts = strValue.Split(',');
                    if (parts.Length == 2 &&
                        double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
                        return new Point(x, y);
                }

                // 格式2: 两个独立参数 [x, y]
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
                // 格式1: 逗号分隔的字符串
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

                // 格式2: 数值参数集合
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
                // 格式1: 逗号分隔的字符串
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

                // 格式2: 数值参数集合
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
                // 格式1: 颜色名称或HEX字符串
                if (parameters[0] is string colorString)
                {
                    var converter = new System.Windows.Media.BrushConverter();
                    var brush = converter.ConvertFromString(colorString) as SolidColorBrush;
                    return brush?.Color;
                }

                // 格式2: 整数值 (ARGB)
                if (parameters[0] is int argb)
                {
                    return Color.FromArgb(
                        (byte)(argb >> 24 & 0xFF),
                        (byte)(argb >> 16 & 0xFF),
                        (byte)(argb >> 8 & 0xFF),
                        (byte)(argb & 0xFF));
                }

                // 格式3: 单独分量
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
                // 格式1: 直接传递笔刷
                if (parameters[0] is Brush brush)
                    return brush;

                // 格式2: 资源键查找
                if (parameters[0] is string resourceKey)
                {
                    if (Application.Current.TryFindResource(resourceKey) is Brush resourceBrush)
                        return resourceBrush;
                }

                // 格式3: 颜色字符串（使用WPF的BrushConverter）
                if (parameters[0] is string colorString)
                {
                    var converter = new System.Windows.Media.BrushConverter();
                    return converter.ConvertFromString(colorString) as Brush;
                }

                // 格式4: 颜色值（委托给颜色转换器）
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
                // 特殊处理Brush类型（WPF的BrushConverter需要单独处理）
                if (typeof(Brush).IsAssignableFrom(targetType))
                {
                    var brushConverter = new System.Windows.Media.BrushConverter();
                    return brushConverter.ConvertFromString(strValue);
                }

                // 获取目标类型的TypeConverter
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);

                // 支持文化不敏感的转换（如数字、日期等）
                if (converter.CanConvertFrom(typeof(string)))
                {
                    return converter.ConvertFromString(null, CultureInfo.InvariantCulture, strValue);
                }

                // 回退到默认转换（适用于大多数WPF内置类型）
                return converter.ConvertFrom(strValue);
            }
            catch (NotSupportedException)
            {
                // 类型不支持转换时尝试资源查找
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
}