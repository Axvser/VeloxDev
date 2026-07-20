# 主题

为应用添加深色/浅色动态主题切换，支持动画过渡。主题系统需要 **GUI 项目**（WPF/Avalonia/WinUI）。

---

## 第一步 — 创建 WPF 项目

```shell
dotnet new wpf -n MyThemedApp
cd MyThemedApp
dotnet add package VeloxDev.WPF
```

## 第二步 — 用 ThemeConfig 装饰窗口

将以下代码粘贴到 `MainWindow.xaml.cs`（替换现有的 partial class）：

```csharp
using System.Windows;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;

// 叠加 [ThemeConfig] 特性声明各主题下的属性值
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Foreground), ["#1e1e1e"], ["#ffffff"])]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        InitializeTheme(); // 必须在 InitializeComponent() 之后调用

        // 动画过渡必需
        ThemeManager.SetPlatformInterpolator(new Interpolator());
        ThemeManager.StartModel = StartModel.Cache;
    }

    // 带动画的主题切换
    private void ReverseThemeWithAnimation()
    {
        var target = ThemeManager.Current == typeof(Dark) ? typeof(Light) : typeof(Dark);
        ThemeManager.Transition(target, TransitionEffects.Theme);
    }

    // 即时切换
    private void ReverseThemeInstant()
    {
        if (ThemeManager.Current == typeof(Dark))
            ThemeManager.Jump<Light>();
        else
            ThemeManager.Jump<Dark>();
    }

    // 生命周期钩子 — 每次主题切换时自动调用
    partial void OnThemeChanged(Type? oldTheme, Type? newTheme)
    {
        MessageBox.Show($"主题：{oldTheme?.Name} → {newTheme?.Name}");
    }
}
```

## 第三步 — 在 XAML 中添加切换按钮

`MainWindow.xaml`：

```xml
<Window x:Class="Demo.MainWindow" ...>
    <StackPanel>
        <TextBlock Text="你好 VeloxDev！" FontSize="24" />
        <Button Click="ReverseThemeWithAnimation" Content="切换主题" />
    </StackPanel>
</Window>
```

## 第四步 — 运行

```shell
dotnet run
```

点击按钮 — 窗口背景和文字颜色会在深浅色之间动画过渡。

## 核心 API

| API | 用途 |
|-----|------|
| `[ThemeConfig<TConverter, T1, T2>(...)]` | 声明属性在各主题下的值映射 |
| `InitializeTheme()` | 生成的方法，在 `InitializeComponent()` 后调用 |
| `ThemeManager.Jump<T>()` | 即时切换 |
| `ThemeManager.Transition<T>(effect)` | 动画切换 |
| `ThemeManager.SetPlatformInterpolator()` | 过渡动画必需 |
| `partial void OnThemeChanged(...)` | 生命周期钩子 |
