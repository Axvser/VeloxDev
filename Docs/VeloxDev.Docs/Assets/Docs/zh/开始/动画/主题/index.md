# 主题

这是由动画系统延伸出来的分支能力，可以在UI中自然地从一套主题配色过渡到另外一套主题配色

> **配置**

```csharp
// [ 全局生效 ]
// 为主题系统注册平台适配包（ 如VeloxDev.WPF）提供的插值器
ThemeManager.SetPlatformInterpolator(new Interpolator());

// [ 全局生效 ]
// 当主题发生变化，您希望动画的起始状态是从缓存获取呢？还是反射获取当前状态作为起始呢？
ThemeManager.StartModel = StartModel.Cache;
```

> **构建**

```csharp
// 至多同时配置7个主题
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
public partial class MainWindow : Window
{    
    public MainWindow()
    {
        InitializeComponent();

        // [ 局部生效 ]
        InitializeTheme();
    }
}
```

> **切换主题**

如果你配置了插值器，那么下述函数将变得可用

```csharp
private static void ReverseThemeWithAnimation()
{
    var condition = ThemeManager.Current == typeof(Dark);
    if (condition)
    {
        ThemeManager.Transition<Light>(TransitionEffects.Theme);
    }
    else
    {
        ThemeManager.Transition<Dark>(TransitionEffects.Theme);
    }
}
```

当然，考虑到一些场景下渐变式切换可能导致性能问题，也提供了不加载动画的方式，这种方式无论何时都受到支持

```csharp
private static void ReverseThemeWithOutAnimation()
{
    var condition = ThemeManager.Current == typeof(Dark);
    if (condition)
    {
        ThemeManager.Jump<Light>();
    }
    else
    {
        ThemeManager.Jump<Dark>();
    }
}
```

> **回调**

```csharp
partial void OnThemeChanged(Type? oldValue, Type? newValue)
{

}
```

> **自定义主题**

```csharp
public class Light : ITheme
{

}
```

> **自定义值转换器**

```csharp
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
```