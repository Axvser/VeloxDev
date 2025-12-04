using System.ComponentModel;
using System.Globalization;
using VeloxDev.Core.Interfaces.DynamicTheme;

namespace VeloxDev.WinForms.PlatformAdapters
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

    public class IntConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            return parameters[0] switch
            {
                int i => i,
                double d => (int)d,
                float f => (int)f,
                string s when int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int result) => result,
                _ => null
            };
        }
    }

    public class FloatConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            return parameters[0] switch
            {
                float f => f,
                double d => (float)d,
                int i => (float)i,
                string s when float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out float result) => result,
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
                        int.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out int x) &&
                        int.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out int y))
                        return new Point(x, y);
                }

                // 格式2: 两个独立参数 [x, y]
                if (parameters.Length >= 2)
                {
                    int x = System.Convert.ToInt32(parameters[0]);
                    int y = System.Convert.ToInt32(parameters[1]);
                    return new Point(x, y);
                }

                return null;
            }
            catch { return null; }
        }
    }

    public class PointFConverter : IThemeValueConverter
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
                        float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float y))
                        return new PointF(x, y);
                }

                // 格式2: 两个独立参数 [x, y]
                if (parameters.Length >= 2)
                {
                    float x = System.Convert.ToSingle(parameters[0]);
                    float y = System.Convert.ToSingle(parameters[1]);
                    return new PointF(x, y);
                }

                return null;
            }
            catch { return null; }
        }
    }

    public class SizeConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式1: 逗号分隔的字符串 "width,height"
                if (parameters[0] is string strValue)
                {
                    var parts = strValue.Split(',');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out int width) &&
                        int.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out int height))
                        return new Size(width, height);
                }

                // 格式2: 两个独立参数 [width, height]
                if (parameters.Length >= 2)
                {
                    int width = System.Convert.ToInt32(parameters[0]);
                    int height = System.Convert.ToInt32(parameters[1]);
                    return new Size(width, height);
                }

                return null;
            }
            catch { return null; }
        }
    }

    public class SizeFConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式1: 逗号分隔的字符串 "width,height"
                if (parameters[0] is string strValue)
                {
                    var parts = strValue.Split(',');
                    if (parts.Length == 2 &&
                        float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float width) &&
                        float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float height))
                        return new SizeF(width, height);
                }

                // 格式2: 两个独立参数 [width, height]
                if (parameters.Length >= 2)
                {
                    float width = System.Convert.ToSingle(parameters[0]);
                    float height = System.Convert.ToSingle(parameters[1]);
                    return new SizeF(width, height);
                }

                return null;
            }
            catch { return null; }
        }
    }

    public class RectangleConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式1: 逗号分隔的字符串 "x,y,width,height"
                if (parameters[0] is string strValue)
                {
                    var parts = strValue.Split(',');
                    if (parts.Length == 4 &&
                        int.TryParse(parts[0], out int x) &&
                        int.TryParse(parts[1], out int y) &&
                        int.TryParse(parts[2], out int width) &&
                        int.TryParse(parts[3], out int height))
                        return new Rectangle(x, y, width, height);
                }

                // 格式2: 四个独立参数 [x, y, width, height]
                if (parameters.Length >= 4)
                {
                    int x = System.Convert.ToInt32(parameters[0]);
                    int y = System.Convert.ToInt32(parameters[1]);
                    int width = System.Convert.ToInt32(parameters[2]);
                    int height = System.Convert.ToInt32(parameters[3]);
                    return new Rectangle(x, y, width, height);
                }

                return null;
            }
            catch { return null; }
        }
    }

    public class RectangleFConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式1: 逗号分隔的字符串 "x,y,width,height"
                if (parameters[0] is string strValue)
                {
                    var parts = strValue.Split(',');
                    if (parts.Length == 4 &&
                        float.TryParse(parts[0], out float x) &&
                        float.TryParse(parts[1], out float y) &&
                        float.TryParse(parts[2], out float width) &&
                        float.TryParse(parts[3], out float height))
                        return new RectangleF(x, y, width, height);
                }

                // 格式2: 四个独立参数 [x, y, width, height]
                if (parameters.Length >= 4)
                {
                    float x = System.Convert.ToSingle(parameters[0]);
                    float y = System.Convert.ToSingle(parameters[1]);
                    float width = System.Convert.ToSingle(parameters[2]);
                    float height = System.Convert.ToSingle(parameters[3]);
                    return new RectangleF(x, y, width, height);
                }

                return null;
            }
            catch { return null; }
        }
    }

    public class PaddingConverter : IThemeValueConverter
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
                        case 1 when int.TryParse(parts[0], out int uniform):
                            return new Padding(uniform);
                        case 2 when int.TryParse(parts[0], out int horz) &&
                                 int.TryParse(parts[1], out int vert):
                            return new Padding(horz, vert, horz, vert);
                        case 4 when int.TryParse(parts[0], out int left) &&
                                 int.TryParse(parts[1], out int top) &&
                                 int.TryParse(parts[2], out int right) &&
                                 int.TryParse(parts[3], out int bottom):
                            return new Padding(left, top, right, bottom);
                    }
                }

                // 格式2: 数值参数集合
                return parameters.Length switch
                {
                    1 => new Padding(System.Convert.ToInt32(parameters[0])),
                    2 => new Padding(
                        System.Convert.ToInt32(parameters[0]),
                        System.Convert.ToInt32(parameters[1]),
                        System.Convert.ToInt32(parameters[0]),
                        System.Convert.ToInt32(parameters[1])),
                    4 => new Padding(
                        System.Convert.ToInt32(parameters[0]),
                        System.Convert.ToInt32(parameters[1]),
                        System.Convert.ToInt32(parameters[2]),
                        System.Convert.ToInt32(parameters[3])),
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
                    // 使用ColorConverter转换颜色名称或HEX值
                    var converter = new System.Drawing.ColorConverter();
                    if (converter.ConvertFromString(colorString) is Color color)
                        return color;

                    // 尝试从已知颜色名称转换
                    if (Enum.TryParse<KnownColor>(colorString, true, out KnownColor knownColor))
                        return Color.FromKnownColor(knownColor);
                }

                // 格式2: 整数值 (ARGB)
                if (parameters[0] is int argb)
                {
                    return Color.FromArgb(argb);
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

    public class FontConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 格式1: 字体描述字符串 "字体名,大小,样式"
                if (parameters[0] is string fontString)
                {
                    var parts = fontString.Split(',');
                    if (parts.Length >= 2)
                    {
                        string fontFamily = parts[0].Trim();
                        float size = float.Parse(parts[1].Trim());

                        FontStyle style = FontStyle.Regular;
                        if (parts.Length >= 3)
                        {
                            if (Enum.TryParse<FontStyle>(parts[2].Trim(), true, out FontStyle parsedStyle))
                                style = parsedStyle;
                        }

                        return new Font(fontFamily, size, style);
                    }
                }

                // 格式2: 系统字体名称
                if (parameters[0] is string systemFontName)
                {
                    var systemFont = GetSystemFont(systemFontName);
                    if (systemFont != null) return systemFont;
                }

                // 格式3: 单独参数 [字体名, 大小, 样式(可选)]
                if (parameters.Length >= 2)
                {
                    string fontFamily = parameters[0]?.ToString() ?? "Microsoft Sans Serif";
                    float size = System.Convert.ToSingle(parameters[1]);
                    FontStyle style = parameters.Length >= 3 ?
                        (FontStyle)Enum.Parse(typeof(FontStyle), parameters[2]?.ToString() ?? "Regular") :
                        FontStyle.Regular;

                    return new Font(fontFamily, size, style);
                }

                return null;
            }
            catch { return null; }
        }

        private static Font? GetSystemFont(string fontName)
        {
            return fontName.ToLowerInvariant() switch
            {
                "captionfont" or "caption" => SystemFonts.CaptionFont,
                "defaultfont" or "default" => SystemFonts.DefaultFont,
                "dialogfont" or "dialog" => SystemFonts.DialogFont,
                "iconfont" or "icon" => SystemFonts.IconTitleFont,
                "menufont" or "menu" => SystemFonts.MenuFont,
                "messageboxfont" or "messagebox" => SystemFonts.MessageBoxFont,
                "smallcaptionfont" or "smallcaption" => SystemFonts.SmallCaptionFont,
                "statusfont" or "status" => SystemFonts.StatusFont,
                _ => null
            };
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
                    var colorConverter = new System.Drawing.ColorConverter();
                    if (colorConverter.ConvertFromString(strValue) is Color color)
                    {
                        return new SolidBrush(color);
                    }
                }

                // 特殊处理Color类型
                if (targetType == typeof(Color))
                {
                    var colorConverter = new System.Drawing.ColorConverter();
                    return colorConverter.ConvertFromString(strValue);
                }

                // 特殊处理Font类型 - 使用FontConverter的正确方法
                if (targetType == typeof(Font))
                {
                    var fontConverter = new System.Drawing.FontConverter();
                    return fontConverter.ConvertFromString(strValue);
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
                // WinForms没有Application.Current.Resources，但可以尝试其他资源查找方式
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}