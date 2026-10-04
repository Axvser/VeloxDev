using Microsoft.Maui.Converters;
using Microsoft.Maui.Graphics.Converters;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace VeloxDev.DynamicTheme
{
    public class DoubleConverter : IThemeValueConverter
    {
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 用 MAUI 推荐的类型转换。
                if (parameters[0] is string strValue)
                {
                    if (double.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
                    {
                        return result;
                    }
                }

                // 处理其它类型。
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 用 MAUI 内置的 Point 解析。
                if (parameters[0] is string strValue)
                {
                    // 用 PointTypeConverter 转换。
                    var converter = new PointTypeConverter();
                    return converter.ConvertFromInvariantString(strValue);
                }

                // 多参数构造。
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 用 MAUI 内置的 Thickness 解析。
                if (parameters[0] is string strValue)
                {
                    var converter = new ThicknessTypeConverter();
                    return converter.ConvertFromInvariantString(strValue);
                }

                // 多参数构造。
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 用 MAUI 内置的 CornerRadius 解析。
                if (parameters[0] is string strValue)
                {
                    var converter = new CornerRadiusTypeConverter();
                    return converter.ConvertFromInvariantString(strValue);
                }

                // 多参数构造。
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                // 用 MAUI 内置的颜色解析。
                if (parameters[0] is string colorString)
                {
                    var converter = new ColorTypeConverter();
                    return converter.ConvertFromInvariantString(colorString);
                }

                // 整数值（ARGB）
                if (parameters[0] is int argb)
                {
                    return Color.FromInt(argb);
                }

                // 各分量
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1)
                return null;

            try
            {
                // 1. 直接传画刷
                if (parameters[0] is Brush brush)
                    return brush;

                // 2. 资源键查找 —— 用 MAUI 官方的资源查找机制
                if (parameters[0] is string resourceKey)
                {
                    // 取目标元素（资源查找上下文）。
                    var targetElement = GetTargetElement(parameters);

                    // 用 MAUI 官方的资源查找。
                    object? resource = null;

                    // 先试元素级资源查找。
                    if (targetElement != null)
                    {
                        resource = FindElementResource(targetElement, resourceKey);
                    }

                    // 找不到再试应用级资源。
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

        // 取目标元素。
        private static IElement? GetTargetElement(object?[] parameters)
        {
            // 1. 先试从参数取显式目标（通常是控件本身）。
            if (parameters.Length > 1 && parameters[1] is IElement explicitTarget)
            {
                return explicitTarget;
            }

            // 2. 再试取当前页（用 MAUI 官方方法取当前上下文）。
            return Shell.Current?.CurrentPage ?? Application.Current?.Windows[0].Page;
        }

        // MAUI 官方推荐的元素级资源查找。
        private static object? FindElementResource(IElement element, string key)
        {
            // 查元素自身的资源。
            if (element is VisualElement visualElement &&
                ThemeResourceLookup.TryFindInResourceDictionary(visualElement.Resources, key, out var resource))
            {
                return resource;
            }

            // 沿父链上溯找资源。
            if (element is Element mauiElement && mauiElement.Parent is IElement parent)
            {
                return FindElementResource(parent, key);
            }

            return null;
        }

        // MAUI 官方推荐的应用级资源查找。
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
        /// <inheritdoc />
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            // 参数校验
            if (parameters == null || parameters.Length < 1 || parameters[0] is not string strValue)
                return null;

            try
            {
                // 1. 先试资源查找
                if (ThemeResourceLookup.TryFindApplicationResource(strValue, out var resource) &&
                    targetType.IsInstanceOfType(resource))
                {
                    return resource;
                }

                // 到此为止 —— 平台自己的类型由上面几步负责，其它类型不再支持。
                // 自定义类型（含可插值的那些）由宿主自己写一个 IThemeValueConverter，声明在
                // [ThemeConfig<...>] 上：那是本模块记录在案的扩展点，也是这里不再需要反射的原因。
                // 3. 单独处理 MAUI 专属类型
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
        /// <summary>Tries to resolve the application-level resource named <paramref name="key"/>.</summary>
        public static bool TryFindApplicationResource(string key, out object? value)
        {
            value = null;
            return TryFindInResourceDictionary(Application.Current?.Resources, key, out value);
        }

        /// <summary>Tries to resolve <paramref name="key"/> from <paramref name="dictionary"/>.</summary>
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
