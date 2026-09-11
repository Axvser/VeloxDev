using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Shapes;
using Jalium.UI.Threading;
using VeloxDev.TransitionSystem;
using Path = System.IO.Path;

namespace Demo;

/// <summary>Standalone animation test for the VeloxDev.Jalium PlatformAdapters (TransitionSystem),
/// layout and scenarios aligned with the Avalonia/WPF Transition demos: a Grid with 10 rows, a
/// WrapPanel of 7 scenario buttons, 3 rectangles animated with nested TranslateTransform.X
/// paths, transform collections (Translate+Rotate+Scale) and brush fills, plus a 4-cell
/// overshoot strip with its own button row and a sampled readout.</summary>
internal sealed class MainWindow : Window
{
    private readonly Rectangle _rec0;
    private readonly Rectangle _rec1;
    private readonly Rectangle _rec2;
    private readonly Rectangle _over0;
    private readonly Rectangle _over1;
    private readonly Rectangle _over2;
    private readonly Rectangle _over3;
    private readonly TextBlock _readout;

    public MainWindow()
    {
        Title = "VeloxDev Transition - Jalium";
        Width = 900;
        Height = 780;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        var grid = new Grid();
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(100));
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(100));
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(100));
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(110));
        AddRow(grid, new GridLength(48));
        AddRow(grid, new GridLength(44));

        // 先建矩形：下面的按钮 lambda 捕获 _rec0，字段必须在捕获前完成赋值
        _rec0 = MakeRect(Colors.Cyan);
        _rec1 = MakeRect(Colors.Lime);
        _rec2 = MakeRect(Colors.Orange);

        // 过冲条：4 个并排目标，让 Back 与 Elastic、颜色与尺寸能在同一次运行里对比，而不是一次只看一个。
        // Over3 是黄→紫渐变，过冲时越过紫色而不是回绕。Over0 的 RenderTransform 会被就地改写，所以只建一次。
        _over0 = MakeOverRect(new SolidColorBrush(OverColorStart));
        _over1 = MakeOverRect(new SolidColorBrush(OverColorStart));
        _over2 = MakeOverRect(new SolidColorBrush(Color.FromRgb(0x7A, 0x5A, 0xA5)));
        _over3 = MakeOverRect(CreateBs1Brush());
        _over0.RenderTransform = new TranslateTransform();

        var strip = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(6) };
        strip.Children.Add(_over0);
        strip.Children.Add(_over1);
        strip.Children.Add(_over2);
        strip.Children.Add(_over3);
        Grid.SetRow(strip, 7);
        grid.Children.Add(strip);

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

        Grid.SetRow(_rec0, 1);
        Grid.SetRow(_rec1, 3);
        Grid.SetRow(_rec2, 5);
        grid.Children.Add(_rec0);
        grid.Children.Add(_rec1);
        grid.Children.Add(_rec2);

        var overButtons = new WrapPanel();
        overButtons.Children.Add(MakeButton("位移 Back.Out", (_, _) => OverShootScalarBack()));
        overButtons.Children.Add(MakeButton("位移 Elastic.Out", (_, _) => OverShootScalarElastic()));
        overButtons.Children.Add(MakeButton("颜色过冲", (_, _) => OverShootColor()));
        overButtons.Children.Add(MakeButton("尺寸过冲", (_, _) => OverShootSize()));
        overButtons.Children.Add(MakeButton("渐变过冲", (_, _) => OverShootGradient()));
        overButtons.Children.Add(MakeButton("重置过冲", (_, _) => ResetOverShoot()));
        Grid.SetRow(overButtons, 8);
        grid.Children.Add(overButtons);

        // 读数才是让过冲可观测的东西：数字越过目标再回来，光靠眼睛和一条更慢的缓动分不开。
        _readout = new TextBlock
        {
            Margin = new Thickness(8, 6, 8, 2),
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            Text = "按上面任一按钮；读数显示当前值与目标值",
        };
        Grid.SetRow(_readout, 9);
        grid.Children.Add(_readout);

        Content = grid;

        // 读数用定时器采样目标属性，不用 effect 的事件：流水线每段都会 Clone() effect，
        // 订阅在原始 effect 上的处理函数不会触发。
        var readoutTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        readoutTimer.Tick += (_, _) => _readout.Text =
            $"位移 X   目标 {ShiftTarget,6:F1}   当前 {((TranslateTransform)_over0.RenderTransform!).X,7:F1}"
            + $"     |     宽度   目标 {WidthTarget,6:F1}   当前 {_over2.Width,7:F1}";
        readoutTimer.Start();

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

    // 过冲条里的小目标：居中而不是靠左，位移场景才能在格子内看出越界再回弹。
    private static Rectangle MakeOverRect(Brush fill, double width = OverWidthStart)
    {
        return new Rectangle
        {
            Width = width,
            Height = 60,
            Fill = fill,
            HorizontalAlignment = HorizontalAlignment.Center,
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
        ResetOverShoot();
    }

    private void ExitAll()
    {
        Transition.Exit(_rec0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(_rec1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(_rec2, IncludeMutual: true, IncludeNoMutual: true);
    }

    private void ResetOverShoot()
    {
        Transition.Exit(_over0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(_over1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(_over2, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(_over3, IncludeMutual: true, IncludeNoMutual: true);

        // 直接写初值：Over0 的 RenderTransform 被就地改写，每次换新对象；
        // Fill 是整体替换而非就地修改，所以初值能完整恢复。
        _over0.RenderTransform = new TranslateTransform();
        _over1.Fill = new SolidColorBrush(OverColorStart);
        _over2.Width = OverWidthStart;
        _over3.Fill = CreateBs1Brush();
    }

    // ── Animations (aligned with Avalonia/WPF) ──────────────────────────────

    // Simple: nested TranslateTransform.X path + solid fill.
    private static readonly Transition<Rectangle> Animation0 =
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
    private static readonly Transition<Rectangle> Animation1 =
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
    private static readonly Transition<Rectangle> Animation2 =
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

    // ── 过冲（overshoot）────────────────────────────────────────────────────
    //
    // 上面那些缓动全都落在 [0,1] 内，没有一个能越过目标再回来。Back 峰值 1.10、Elastic 峰值 1.37，
    // 过冲具体意味着什么由采样器决定：R/G/B 共用一个进度、在第一个触边的通道处停下；Size/Rect 这类
    // 结构体尺寸同理在 0 处停下；而自带区间的通道（alpha/opacity）与普通 double 保留完整的缓动时间。
    // 读数才让过冲可观测：数字越过目标再回来，光靠眼睛和一条更慢的缓动分不开。
    // ───────────────────────────────────────────────────────────────────────

    // 目标值与动画共用同一个常量，避免读数与动画各写一份字面量后漂移。
    private const double ShiftTarget = 300d;
    private const double WidthTarget = 220d;

    // 过冲条各目标的起始值：构造函数、每次运行前的归位、重置三处共用同一份定义。
    private const double OverWidthStart = 80d;
    private static readonly Color OverColorStart = Color.FromRgb(0x3A, 0x6E, 0xA5);

    private static readonly Transition<Rectangle> OverScalarBack =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, ShiftTarget)
            .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(0.9), Ease = Eases.Back.Out });

    private static readonly Transition<Rectangle> OverScalarElastic =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, ShiftTarget)
            .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Elastic.Out });

    // 目标色每个通道都留有余量：有余量时共享进度允许颜色整体过冲，直到第一个触到 255 的通道才停下，
    // 所以变的是亮度而不是色相。若逐通道各自截断，绿蓝会跑过红，色相就被挪走了。
    private static readonly Transition<Rectangle> OverColor =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0xD0)))
            .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(0.9), Ease = Eases.Back.Out });

    // Width 在 Jalium 上是普通 double，走 DoubleSampler，没有 0 界：直接外推（Elastic 峰值处 80+140*1.37≈272）。
    // 起点 80 < 终点 220，而 EaseOutElastic 的最小值仍 >0，所以不会出现负宽度，也就不会触发布局对负尺寸的拒绝。
    private static readonly Transition<Rectangle> OverSize =
        Transition<Rectangle>.Create()
            .Property(r => r.Width, WidthTarget)
            .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Elastic.Out });

    // 非纯色 Fill 走的是混合刷路径而不是纯色路径：交叉淡出的系数是一个比例，因此在两端饱和而不是越界。
    private static readonly Transition<Rectangle> OverBrush =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, CreateShiftedBs1())
            .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Back.Out });

    // 每次运行前先把自己的目标同步写回起始值，而不是动画回去：Prepare 会把目标的当前值当作动画起点，
    // 少了这一步，第二次点击——或共用同一元素的兄弟按钮——就会从目标动到目标，看起来什么都没发生。
    // 只重置这条自己拥有的元素：动别的元素会取消那边的运行，并排对比就没了。
    private void OverShootScalarBack() => RunScalar(OverScalarBack);
    private void OverShootScalarElastic() => RunScalar(OverScalarElastic);

    private void RunScalar(Transition<Rectangle> animation)
    {
        Transition.Exit(_over0, IncludeMutual: true, IncludeNoMutual: true);
        ((TranslateTransform)_over0.RenderTransform!).X = 0;
        animation.Execute(_over0);
    }

    private void OverShootColor()
    {
        Transition.Exit(_over1, IncludeMutual: true, IncludeNoMutual: true);
        _over1.Fill = new SolidColorBrush(OverColorStart);
        OverColor.Execute(_over1);
    }

    private void OverShootSize()
    {
        Transition.Exit(_over2, IncludeMutual: true, IncludeNoMutual: true);
        _over2.Width = OverWidthStart;
        OverSize.Execute(_over2);
    }

    private void OverShootGradient()
    {
        Transition.Exit(_over3, IncludeMutual: true, IncludeNoMutual: true);
        _over3.Fill = CreateBs1Brush();
        OverBrush.Execute(_over3);
    }

    private static LinearGradientBrush CreateShiftedBs1()
    {
        return new LinearGradientBrush(
            new GradientStopCollection(
            [
                new GradientStop(Colors.Cyan, 0.25),
                new GradientStop(Colors.Orange, 0.75),
            ]),
            new Point(0, 0),
            new Point(1, 1));
    }

    // 对照 WPF demo 里的 Bs1 资源（黄→紫，左上到右下）在代码里重建：资源不是过渡路径能指向的值。
    private static LinearGradientBrush CreateBs1Brush()
    {
        return new LinearGradientBrush(
            new GradientStopCollection(
            [
                new GradientStop(Colors.Yellow, 0),
                new GradientStop(Colors.Violet, 1),
            ]),
            new Point(0, 0),
            new Point(1, 1));
    }
}
