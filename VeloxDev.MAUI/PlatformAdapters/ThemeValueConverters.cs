using Microsoft.Maui.Converters;
using Microsoft.Maui.Graphics.Converters;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using VeloxDev.Core.Interfaces.DynamicTheme;

namespace VeloxDev.MAUI.PlatformAdapters
{
    public class DoubleConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 使用 MAUI 推荐的类型转换方式
                if (parameters[0] is string strValue)
                {
                    if (double.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
                    {
                        return result;
                    }
                }

                // 处理其他类型
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
                // 使用 MAUI 内置的点解析
                if (parameters[0] is string strValue)
                {
                    // 使用 PointTypeConverter 进行转换
                    var converter = new PointTypeConverter();
                    return converter.ConvertFromInvariantString(strValue);
                }

                // 多参数构造
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
                // 使用 MAUI 内置的厚度解析
                if (parameters[0] is string strValue)
                {
                    var converter = new ThicknessTypeConverter();
                    return converter.ConvertFromInvariantString(strValue);
                }

                // 多参数构造
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
                // 使用 MAUI 内置的圆角解析
                if (parameters[0] is string strValue)
                {
                    var converter = new CornerRadiusTypeConverter();
                    return converter.ConvertFromInvariantString(strValue);
                }

                // 多参数构造
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
                // 使用 MAUI 内置的颜色解析
                if (parameters[0] is string colorString)
                {
                    var converter = new ColorTypeConverter();
                    return converter.ConvertFromInvariantString(colorString);
                }

                // 整数值 (ARGB)
                if (parameters[0] is int argb)
                {
                    return Color.FromInt(argb);
                }

                // 单独分量
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
                // 1. 直接传递笔刷
                if (parameters[0] is Brush brush)
                    return brush;

                // 2. 资源键查找 - 使用 MAUI 官方资源查找机制
                if (parameters[0] is string resourceKey)
                {
                    // 获取目标元素（资源查找上下文）
                    var targetElement = GetTargetElement(parameters);

                    // 使用 MAUI 官方资源查找方式
                    object? resource = null;

                    // 首先尝试元素级资源查找
                    if (targetElement != null)
                    {
                        resource = FindElementResource(targetElement, resourceKey);
                    }

                    // 如果未找到，尝试应用级资源
                    resource ??= FindApplicationResource(resourceKey);

                    if (resource is Brush foundBrush)
                    {
                        return foundBrush;
                    }

                    Debug.WriteLine($"Brush resource '{resourceKey}' not found");
                }

                // 3. 颜色字符串解析
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

        // 获取目标元素
        private static IElement? GetTargetElement(object?[] parameters)
        {
            // 1. 尝试从参数获取显式目标（通常是控件本身）
            if (parameters.Length > 1 && parameters[1] is IElement explicitTarget)
            {
                return explicitTarget;
            }

            // 2. 尝试获取当前页面（使用 MAUI 官方方法获取当前上下文）
            return Shell.Current?.CurrentPage ?? Application.Current?.MainPage;
        }

        // MAUI 官方推荐的元素级资源查找
        private static object? FindElementResource(IElement element, string key)
        {
            // 检查元素自身的资源
            if (element is VisualElement visualElement &&
                visualElement.Resources?.TryGetValue(key, out var resource) == true)
            {
                return resource;
            }

            // 向上遍历父级查找
            if (element is Element mauiElement && mauiElement.Parent is IElement parent)
            {
                return FindElementResource(parent, key);
            }

            return null;
        }

        // MAUI 官方推荐的应用级资源查找
        private static object? FindApplicationResource(string key)
        {
            if (Application.Current == null) return null;

            // 检查应用级资源
            if (Application.Current.Resources?.TryGetValue(key, out var resource) == true)
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
            // 参数验证
            if (parameters == null || parameters.Length != 1 || parameters[0] is not string strValue)
                return null;

            try
            {
                // 1. 尝试资源查找
                if (Application.Current?.Resources?.TryGetValue(strValue, out var resource) == true &&
                    targetType.IsInstanceOfType(resource))
                {
                    return resource;
                }

                // 2. 使用 .NET 类型转换器
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);
                if (converter?.CanConvertFrom(typeof(string)) == true)
                {
                    return converter.ConvertFromInvariantString(strValue);
                }

                // 3. 特殊处理 MAUI 特定类型
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
}
