using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using System;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;

namespace Demo.Views;

// 建议把主题相关操作放到单独的 partial 类里，交互逻辑才不会被无关代码搅乱。
// 注意：在 Rider 下这样做可能认不出生成的内容；不影响编译，但可能要重启 Rider 才能恢复识别。

// 用户部分 ↓

[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
public partial class MainWindow : Window
{
    private readonly WindowNotificationManager _message;

    public MainWindow()
    {
        InitializeComponent();
        _message = new WindowNotificationManager(this) { MaxItems = 3 };
        LoadTheme();
    }

    private void ChangeTheme(object sender, RoutedEventArgs e)
    {
        ReverseThemeWithAnimation();
    }
}

public partial class MainWindow
{
    private void LoadTheme()
    {
        InitializeTheme(); // 此调用必需，且必须在 InitializeComponent() 之后

        // 全局生效：不用主题过渡时无需配置插值器，否则此调用是必须的。
        ThemeManager.SetPlatformInterpolator(new Interpolator());

        // 全局生效：主题变化时，动画的起始态取自缓存，还是用反射读当前状态当起点？
        ThemeManager.StartModel = StartModel.Cache;
    }

    // 主题切换有回调。
    partial void OnThemeChanged(Type? oldValue, Type? newValue)
    {
        _message.Show(new Notification("Message", $"Theme changed from {oldValue?.Name} to {newValue?.Name}"));
    }

    // 这类主题切换带渐变动画。
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

    // 这类主题切换没有渐变动画。
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

    // 读取与编辑主题资源包的一组扩展。这些方法都是自动生成的；本例里它们都属于 MainWindow。
    private void ThemeValueEx()
    {
        // 动态编辑主题资源值
        SetThemeValue<Light>(nameof(Background), new object?[] { "#ffffff" });
        // 可还原到初始状态
        RestoreThemeValue<Light>(nameof(Foreground));

        // 取静态资源
        var staticCache = GetStaticThemeCache();
        // 取动态资源
        var dynamicCache = GetActiveThemeCache();

        // 这里的「资源」是自动生成的复杂结构，类型是
        // Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>：从左到右依次是属性名、
        // 参与主题切换的目标、主题、该主题下属性的值。动态资源只保存被改动过的属性，否则什么都不存；
        // 主题切换时动态内容覆盖静态内容。它提供对主题资源的完整访问。
    }
}