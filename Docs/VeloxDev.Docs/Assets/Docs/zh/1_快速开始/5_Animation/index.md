# 动画

使用流畅 API 对任意 UI 元素做属性过渡动画 —— **快照模式**（捕获→修改→回放）或**属性模式**（定义目标值）。需要 **GUI 项目**。

---

## Demo 效果

点击"动画" → 矩形向右移动 400px、透明渐隐、颜色变为橙色，1.5 秒后自动反向回弹。

## 操作步骤

### 1. 创建 WPF 项目并安装

```shell
dotnet new wpf -n MyAnimationApp
cd MyAnimationApp
dotnet add package VeloxDev.WPF
```

### 2. 编写

`MainWindow.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using VeloxDev.TransitionSystem;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Rec0.RenderTransform = new TranslateTransform();
    }

    // ── 快照模式 ──────────────────────────────
    private void SnapshotTransition(object sender, RoutedEventArgs e)
    {
        var snap = Rec0.SnapshotAll();    // ① 捕获当前全部属性
        Rec0.Opacity = 0;                 // ② 直接修改目标值
        Rec0.Width = 300;
        snap.Effect(new TransitionEffect   // ③ 设置效果并回放
        {
            Duration = TimeSpan.FromMilliseconds(600),
            Ease = Eases.Back.Out
        }).Execute(Rec0);
    }

    // ── 属性模式 ──────────────────────────────
    private void PropertyTransition(object sender, RoutedEventArgs e)
    {
        // 将动画定义为静态字段可复用
        var animation = Transition<Rectangle>.Create()
            .Property(r => r.Opacity, 0.0)
            .Property(r => ((TranslateTransform)r.RenderTransform).X, 400.0)
            .Property(r => r.Fill, new SolidColorBrush(Colors.Orange))
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(1.5),
                Ease = Eases.Cubic.Out,
                IsAutoReverse = true,
                LoopTime = 2
            });

        animation.Execute(Rec0);
    }

    // ── 演示 Await 延迟 + AwaitThen 拼接 ──────
    private void ChainedTransition(object sender, RoutedEventArgs e)
    {
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform, [
                new TranslateTransform(200, 0),
                new ScaleTransform(1.3, 1.3)
            ])
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(2),
                Ease = Eases.Circ.InOut,
                LoopTime = 2,
                IsAutoReverse = true
            })
            .AwaitThen(TimeSpan.FromSeconds(5)) // 等待 5 秒再执行后续动画
            .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(2),
                Ease = Eases.Sine.In
            })
            .Execute(Rec0);
    }

    private void StopAnimation(object sender, RoutedEventArgs e)
        => Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: false);
}
```

### 3. XAML

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

### 4. 运行

```shell
dotnet run
```

## 两种模式对比

| 模式 | 适用场景 | API |
|------|----------|-----|
| **快照** | 先改属性再回放 | `element.SnapshotAll().Effect(...).Execute(target)` |
| **属性** | 直接定义目标值 | `Transition<T>.Create().Property(...).Effect(...).Execute(target)` |

## 核心 API

| 方法 | 说明 |
|------|------|
| `element.SnapshotAll()` | 捕获所有可动画属性 |
| `Transition<T>.Create()` | 创建属性过渡构建器 |
| `.Property(lambda, value)` | 设置目标属性值 |
| `.Effect(TransitionEffect)` | 设置时长、缓动、循环 |
| `Transition.Exit(target)` | 停止目标上的所有动画 |
