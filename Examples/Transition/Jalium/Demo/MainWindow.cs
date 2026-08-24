using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Shapes;
using VeloxDev.TransitionSystem;
using Path = System.IO.Path;

namespace Demo;

/// <summary>Standalone animation test for the VeloxDev.Jalium PlatformAdapters (TransitionSystem),
/// layout and scenarios aligned with the Avalonia/WPF Transition demos: a Grid with 7 rows, a
/// WrapPanel of 7 scenario buttons, and 3 rectangles animated with nested TranslateTransform.X
/// paths, transform collections (Translate+Rotate+Scale), and brush fills.</summary>
internal sealed class MainWindow : Window
{
    private readonly Rectangle _rec0;
    private readonly Rectangle _rec1;
    private readonly Rectangle _rec2;

    public MainWindow()
    {
        Title = "VeloxDev Transition - Jalium";
        Width = 900;
        Height = 560;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        var grid = new Grid();
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(100));
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(100));
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(100));
        AddRow(grid, GridLength.Star);

        var buttons = new WrapPanel();
        buttons.Children.Add(MakeButton("主线程互斥", (_, _) => LoadMainThread()));
        buttons.Children.Add(MakeButton("后台线程互斥", (_, _) => _ = Task.Run(LoadMainThread)));
        buttons.Children.Add(MakeButton("主线程并发", (_, _) => LoadMainThreadNonMutual()));
        buttons.Children.Add(MakeButton("后台线程并发", (_, _) => _ = Task.Run(LoadMainThreadNonMutual)));
        buttons.Children.Add(MakeButton("连续互斥", (_, _) => _ = Task.Run(() => Animation0.Execute(_rec0))));
        buttons.Children.Add(MakeButton("重置", (_, _) => Reset()));
        buttons.Children.Add(MakeButton("停止全部", (_, _) => ExitAll()));
        Grid.SetRow(buttons, 0);
        grid.Children.Add(buttons);

        _rec0 = MakeRect(Colors.Cyan);
        _rec1 = MakeRect(Colors.Lime);
        _rec2 = MakeRect(Colors.Orange);
        Grid.SetRow(_rec0, 1);
        Grid.SetRow(_rec1, 3);
        Grid.SetRow(_rec2, 5);
        grid.Children.Add(_rec0);
        grid.Children.Add(_rec1);
        grid.Children.Add(_rec2);

        Content = grid;

        // Rec0's RenderTransform is a TranslateTransform; Rec1/Rec2 start null and are animated
        // to a TransformGroup. Auto-run one animation on load and record the result so a headless
        // run can assert interpolation + UI-thread marshalling worked end to end.
        _rec0.RenderTransform = new TranslateTransform();
        Loaded += (_, _) =>
        {
            var effect = new TransitionEffect { Duration = TimeSpan.FromMilliseconds(800), FPS = 60 };
            effect.Completed += (_, _) =>
            {
                try
                {
                    var x = (_rec0.RenderTransform as TranslateTransform)?.X ?? double.NaN;
                    File.WriteAllText(
                        Path.Combine(Path.GetTempPath(), "jalium-transition-demo-ok.txt"),
                        $"transition ran; rec0 X={x:F0}");
                }
                catch (IOException) { }
            };
            Transition<Rectangle>.Create()
                .Property(r => ((TranslateTransform)r.RenderTransform!).X, 300d)
                .Effect(effect)
                .Execute(_rec0);
        };
    }

    private static void AddRow(Grid grid, GridLength height)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = height });
    }

    private Rectangle MakeRect(Color fill)
    {
        // 100×100 squares (not stretched into bars), like the reference demos.
        return new Rectangle
        {
            Width = 100,
            Height = 100,
            Fill = new SolidColorBrush(fill),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private Button MakeButton(string text, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, Foreground = new SolidColorBrush(Colors.White) },
            Margin = new Thickness(4),
        };
        button.Click += onClick;
        return button;
    }

    // ── Scenarios (aligned with Avalonia/WPF) ───────────────────────────────

    private void LoadMainThread()
    {
        // Mutual (default): a new animation interrupts the old one.
        Animation0.Execute(_rec0);
        Animation1.Execute(_rec1);
        Animation2.Execute(_rec2);
    }

    private void LoadMainThreadNonMutual()
    {
        Animation0.Execute(_rec0, CanMutualTask: false);
        Animation1.Execute(_rec1, CanMutualTask: false);
        Animation2.Execute(_rec2, CanMutualTask: false);
    }

    private void Reset()
    {
        ExitAll();
        _rec0.RenderTransform = new TranslateTransform();
        _rec0.Fill = new SolidColorBrush(Colors.Cyan);
        _rec0.Opacity = 1d;
        _rec1.RenderTransform = null;
        _rec1.Fill = new SolidColorBrush(Colors.Lime);
        _rec2.RenderTransform = null;
        _rec2.Fill = new SolidColorBrush(Colors.Orange);
    }

    private void ExitAll()
    {
        Transition.Exit(_rec0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(_rec1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(_rec2, IncludeMutual: true, IncludeNoMutual: true);
    }

    // ── Animations (aligned with Avalonia/WPF) ──────────────────────────────

    // Simple: nested TranslateTransform.X path + solid fill.
    private static readonly Transition<Rectangle>.StateSnapshot Animation0 =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, 300)
            .Property(r => r.Fill, new SolidColorBrush(Colors.OrangeRed))
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Sine.InOut,
            });

    // Delayed: transform collection (Translate + Rotate) + fill.
    private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(5))
            .Property(r => r.RenderTransform,
                [new TranslateTransform(-200, 0), new RotateTransform(180)],
                RotationDirection.ClockWise)
            .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(4),
                IsAutoReverse = true,
                FPS = 144,
                LoopTime = 4,
            });

    // Combined: transform collection (Translate + Scale) + fill, then AwaitThen + fill.
    private static readonly Transition<Rectangle>.StateSnapshot Animation2 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform,
                [new TranslateTransform(200, 0), new ScaleTransform(1.3, 1.3)],
                RotationDirection.CounterClockWise)
            .Property(r => r.Fill, new SolidColorBrush(Colors.LightSeaGreen))
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(3),
                IsAutoReverse = true,
                FPS = 144,
                Ease = Eases.Circ.InOut,
                LoopTime = 2,
            })
            .AwaitThen(TimeSpan.FromSeconds(5))
            .Property(r => r.Fill, new SolidColorBrush(Colors.Lime))
            .Effect(e =>
            {
                e.Duration = TimeSpan.FromSeconds(4);
                e.FPS = 144;
                e.Ease = Eases.Sine.In;
            });
}
