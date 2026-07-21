# Animation

Animate any UI element using a fluent API — **Snapshot mode** (capture → mutate → replay) or **Property mode** (define target values). Requires a **GUI project**.

---

## Demo

Click "Animate" → Rectangle moves 400px right, fades out, turns orange, then auto-reverses after 1.5s.

## Steps

### 1. Create a WPF Project and Install

```shell
dotnet new wpf -n MyAnimationApp
cd MyAnimationApp
dotnet add package VeloxDev.WPF
```

### 2. Write Code

`MainWindow.xaml.cs`:

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

    // ── Snapshot mode ──────────────────────────────
    private void SnapshotTransition(object sender, RoutedEventArgs e)
    {
        var snap = Rec0.SnapshotAll();    // ① capture all animatable properties
        Rec0.Opacity = 0;                 // ② mutate target values directly
        Rec0.Width = 300;
        snap.Effect(new TransitionEffect   // ③ set effect and replay
        {
            Duration = TimeSpan.FromMilliseconds(600),
            Ease = Eases.Back.Out
        }).Execute(Rec0);
    }

    // ── Property mode ──────────────────────────────
    private void PropertyTransition(object sender, RoutedEventArgs e)
    {
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

    // ── Chained with Await/AwaitThen ───────────────
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
            .AwaitThen(TimeSpan.FromSeconds(5)) // wait 5s before next segment
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
        <Button Content="Snapshot" Click="SnapshotTransition" />
        <Button Content="Animate" Click="PropertyTransition" />
        <Button Content="Stop" Click="StopAnimation" />
    </StackPanel>
</Window>
```

### 4. Run

```shell
dotnet run
```

## Mode Comparison

| Mode | Use Case | API |
|------|----------|-----|
| **Snapshot** | Mutate first, then animate back | `element.SnapshotAll().Effect(...).Execute(target)` |
| **Property** | Define target values directly | `Transition<T>.Create().Property(...).Effect(...).Execute(target)` |

## Key APIs

| Method | Description |
|--------|-------------|
| `element.SnapshotAll()` | Capture all animatable properties |
| `Transition<T>.Create()` | Create a property transition builder |
| `.Property(lambda, value)` | Set target property value |
| `.Effect(TransitionEffect)` | Set duration, easing, looping |
| `Transition.Exit(target)` | Stop all animations on target |

