using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace Demo.Views;

public partial class MainWindow : Window
{
    private bool _resetInitialized;

    public MainWindow()
    {
        InitializeComponent();

        Rec0.RenderTransform = new TranslateTransform();

        // 过冲条就地驱动 RenderTransform.X，所以变换只在这里创建一次。
        Over0.RenderTransform = new TranslateTransform();

        // The reset is expressed as explicit property paths, built only after the window is Opened so it
        // describes an established initial state.
        Loaded += (s, e) =>
        {
            if (_resetInitialized) return;
            _resetInitialized = true;

            // Explicitly initialize to a definite state first, so the reset paths below describe a known state.
            Rec0.RenderTransform = new TranslateTransform();
            Rec1.RenderTransform = null;
            Rec2.RenderTransform = null;

            btnReset.Click += (s, e) =>
            {
                Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                // RenderTransform is cleared by direct assignment rather than through the builder: the adapter's
                // Transform overload takes a collection, and an empty one builds an empty TransformGroup, not null.
                Rec1.RenderTransform = null;
                Rec2.RenderTransform = null;

                // Apply the reset synchronously: bypasses the async Execute pipeline
                // (unreliable for Transform reset on some platforms), and Rec0 gets a fresh object
                // each time so its references are not polluted by in-place animation edits.
                ApplyReset(CreateResetRec0(), Rec0);
                ApplyReset(CreateResetRec1(), Rec1);
                ApplyReset(CreateResetRec2(), Rec2);

                ResetOverShoot();
            };

            // 过冲条的读数：用定时器采样目标属性，而不是订阅 effect 的事件——流水线每段都会 Clone() effect，
            // 在这里订阅的处理函数不是真正触发的那个。
            var readout = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            readout.Tick += (s, e) =>
            {
                Readout.Text =
                    $"位移 X   目标 {ShiftTarget,6:F1}   当前 {((TranslateTransform)Over0.RenderTransform!).X,7:F1}"
                    + $"     |     宽度   目标 {WidthTarget,6:F1}   当前 {Over2.Width,7:F1}";
                OverState.Text = BuildState();
            };
            readout.Start();

            // 采样器一致性把手：一条采样器一个按钮，点一下跑那一条、把结果写进载荷。把手与探针表同源，
            // 所以加一条采样器只需要改 SamplerProbe 一处。
            // 扫描必须落在 UI 线程上 —— 它要构造画刷、阴影、变换这类有线程亲和性的对象，而点击处理函数就在 UI 线程。
            foreach (var sampler in SamplerProbe.SamplerNames)
            {
                var handle = new Button
                {
                    Content = sampler,
                    Width = 132,
                    Height = 30,
                    Margin = new Thickness(2),
                    FontSize = 11,
                    Tag = sampler,
                };
                AutomationProperties.SetAutomationId(handle, $"over.sampler.{sampler}");
                handle.Click += RunSamplerProbe;
                SamplerButtons.Children.Add(handle);
            }

            // 演示台插在把手条上方：把手是"验"，台子是"看"，点一次两件事一起发生。
            if (SamplerButtons.Parent is Panel parent)
            {
                parent.Children.Insert(
                    parent.Children.IndexOf(SamplerButtons),
                    _bench.Build(SamplerProbe.SamplerNames));
            }
        };
    }

    /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
    private long _probeSequence;

    private readonly SamplerBench _bench = new();

    // 只留一支演出用的定时器：连点两个把手时，后一次要能叫停前一次，否则两条采样器会同时往各自的格子里写。
    private DispatcherTimer? _benchTimer;

    private void RunSamplerProbe(object? sender, RoutedEventArgs e)
    {
        var sampler = (string)((Button)sender!).Tag!;

        // 采样器写在**这一格的在屏控件**上、载荷也从它读回，所以下面两件事是同一件事的两种读法。
        var subject = _bench.SubjectFor(sampler);

        // 验：五个固定的缓动时间各跑一帧，写进载荷。
        OverConf.Text = SamplerProbe.Run(subject, sampler, ++_probeSequence);

        // 看：按 Back.Out 把这条采样器跑一遍，它会越过端点再落回来 —— 肉眼看得到的就是这个。
        PlaySampler(sampler, subject);
    }

    /// <summary>
    /// 演出：把缓动进度从 0 走到 1，每一拍把该采样器在当前缓动时间上写出的值写进它那一格。
    /// </summary>
    private void PlaySampler(string sampler, SamplerSubject subject)
    {
        _benchTimer?.Stop();

        var duration = TimeSpan.FromMilliseconds(800);
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _benchTimer = timer;

        timer.Tick += (s, e) =>
        {
            var progress = Math.Min(1d, clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            SamplerProbe.Frame(subject, sampler, Eases.Back.Out.Ease(progress));

            if (progress >= 1d) timer.Stop();
        };

        timer.Start();
    }

    // -----------------------------------------------------------------------------------------------
    // 验收观测面
    //
    // 人类读数之外再写一份机器可读的载荷：同一支定时器、同一批值，但用固定的 key=value 而不是散文，
    // 测试就不必去解析一份随时可能被重新排版的版式。载荷只报告"每个目标当前/峰值是多少"，至于哪个场景
    // 动哪个目标、起止与时长，由测试侧的 manifest 声明 —— 观测与语义各自只有一处来源。
    // -----------------------------------------------------------------------------------------------

    private readonly Stopwatch _scenarioClock = new();
    private readonly double[] _targetPeaks = new double[4];
    private string _scenario = "none";
    private int _scenarioDurationMs;
    private long _sequence;

    /// <summary>Records the scenario that is starting and clears that target's previous peak — every click has to begin from a clean observation.</summary>
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
        var x = ((TranslateTransform)Over0.RenderTransform!).X;
        var width = Over2.Width;
        _targetPeaks[0] = Math.Max(_targetPeaks[0], x);
        _targetPeaks[2] = Math.Max(_targetPeaks[2], width);

        // done 由时长推出，而不是订阅 effect.Completed：流水线每段克隆 effect，订阅在原件上的处理函数不触发。
        var elapsed = _scenarioClock.ElapsedMilliseconds;
        var done = _scenario != "none" && elapsed >= _scenarioDurationMs + 50 ? 1 : 0;

        return $"v=1;seq={_sequence};scen={_scenario};t={elapsed};done={done};"
             + $"t0.cur={x:F3};t0.peak={Peak(0)};"
             + $"t1.cur={Describe(Over1.Fill)};"
             + $"t2.cur={width:F3};t2.peak={Peak(2)};"
             + $"t3.cur={Describe(Over3.Fill)};"
             + RecState("r0", Rec0) + RecState("r1", Rec1) + RecState("r2", Rec2)
             + $"nomutual={NoMutualCount()};";
    }

    /// <summary>
    /// 加载模式那一排三块图形的状态：动画真正写的那几个属性，读出来报给测试。
    /// </summary>
    /// <remarks>
    /// 与过冲条同一支定时器、同一次采样，所以两者不可能不一致。报的是"动的是什么"而不是"应该动到哪" ——
    /// 那三条动画各自带 auto-reverse 与 loop，终点要靠复算库的语义才知道，测试不去复算它。
    /// </remarks>
    private static string RecState(string prefix, Rectangle target)
        => $"{prefix}.x={TranslateX(target):F3};"
         + $"{prefix}.fill={Describe(target.Fill)};"
         + $"{prefix}.opacity={target.Opacity:F3};";

    /// <summary>
    /// 动画真正写的那个位移，无论它被写成单个 TranslateTransform 还是组合进 TransformGroup。
    /// </summary>
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
    /// 这是唯一能把"互斥加载"和"并发加载"区分开的可观测量：互斥调度器是按目标缓存的一辈子不释放，
    /// `TryGetMutualScheduler` 返回 true 只说明"这目标跑过互斥动画"；而非互斥的那张表在每条动画结束时
    /// 真的会清空。要点是取**数组长度**而不是那个 bool —— 表项本身不随运行结束移除。
    /// </remarks>
    private int NoMutualCount()
    {
        var running = 0;

        foreach (var target in new Rectangle[] { Rec0, Rec1, Rec2 })
        {
            if (TransitionSchedulerCore.TryGetNoMutualScheduler(target, out var schedulers))
            {
                running += schedulers.Length;
            }
        }

        return running;
    }

    private string Peak(int index)
        => double.IsNegativeInfinity(_targetPeaks[index]) ? "0" : _targetPeaks[index].ToString("F3");

    /// <summary>Writes a brush in a form a test can read: #rrggbb for a solid colour, the type name otherwise.</summary>
    /// <remarks>
    /// Matches the interface, not SolidColorBrush: a Fill the XAML declares as a literal is an
    /// ImmutableSolidColorBrush, and matching the concrete class would report that type name instead of its colour.
    /// </remarks>
    private static string Describe(IBrush? brush)
        => brush is ISolidColorBrush solid ? $"#{solid.Color.R:x2}{solid.Color.G:x2}{solid.Color.B:x2}"
           : brush?.GetType().Name ?? "none";

    private void LoadMainThread(object sender, RoutedEventArgs e)
    {
        // Start directly on the main (UI) thread; CanMutualTask: true (default) — mutually
        // exclusive, the new animation interrupts the old one
        Animation0.Execute(Rec0);
        Animation1.Execute(Rec1);
        Animation2.Execute(Rec2);
    }

    private void LoadBackground(object sender, RoutedEventArgs e)
    {
        // Started from a non-UI thread; the framework automatically switches back to the UI thread
        // (thread marshaling derived from the target).
        _ = Task.Run(() =>
        {
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        });
    }

    private void LoadMainThreadNonMutual(object sender, RoutedEventArgs e)
    {
        // Main thread + CanMutualTask: false — run concurrently, neither cancels the other
        Animation0.Execute(Rec0, CanMutualTask: false);
        Animation1.Execute(Rec1, CanMutualTask: false);
        Animation2.Execute(Rec2, CanMutualTask: false);
    }

    private void LoadBackgroundNonMutual(object sender, RoutedEventArgs e)
    {
        // Non-UI thread + CanMutualTask: false — run concurrently
        _ = Task.Run(() =>
        {
            Animation0.Execute(Rec0, CanMutualTask: false);
            Animation1.Execute(Rec1, CanMutualTask: false);
            Animation2.Execute(Rec2, CanMutualTask: false);
        });
    }

    private void RepeatMutual(object sender, RoutedEventArgs e)
    {
        // Each click starts a mutually-exclusive animation on Rec0, and the new animation cancels
        // the previous one (tests scheduler gating and cancellation).
        _ = Task.Run(() => Animation0.Execute(Rec0));
    }

    private void ExitAll(object sender, RoutedEventArgs e)
    {
        // IncludeMutual   indicates whether to end animations configured with CanMutualTask: true
        // IncludeNoMutual indicates whether to end animations configured with CanMutualTask: false
        Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);
    }
}

public partial class MainWindow
{
    // Apply snapshot values synchronously (bypassing the async Execute pipeline so Transform/3D
    // resets are deterministic and reliable).
    // Projection and RenderTransform(Scale) are mutually exclusive on some platforms — clear
    // Projection first, then write the rest.
    private static void ApplyReset(Transition<Rectangle> snapshot, Rectangle target)
    {
        // Two passes: clear Projection first (releasing the mutual exclusion with
        // RenderTransform/Scale), then write everything else.
        // A single property conflict (framework constraint) does not abort the whole reset.
        foreach (var kvp in snapshot.GetState().Values)
            if (kvp.Key.Path.Contains("Projection"))
                try { kvp.Key.SetValue(target, kvp.Value); } catch { }
        foreach (var kvp in snapshot.GetState().Values)
            if (!kvp.Key.Path.Contains("Projection"))
                try { kvp.Key.SetValue(target, kvp.Value); } catch { }
    }

    // Rec0's RenderTransform.X / Fill are modified in place, so reset must use a new object
    private static Transition<Rectangle> CreateResetRec0()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform, [new TranslateTransform()])
            .Property(r => r.Fill, new SolidColorBrush(Colors.Cyan))
            .Effect(TransitionEffects.Empty);
    }

    private static Transition<Rectangle> CreateResetRec1()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.Fill, new SolidColorBrush(Colors.Lime))
            .Effect(TransitionEffects.Empty);
    }

    private static Transition<Rectangle> CreateResetRec2()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.Fill, CreateBs1Brush())
            .Effect(TransitionEffects.Empty);
    }

    // The Bs1 resource of MainWindow.axaml (Yellow → Violet, 0%,0% → 100%,100%), rebuilt in code because a resource
    // is not a value a transition path can point at. Keep the two in step: Rec2's reset animates Fill back to this
    // brush, so editing the XAML alone would leave the reset animating to a stale one.
    private static LinearGradientBrush CreateBs1Brush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Yellow, 0),
                new GradientStop(Colors.Violet, 1)
            }
        };
    }

    // Simple animation: demonstrates a nested property path, directly modifying RenderTransform.X
    private static readonly Transition<Rectangle> Animation0 =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, 400)
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(1, 0), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.DeepSkyBlue, 0),
                        new GradientStop(Colors.MediumPurple, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Sine.InOut
            });

    // Delayed animation: reverse rotation + movement + background gradient
    private static readonly Transition<Rectangle> Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(5))
            .Property(r => r.RenderTransform, [new TranslateTransform(-200, 0), new RotateTransform(180)], RotationDirection.ClockWise)
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(0, 1), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.OrangeRed, 0),
                        new GradientStop(Colors.Yellow, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(4),
                IsAutoReverse = true,
                FPS = 144,
                LoopTime = 4,
            });

    // Combined animation: reverse 3D rotation + scaling + switch to a new gradient background
    private static readonly Transition<Rectangle> Animation2 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform,
            [
                new TranslateTransform(200, 0),
                new Rotate3DTransform(180, 180, 0, 0, 0, 0, 0),
                new ScaleTransform(1.3, 1.3)
            ], RotationDirection.CounterClockWise)
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(1, 1), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.LightSeaGreen, 0),
                        new GradientStop(Colors.CadetBlue, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(3),
                IsAutoReverse = true,
                FPS = 144,
                Ease = Eases.Circ.InOut,
                LoopTime = 2,
            })
            .AwaitThen(TimeSpan.FromSeconds(5))
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(1, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(0, 1), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.Yellow, 0),
                        new GradientStop(Colors.Lime, 1)
                    }
                })
            .Effect((x) =>
            {
                x.Duration = TimeSpan.FromSeconds(4);
                x.FPS = 144;
                x.Ease = Eases.Sine.In;
            });

    // -----------------------------------------------------------------------------------------------
    // 过冲
    //
    // 上面每一条用到的缓动返回值都落在 [0,1] 内，所以没有一条能越过目标再回来。Back 峰值 1.10、
    // Elastic 峰值 1.37。过冲能不能真的看到，取决于目标属性落在哪个采样器上：
    //   - 纯数值（TranslateTransform.X、Width）自由外推，是真正的过冲；
    //   - 笔刷（Fill）在 t>=1 时直接写终值，只在端点饱和，看到的是"不过冲、不回绕"。
    // 读数是让过冲可见的东西：数字越过目标再回落，肉眼无法把它和一条更慢的缓动区分开。
    // -----------------------------------------------------------------------------------------------

    private const double ShiftTarget = 300d;
    private const double WidthTarget = 220d;
    private const double WidthStart = 80d;
    // 时长同时喂给 effect 与载荷：载荷靠它推出 done，两处若各写一份就会漂移。
    private const int BackDurationMs = 900;
    private const int ElasticDurationMs = 1100;

    // 颜色场景的起始色：过冲条在 XAML 里声明的那个填充色。起始色只有这一份来源，逐场景重置与整条重置都读它。
    private static readonly Color OverColorStart = Color.FromRgb(0x3A, 0x6E, 0xA5);

    private static readonly Transition<Rectangle> OverScalarBack =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, ShiftTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

    private static readonly Transition<Rectangle> OverScalarElastic =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, ShiftTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

    // 目标色每个通道都留有余量：R/G/B 共用一个归一化进度，任何一个通道触边都会把整组拉住，
    // 所以颜色既不会回绕，也不会出现逐通道钳制造成的偏色。
    private static readonly Transition<Rectangle> OverColor =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0xD0)))
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

    // Width 是 double，采样器不给它任何边界：这里没有任何东西阻止它变成负数，而 Width 的 setter 会拒绝
    // 负值。所以这个场景只做放大（80 → 220），弹性曲线也不会跌破起点，非法区间不可达。要让宽高拿到边界，
    // 得去动画一个 Size 类型的属性。
    private static readonly Transition<Rectangle> OverSize =
        Transition<Rectangle>.Create()
            .Property(r => r.Width, WidthTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

    // 非纯色 Fill 走的是混合笔刷路径而不是纯色路径：交叉淡化系数在两端饱和，越界的那段不会写出去。
    private static readonly Transition<Rectangle> OverBrush =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, CreateShiftedBs1())
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Back.Out });

    private static LinearGradientBrush CreateShiftedBs1()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Cyan, 0.25),
                new GradientStop(Colors.Orange, 0.75)
            }
        };
    }

    // 每次运行都先把自己那个目标同步地放回起始值，而不是动画回去。Prepare 以目标当前值作为动画起点，
    // 少了这一步，第二次点击——或者共用该元素的兄弟按钮——就会从目标动到目标，看起来什么都没发生。
    // 只重置这一个元素：别的元素上的运行继续跑，过冲条才保持可对比。
    private void OverShootScalarBack(object sender, RoutedEventArgs e) => RunScalar(OverScalarBack, "back", BackDurationMs);
    private void OverShootScalarElastic(object sender, RoutedEventArgs e) => RunScalar(OverScalarElastic, "elastic", ElasticDurationMs);

    private void RunScalar(Transition<Rectangle> animation, string scenario, int durationMs)
    {
        Transition.Exit(Over0, IncludeMutual: true, IncludeNoMutual: true);
        ((TranslateTransform)Over0.RenderTransform!).X = 0;
        BeginScenario(scenario, durationMs, targetIndex: 0);
        animation.Execute(Over0);
    }

    private void OverShootColor(object sender, RoutedEventArgs e)
    {
        Transition.Exit(Over1, IncludeMutual: true, IncludeNoMutual: true);
        Over1.Fill = new SolidColorBrush(OverColorStart);
        BeginScenario("color", BackDurationMs, targetIndex: 1);
        OverColor.Execute(Over1);
    }

    private void OverShootSize(object sender, RoutedEventArgs e)
    {
        Transition.Exit(Over2, IncludeMutual: true, IncludeNoMutual: true);
        Over2.Width = WidthStart;
        BeginScenario("size", ElasticDurationMs, targetIndex: 2);
        OverSize.Execute(Over2);
    }

    private void OverShootGradient(object sender, RoutedEventArgs e)
    {
        Transition.Exit(Over3, IncludeMutual: true, IncludeNoMutual: true);
        Over3.Fill = CreateBs1Brush();
        BeginScenario("brush", ElasticDurationMs, targetIndex: 3);
        OverBrush.Execute(Over3);
    }

    private void OverShootReset(object sender, RoutedEventArgs e) => ResetOverShoot();

    private void ResetOverShoot()
    {
        foreach (var target in new[] { Over0, Over1, Over2, Over3 })
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

        // 和上面的重置一样直接写回：这里就是 XAML 里声明的值。
        Over0.RenderTransform = new TranslateTransform();
        Over1.Fill = new SolidColorBrush(OverColorStart);
        Over2.Width = WidthStart;
        Over3.Fill = CreateBs1Brush();
    }
}