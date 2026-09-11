using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Shapes;
using Jalium.UI.Threading;
using System.Diagnostics;
using VeloxDev.TransitionSystem;
using Path = System.IO.Path;

namespace Demo;

/// <summary>Standalone animation test for the VeloxDev.Jalium PlatformAdapters (TransitionSystem),
/// layout and scenarios aligned with the Avalonia/WPF Transition demos: a Grid with 14 rows, a
/// WrapPanel of 7 scenario buttons, 3 rectangles animated with nested TranslateTransform.X
/// paths, transform collections (Translate+Rotate+Scale) and brush fills, plus a 4-cell
/// overshoot strip with its own button row, a sampled readout, a strip of sampler-conformance
/// handles publishing the payload their clicks produce, and a bench above it that plays the
/// clicked sampler across an eased sweep so the same value can be watched moving.</summary>
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

    // 机器可读读数（载荷）载体，与人类读数并排
    private readonly TextBlock _overState;

    // 采样器一致性的载荷载体：由把手点击驱动，每次点击只跑那一条采样器
    private readonly TextBlock _overConf;

    public MainWindow()
    {
        Title = "VeloxDev Transition - Jalium";
        Width = 900;
        Height = 980;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        var grid = new Grid();
        // 加载模式那一排：三块目标与过冲格同尺寸（80x60），两半读起来是一块台子。
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(60));
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(60));
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(60));
        AddRow(grid, GridLength.Star);
        AddRow(grid, new GridLength(110));
        AddRow(grid, new GridLength(48));
        AddRow(grid, new GridLength(44));
        AddRow(grid, new GridLength(30));
        AddRow(grid, new GridLength(92));
        AddRow(grid, new GridLength(72));
        AddRow(grid, new GridLength(30));

        // 先建矩形：下面的按钮 lambda 捕获 _rec0，字段必须在捕获前完成赋值
        _rec0 = MakeRect(Colors.Cyan);
        _rec1 = MakeRect(Colors.Lime);
        // Rec2 起止两端都是渐变 —— 上侧这一排里特意留一块跑"渐变 → 渐变"，因为那条路走的是画刷
        // 交叉淡出（非纯色分支），与另外两块的纯色路径不是同一段代码。
        _rec2 = MakeRect(CreateBs1Brush());

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
        // 令牌与其余六个平台逐字一致 —— 验收套件靠它点这一排。
        buttons.Children.Add(MakeButton("主线程互斥", (_, _) => LoadMainThread(), "over.btn.load.main"));
        buttons.Children.Add(MakeButton("后台线程互斥", (_, _) => _ = Task.Run(LoadMainThread), "over.btn.load.background"));
        buttons.Children.Add(MakeButton("主线程并发", (_, _) => LoadMainThreadNonMutual(), "over.btn.load.main.concurrent"));
        buttons.Children.Add(MakeButton("后台线程并发", (_, _) => _ = Task.Run(LoadMainThreadNonMutual), "over.btn.load.background.concurrent"));
        buttons.Children.Add(MakeButton("连续互斥", (_, _) => _ = Task.Run(() => Animation0.Execute(_rec0)), "over.btn.load.repeat"));
        buttons.Children.Add(MakeButton("重置", (_, _) => Reset(), "over.btn.reset.all"));
        buttons.Children.Add(MakeButton("停止全部", (_, _) => ExitAll(), "over.btn.stop.all"));
        Grid.SetRow(buttons, 0);
        grid.Children.Add(buttons);

        Grid.SetRow(_rec0, 1);
        Grid.SetRow(_rec1, 3);
        Grid.SetRow(_rec2, 5);
        grid.Children.Add(_rec0);
        grid.Children.Add(_rec1);
        grid.Children.Add(_rec2);

        // AutomationId 是验收套件抓取这些控件的把手：测试永远不匹配中文标签，token 与语言无关且稳定。
        var overButtons = new WrapPanel();
        overButtons.Children.Add(MakeButton("位移 Back.Out", (_, _) => OverShootScalarBack(), "over.btn.back"));
        overButtons.Children.Add(MakeButton("位移 Elastic.Out", (_, _) => OverShootScalarElastic(), "over.btn.elastic"));
        overButtons.Children.Add(MakeButton("颜色过冲", (_, _) => OverShootColor(), "over.btn.color"));
        overButtons.Children.Add(MakeButton("尺寸过冲", (_, _) => OverShootSize(), "over.btn.size"));
        overButtons.Children.Add(MakeButton("渐变过冲", (_, _) => OverShootGradient(), "over.btn.brush"));
        overButtons.Children.Add(MakeButton("重置过冲", (_, _) => ResetOverShoot(), "over.btn.reset"));
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
        SetAutomationToken(_readout, "Readout", "over.readout");
        Grid.SetRow(_readout, 9);
        grid.Children.Add(_readout);

        // 读数的机器可读孪生体：同一支定时器、同一批值，但用固定的 key=value 载荷而不是散文。
        _overState = new TextBlock
        {
            Margin = new Thickness(8, 0, 8, 2),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Text = "v=1;seq=0",
        };
        SetAutomationToken(_overState, "OverState", "over.state");
        Grid.SetRow(_overState, 10);
        grid.Children.Add(_overState);

        // 演示台插在把手条上方：把手是"验"，台子是"看"，点一次两件事一起发生。
        var bench = _bench.Build(SamplerProbe.SamplerNames);
        Grid.SetRow(bench, 11);
        grid.Children.Add(bench);

        // 采样器一致性把手：一条采样器一个按钮，点一下跑那一条、把结果写进载荷。把手与探针表同源，
        // 所以加一条采样器只需要改 SamplerProbe 一处。
        // 扫描必须落在 UI 线程上 —— 它要构造画刷、变换这类有线程亲和性的对象，而点击处理函数就在 UI 线程。
        var samplerButtons = new WrapPanel();
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            var handle = MakeButton(sampler, (_, _) => RunSamplerProbe(sampler), $"over.sampler.{sampler}");
            handle.Tag = sampler;
            handle.Width = 132;
            handle.Height = 30;
            handle.FontSize = 11;
            samplerButtons.Children.Add(handle);
        }
        Grid.SetRow(samplerButtons, 12);
        grid.Children.Add(samplerButtons);

        // 采样器把手的载荷：最后一次点击跑出来的那一帧序列，外加证明点击落地的序列号。载荷格式与 over.state 相同，
        // 验收侧不必再多认一种。
        _overConf = new TextBlock
        {
            Margin = new Thickness(8, 0, 8, 2),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Text = "v=1;seq=0;n=0;",
        };
        SetAutomationToken(_overConf, "OverConf", "over.conf");
        Grid.SetRow(_overConf, 13);
        grid.Children.Add(_overConf);

        Content = grid;

        // 读数用定时器采样目标属性，不用 effect 的事件：流水线每段都会 Clone() effect，
        // 订阅在原始 effect 上的处理函数不会触发。
        var readoutTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        readoutTimer.Tick += (_, _) => UpdateReadout();
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

    /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
    private long _probeSequence;

    private readonly SamplerBench _bench = new();

    // 只留一支演出用的定时器：连点两个把手时，后一次要能叫停前一次，否则两条采样器会同时往各自的格子里写。
    private DispatcherTimer? _benchTimer;

    private void RunSamplerProbe(string samplerName)
    {
        // 采样器写在**这一格的在屏控件**上、载荷也从它读回，所以下面两件事是同一件事的两种读法。
        var subject = _bench.SubjectFor(samplerName);

        // 验：五个固定的缓动时间各跑一帧，写进载荷。
        _overConf.Text = SamplerProbe.Run(subject, samplerName, ++_probeSequence);

        // 看：按 Back.Out 把这条采样器跑一遍，它会越过端点再落回来 —— 肉眼看得到的就是这个。
        PlaySampler(samplerName, subject);
    }

    /// <summary>
    /// 演出：把缓动进度从 0 走到 1，每一拍把该采样器在当前缓动时间上写出的值画到它那一格上。
    /// </summary>
    private void PlaySampler(string sampler, SamplerSubject subject)
    {
        _benchTimer?.Stop();

        var duration = TimeSpan.FromMilliseconds(800);
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _benchTimer = timer;

        timer.Tick += (_, _) =>
        {
            var progress = Math.Min(1d, clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            SamplerProbe.Frame(subject, sampler, Eases.Back.Out.Ease(progress));

            if (progress >= 1d) timer.Stop();
        };

        timer.Start();
    }

    private static void AddRow(Grid grid, GridLength height)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = height });
    }

    // 与过冲条同尺寸的方块（不是被拉长的条），与参考 demo 一致。
    private static Rectangle MakeRect(Brush fill)
        => new()
        {
            Width = 80,
            Height = 60,
            Fill = fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private static Rectangle MakeRect(Color fill) => MakeRect(new SolidColorBrush(fill));

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

    private Button MakeButton(string text, RoutedEventHandler onClick, string? automationId = null)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, Foreground = new SolidColorBrush(Colors.White) },
            Margin = new Thickness(4),
        };
        button.Click += onClick;
        if (automationId is not null) SetAutomationToken(button, automationId, automationId);
        return button;
    }

    // 验收套件的把手：AutomationId 附着属性是语言无关的稳定 token，Name 则走 Jalium 的
    // WPF 式 Name→AutomationId 回退，两条路都通。两者都不碰人类可见的文字与无障碍 Name。
    private static void SetAutomationToken(FrameworkElement element, string name, string automationId)
    {
        element.Name = name;
        AutomationProperties.SetAutomationId(element, automationId);
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
        _rec2.Fill = CreateBs1Brush();
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
            .Property(r => r.Fill, CreateShiftedBs1())
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(3),
                IsAutoReverse = true,
                FPS = 144,
                Ease = Eases.Circ.InOut,
                LoopTime = 2,
            })
            .AwaitThen(TimeSpan.FromSeconds(5))
            .Property(r => r.Fill, CreateBs1Brush())
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

    // 时长同时喂给 effect 与载荷：载荷靠它推出 done，两处若各写一份就会漂移。
    private const int BackDurationMs = 900;
    private const int ElasticDurationMs = 1100;

    private static readonly Transition<Rectangle> OverScalarBack =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, ShiftTarget)
            .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

    private static readonly Transition<Rectangle> OverScalarElastic =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, ShiftTarget)
            .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

    // 目标色每个通道都留有余量：有余量时共享进度允许颜色整体过冲，直到第一个触到 255 的通道才停下，
    // 所以变的是亮度而不是色相。若逐通道各自截断，绿蓝会跑过红，色相就被挪走了。
    private static readonly Transition<Rectangle> OverColor =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0xD0)))
            .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

    // Width 在 Jalium 上是普通 double，走 DoubleSampler，没有 0 界：直接外推（Elastic 峰值处 80+140*1.37≈272）。
    // 起点 80 < 终点 220，而 EaseOutElastic 的最小值仍 >0，所以不会出现负宽度，也就不会触发布局对负尺寸的拒绝。
    private static readonly Transition<Rectangle> OverSize =
        Transition<Rectangle>.Create()
            .Property(r => r.Width, WidthTarget)
            .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

    // 非纯色 Fill 走的是混合刷路径而不是纯色路径：交叉淡出的系数是一个比例，因此在两端饱和而不是越界。
    private static readonly Transition<Rectangle> OverBrush =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, CreateShiftedBs1())
            .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Back.Out });

    // 每次运行前先把自己的目标同步写回起始值，而不是动画回去：Prepare 会把目标的当前值当作动画起点，
    // 少了这一步，第二次点击——或共用同一元素的兄弟按钮——就会从目标动到目标，看起来什么都没发生。
    // 只重置这条自己拥有的元素：动别的元素会取消那边的运行，并排对比就没了。
    private void OverShootScalarBack() => RunScalar(OverScalarBack, "back", BackDurationMs);
    private void OverShootScalarElastic() => RunScalar(OverScalarElastic, "elastic", ElasticDurationMs);

    private void RunScalar(Transition<Rectangle> animation, string scenario, int durationMs)
    {
        Transition.Exit(_over0, IncludeMutual: true, IncludeNoMutual: true);
        ((TranslateTransform)_over0.RenderTransform!).X = 0;
        BeginScenario(scenario, durationMs, targetIndex: 0);
        animation.Execute(_over0);
    }

    private void OverShootColor()
    {
        Transition.Exit(_over1, IncludeMutual: true, IncludeNoMutual: true);
        _over1.Fill = new SolidColorBrush(OverColorStart);
        BeginScenario("color", BackDurationMs, targetIndex: 1);
        OverColor.Execute(_over1);
    }

    private void OverShootSize()
    {
        Transition.Exit(_over2, IncludeMutual: true, IncludeNoMutual: true);
        _over2.Width = OverWidthStart;
        BeginScenario("size", ElasticDurationMs, targetIndex: 2);
        OverSize.Execute(_over2);
    }

    private void OverShootGradient()
    {
        Transition.Exit(_over3, IncludeMutual: true, IncludeNoMutual: true);
        _over3.Fill = CreateBs1Brush();
        BeginScenario("brush", ElasticDurationMs, targetIndex: 3);
        OverBrush.Execute(_over3);
    }

    // ── 验收观测面 ──────────────────────────────────────────────────────────
    //
    // 人类读数之外再写一份机器可读的载荷：同一支定时器、同一批值，但用固定的 key=value 而不是散文，
    // 测试就不必去解析一份随时可能被重新排版的版式。载荷只报告"每个目标当前/峰值是多少"，至于哪个场景
    // 动哪个目标、起止与时长，由测试侧的 manifest 声明 —— 观测与语义各自只有一处来源。
    // ───────────────────────────────────────────────────────────────────────

    private readonly Stopwatch _scenarioClock = new();
    private readonly double[] _targetPeaks = new double[4];
    private string _scenario = "none";
    private int _scenarioDurationMs;
    private long _sequence;

    // 记下本次场景，并把该目标此前的峰值清零——每次点击都必须从干净的观测开始。
    private void BeginScenario(string scenario, int durationMs, int targetIndex)
    {
        _scenario = scenario;
        _scenarioDurationMs = durationMs;
        _targetPeaks[targetIndex] = double.NegativeInfinity;
        _scenarioClock.Restart();
    }

    private string BuildState()
    {
        _sequence++;

        // 只有标量目标记录峰值。峰值存在的意义是"刀刃型峰"——采样落在尖峰两侧就会低估它；颜色的过冲是一段
        // 形状而不是一个尖峰，报当前值就够，测试轮询取最大即可。
        var x = (_over0.RenderTransform as TranslateTransform)?.X ?? double.NaN;
        var width = _over2.Width;
        _targetPeaks[0] = Math.Max(_targetPeaks[0], x);
        _targetPeaks[2] = Math.Max(_targetPeaks[2], width);

        // done 由时长推出，而不是订阅 effect.Completed：流水线每段克隆 effect，订阅在原件上的处理函数不触发。
        // 它是必需的，不能靠"值等于目标"判断结束 —— 两条曲线都会中途再次穿过目标（Elastic 在 1.1s 内穿越七次）。
        var elapsed = _scenarioClock.ElapsedMilliseconds;
        var done = _scenario != "none" && elapsed >= _scenarioDurationMs + 50 ? 1 : 0;

        return $"v=1;seq={_sequence};scen={_scenario};t={elapsed};done={done};"
             + $"t0.cur={x:F3};t0.peak={Peak(0)};"
             + $"t1.cur={Describe(_over1.Fill)};"
             + $"t2.cur={width:F3};t2.peak={Peak(2)};"
             + $"t3.cur={Describe(_over3.Fill)};"
             + RecState("r0", _rec0) + RecState("r1", _rec1) + RecState("r2", _rec2)
             + $"nomutual={NoMutualCount()};";
    }

    /// <summary>
    /// 加载模式那一排三块目标的状态：动画真正写的那几个属性，读出来报给测试。
    /// </summary>
    /// <remarks>
    /// 与过冲条同一支定时器、同一次采样，所以两者不可能不一致。报的是"动的是什么"而不是"应该动到哪" ——
    /// 那三个动画各自带 auto-reverse 与 loop，终点要靠复算库的语义才知道，测试不去复算它。
    /// </remarks>
    private static string RecState(string prefix, Rectangle target)
        => $"{prefix}.x={TranslateX(target):F3};"
         + $"{prefix}.fill={Describe(target.Fill)};";

    /// <summary>动画真正写的那个位移，无论它被写成单个 TranslateTransform 还是组合进 TransformGroup。</summary>
    private static double TranslateX(Rectangle target) => target.RenderTransform switch
    {
        TranslateTransform translate => translate.X,
        TransformGroup group => group.Children.OfType<TranslateTransform>().FirstOrDefault()?.X ?? 0d,
        _ => 0d,
    };

    /// <summary>
    /// 这个目标上还有几条**并发**（非互斥）动画在跑。
    /// </summary>
    /// <remarks>
    /// 这是唯一能把"互斥加载"和"并发加载"区分开的可观测量：互斥调度器按目标缓存、一辈子不释放，
    /// `TryGetMutualScheduler` 返回 true 只说明"这目标跑过互斥动画"；而非互斥那张表在每条动画结束时真的会清空。
    /// 要点是取**数组长度**而不是那个 bool —— 表项本身不随运行结束移除。
    /// </remarks>
    private int NoMutualCount()
    {
        var running = 0;

        foreach (var target in new Rectangle[] { _rec0, _rec1, _rec2 })
        {
            if (TransitionScheduler.TryGetNoMutualScheduler(target, out var schedulers))
            {
                running += schedulers.Length;
            }
        }

        return running;
    }

    private string Peak(int index)
        => double.IsNegativeInfinity(_targetPeaks[index]) ? "0" : _targetPeaks[index].ToString("F3");

    // 把刷子写成测试能读懂的形式：纯色给 #rrggbb，其余给类型名。
    private static string Describe(Brush? brush)
        => brush is SolidColorBrush solid ? $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
           : brush?.GetType().Name ?? "none";

    // 读数只取目标的真实属性，不缓存也不伪造。载荷与读数共用这一次采样，两者不可能互相矛盾。
    private void UpdateReadout()
    {
        var shift = (_over0.RenderTransform as TranslateTransform)?.X ?? double.NaN;
        _readout.Text =
            $"位移 X   目标 {ShiftTarget,6:F1}   当前 {shift,7:F1}"
            + $"     |     宽度   目标 {WidthTarget,6:F1}   当前 {_over2.Width,7:F1}";
        _overState.Text = BuildState();
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
