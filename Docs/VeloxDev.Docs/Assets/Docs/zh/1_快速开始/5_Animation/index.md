# 动画

使用 `Transition<T>` 的流畅 API 对任意 UI 元素做动画。需要 **GUI 项目**。

---

## 第一步 — 创建 WPF 项目

```shell
dotnet new wpf -n MyAnimationApp
cd MyAnimationApp
dotnet add package VeloxDev.WPF
```

## 第二步 — 粘贴到 `MainWindow.xaml.cs`

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using VeloxDev.TransitionSystem;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // ── 快照过渡：捕获状态 → 修改 → 回放 ──

    private void SnapshotTransition(object sender, RoutedEventArgs e)
    {
        var snap = Rec0.SnapshotAll();
        Rec0.Opacity = 0;
        Rec0.Width = 300;
        snap.Effect(new TransitionEffect
        {
            Duration = TimeSpan.FromMilliseconds(600),
            Ease = Eases.Back.Out
        }).Execute(Rec0);
    }

    // ── 属性过渡：定义目标值 ──

    private void PropertyTransition(object sender, RoutedEventArgs e)
    {
        Transition<Rectangle>.Create()
            .Property(r => r.Opacity, 0.0)
            .Property(r => ((TranslateTransform)r.RenderTransform).X, 400.0)
            .Property(r => r.Fill, new SolidColorBrush(Colors.Orange))
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(1.5),
                Ease = Eases.Cubic.Out,
                IsAutoReverse = true,
                LoopTime = 2
            })
            .Execute(Rec0);
    }

    private void StopAnimation(object sender, RoutedEventArgs e) => Transition.Exit(Rec0);
}
```

## 第三步 — 在 `MainWindow.xaml` 中添加 UI

```xml
<Window x:Class="Demo.MainWindow" ...>
    <StackPanel>
        <Rectangle x:Name="Rec0" Width="100" Height="100" Fill="CornflowerBlue">
            <Rectangle.RenderTransform><TranslateTransform /></Rectangle.RenderTransform>
        </Rectangle>
        <Button Content="快照" Click="SnapshotTransition" />
        <Button Content="动画" Click="PropertyTransition" />
        <Button Content="停止" Click="StopAnimation" />
    </StackPanel>
</Window>
```

## 第四步 — 运行

```shell
dotnet run
```

## 核心 API

| API | 用途 |
|-----|------|
| `element.SnapshotAll()` | 捕获所有可动画属性 |
| `Transition<T>.Create()` | 创建属性过渡 |
| `.Property(lambda, value)` | 设置目标属性值 |
| `.Effect(TransitionEffect)` | 设置时长、缓动等 |
| `.Execute(target)` | 执行动画 |
| `Transition.Exit(target)` | 停止动画 |
