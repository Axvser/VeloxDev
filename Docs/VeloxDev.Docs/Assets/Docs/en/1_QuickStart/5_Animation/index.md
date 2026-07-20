# Animation

Animate any UI element using `Transition<T>` — a fluent, state-snapshot-based animation system. Requires a **GUI project**.

---

## Step 1 — Create a WPF Project

```shell
dotnet new wpf -n MyAnimationApp
cd MyAnimationApp
dotnet add package VeloxDev.WPF
```

## Step 2 — Paste into `MainWindow.xaml.cs`

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
    }

    // ── Snapshot-based: capture current state → mutate → animate back ──

    private void SnapshotTransition(object sender, RoutedEventArgs e)
    {
        var snap = Rec0.SnapshotAll();          // 1. capture current state
        Rec0.Opacity = 0;                       // 2. mutate
        Rec0.Width = 300;
        snap.Effect(new TransitionEffect       // 3. animate back
        {
            Duration = TimeSpan.FromMilliseconds(600),
            Ease = Eases.Back.Out
        }).Execute(Rec0);
    }

    // ── Property-based: define target values and let it animate ──

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

    private void StopAnimation(object sender, RoutedEventArgs e)
    {
        Transition.Exit(Rec0);
    }
}

// The animation definitions can live in a separate partial
public partial class MainWindow
{
    private static readonly Transition<Rectangle>.StateSnapshot BounceAnim =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform).X, 600)
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromMilliseconds(800),
                Ease = Eases.Elastic.Out
            });
}
```

## Step 3 — Add UI in `MainWindow.xaml`

```xml
<Window x:Class="Demo.MainWindow" ...>
    <StackPanel>
        <Rectangle x:Name="Rec0" Width="100" Height="100" Fill="CornflowerBlue">
            <Rectangle.RenderTransform>
                <TranslateTransform />
            </Rectangle.RenderTransform>
        </Rectangle>
        <Button Content="Snapshot" Click="SnapshotTransition" />
        <Button Content="Animate" Click="PropertyTransition" />
        <Button Content="Stop" Click="StopAnimation" />
    </StackPanel>
</Window>
```

## Step 4 — Run

```shell
dotnet run
```

Click "Animate" — the rectangle fades, slides right, and changes color with elastic easing.

## Key APIs

| API | Purpose |
|-----|---------|
| `element.SnapshotAll()` | Capture all animatable properties |
| `element.Snapshot(x => x.Opacity)` | Capture specific properties |
| `Transition<T>.Create()` | Create a new property-based animation |
| `.Property(lambda, value)` | Set a target property value |
| `.Effect(TransitionEffect)` | Set duration, easing, FPS, loop |
| `.Execute(target)` | Run the animation |
| `Transition.Exit(target)` | Stop running animations |
