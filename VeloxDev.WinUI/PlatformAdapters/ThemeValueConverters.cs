using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using VeloxDev.Core.Interfaces.DynamicTheme;
using Windows.Foundation;
using Windows.UI;

namespace VeloxDev.WinUI.PlatformAdapters
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
        private static readonly Dictionary<string, Color> _namedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        {"Transparent", Colors.Transparent},
        {"Black", Colors.Black},
        {"White", Colors.White},
        {"Red", Colors.Red},
        {"Green", Colors.Green},
        {"Blue", Colors.Blue},
        {"Yellow", Colors.Yellow},
        {"Orange", Colors.Orange},
        {"Purple", Colors.Purple},
        {"Pink", Colors.Pink},
        {"Gray", Colors.Gray},
        {"LightGray", Colors.LightGray},
        {"DarkGray", Colors.DarkGray},
        {"Cyan", Colors.Cyan},
        {"Magenta", Colors.Magenta},
        {"Brown", Colors.Brown},
        {"Lime", Colors.Lime},
        {"Teal", Colors.Teal},
        {"Navy", Colors.Navy},
        {"Maroon", Colors.Maroon},
        {"Silver", Colors.Silver},
        {"Gold", Colors.Gold},
        {"Violet", Colors.Violet},
        {"Indigo", Colors.Indigo},
        {"Beige", Colors.Beige},
        {"Coral", Colors.Coral},
        {"Crimson", Colors.Crimson},
        {"Khaki", Colors.Khaki},
        {"Lavender", Colors.Lavender},
        {"Olive", Colors.Olive},
        {"Plum", Colors.Plum},
        {"Salmon", Colors.Salmon},
        {"SkyBlue", Colors.SkyBlue},
        {"Turquoise", Colors.Turquoise},
        {"Wheat", Colors.Wheat}
    };

        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式1: 颜色名称或HEX字符串
                if (parameters[0] is string colorString)
                {
                    // 检查是否为命名颜色
                    if (_namedColors.TryGetValue(colorString, out var namedColor))
                    {
                        return namedColor;
                    }

                    // 尝试解析HEX格式 (#AARRGGBB 或 #RRGGBB)
                    if (TryParseHexColor(colorString, out var color))
                    {
                        return color;
                    }
                }

                // 格式2: 整数值 (ARGB)
                if (parameters[0] is int argb)
                {
                    return Color.FromArgb(
                        (byte)((argb >> 24) & 0xFF),
                        (byte)((argb >> 16) & 0xFF),
                        (byte)((argb >> 8) & 0xFF),
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

        private static bool TryParseHexColor(string input, out Color color)
        {
            color = default;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            // 移除#号
            input = input.Trim().TrimStart('#');

            // 检查长度是否有效
            if (input.Length != 3 && input.Length != 4 && input.Length != 6 && input.Length != 8)
                return false;

            // 处理缩写格式 (#RGB 或 #ARGB)
            if (input.Length == 3 || input.Length == 4)
            {
                char[] expanded = new char[input.Length * 2];
                for (int i = 0; i < input.Length; i++)
                {
                    expanded[i * 2] = input[i];
                    expanded[i * 2 + 1] = input[i];
                }
                input = new string(expanded);
            }

            // 确保现在有6位或8位
            if (input.Length != 6 && input.Length != 8)
                return false;

            // 使用 uint.Parse 正确解析十六进制
            try
            {
                uint hexValue = uint.Parse(input, NumberStyles.HexNumber);

                if (input.Length == 6)
                {
                    // RRGGBB
                    color = Color.FromArgb(
                        255,
                        (byte)((hexValue >> 16) & 0xFF),
                        (byte)((hexValue >> 8) & 0xFF),
                        (byte)(hexValue & 0xFF));
                }
                else
                {
                    // AARRGGBB
                    color = Color.FromArgb(
                        (byte)((hexValue >> 24) & 0xFF),
                        (byte)((hexValue >> 16) & 0xFF),
                        (byte)((hexValue >> 8) & 0xFF),
                        (byte)(hexValue & 0xFF));
                }

                return true;
            }
            catch
            {
                return false;
            }
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
                    if (Application.Current.Resources.TryGetValue(resourceKey, out var value) &&
                        value is Brush resourceBrush)
                    {
                        return resourceBrush;
                    }
                }

                // 格式3: 颜色字符串（使用ColorConverter）
                if (parameters[0] is string colorString)
                {
                    var colorConverter = new ColorConverter();
                    if (colorConverter.Convert(typeof(Color), propertyName, [colorString]) is Color color)
                    {
                        return new SolidColorBrush(color);
                    }
                }

                // 格式4: 颜色值（委托给颜色转换器）
                var colorConverter2 = new ColorConverter();
                if (colorConverter2.Convert(typeof(Color), propertyName, parameters) is Color color2)
                {
                    return new SolidColorBrush(color2);
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
                // 特殊处理Brush类型
                if (typeof(Brush).IsAssignableFrom(targetType))
                {
                    var brushConverter = new BrushConverter();
                    return brushConverter.Convert(targetType, propertyName, [strValue]);
                }

                // 获取目标类型的TypeConverter
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);

                // 支持文化不敏感的转换
                if (converter.CanConvertFrom(typeof(string)))
                {
                    return converter.ConvertFromString(null, CultureInfo.InvariantCulture, strValue);
                }

                // 回退到默认转换
                return converter.ConvertFrom(strValue);
            }
            catch (NotSupportedException)
            {
                // 类型不支持转换时尝试资源查找
                if (Application.Current.Resources.TryGetValue(strValue, out var resourceValue) &&
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