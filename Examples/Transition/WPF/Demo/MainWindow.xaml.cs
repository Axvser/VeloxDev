using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VeloxDev.TransitionSystem;

namespace Demo;

public partial class MainWindow : Window
{
    private bool _resetInitialized;

    public MainWindow()
    {
        InitializeComponent();

        Rec0.RenderTransform = new TranslateTransform();

        // The overshoot strip drives RenderTransform.X in place, so its transform is created once here.
        Over0.RenderTransform = new TranslateTransform();

        // Reset snapshots are taken only after the element is Loaded, avoiding information loss from
        // an initial state that has not yet been established.
        // Rec0's RenderTransform.X is modified in place, which would pollute the references held by
        // the snapshot, so Rec0 is reset with a new object; Rec1/Rec2 Fills are replaced wholesale
        // (not mutated), so the initial snapshots fully restore them.
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

            // Readout for the overshoot strip. Sampled on a timer rather than from the effect's events: the pipeline
            // clones the effect once per segment, so the handlers subscribed here are not the ones that fire.
            var readout = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            readout.Tick += (s, e) =>
            {
                Readout.Text =
                    $"位移 X   目标 {ShiftTarget,6:F1}   当前 {((TranslateTransform)Over0.RenderTransform).X,7:F1}"
                    + $"     |     宽度   目标 {WidthTarget,6:F1}   当前 {Over2.Width,7:F1}";
                OverState.Text = BuildState();
            };
            readout.Start();
        };
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
        var x = ((TranslateTransform)Over0.RenderTransform).X;
        var width = Over2.Width;
        _targetPeaks[0] = Math.Max(_targetPeaks[0], x);
        _targetPeaks[2] = Math.Max(_targetPeaks[2], width);

        // done 由时长推出，而不是订阅 effect.Completed：流水线每段克隆 effect，订阅在原件上的处理函数不触发。
        // 它是必需的，不能靠"值等于目标"判断结束 —— 两条曲线都会中途再次穿过目标（Elastic 在 1.1s 内穿越七次）。
        var elapsed = _scenarioClock.ElapsedMilliseconds;
        var done = _scenario != "none" && elapsed >= _scenarioDurationMs + 50 ? 1 : 0;

        return $"v=1;seq={_sequence};scen={_scenario};t={elapsed};done={done};"
             + $"t0.cur={x:F3};t0.peak={Peak(0)};"
             + $"t1.cur={Describe(Over1.Fill)};"
             + $"t2.cur={width:F3};t2.peak={Peak(2)};"
             + $"t3.cur={Describe(Over3.Fill)};";
    }

    private string Peak(int index)
        => double.IsNegativeInfinity(_targetPeaks[index]) ? "0" : _targetPeaks[index].ToString("F3");

    // 把刷子写成测试能读懂的形式：纯色给 #rrggbb，其余给类型名。
    private static string Describe(Brush? brush)
        => brush is SolidColorBrush solid ? $"#{solid.Color.R:x2}{solid.Color.G:x2}{solid.Color.B:x2}"
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
            .Property(r => r.Opacity, 1d)
            .Effect(TransitionEffects.Empty);
    }

    private static Transition<Rectangle> CreateResetRec1()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.Fill, new SolidColorBrush(Colors.Lime))
            .Property(r => r.Opacity, 1d)
            .Effect(TransitionEffects.Empty);
    }

    private static Transition<Rectangle> CreateResetRec2()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.Fill, CreateBs1Brush())
            .Property(r => r.Opacity, 1d)
            .Effect(TransitionEffects.Empty);
    }

    // The Bs1 resource of MainWindow.xaml (Yellow → Violet, 0,0 → 1,1), rebuilt in code because a resource is not
    // a value a transition path can point at. Keep the two in step: Rec2's reset animates Fill back to this brush,
    // so editing the XAML alone would leave the reset animating to a stale one.
    private static LinearGradientBrush CreateBs1Brush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
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
            .Property(r => r.Opacity, 0)
            .Property(r => ((TranslateTransform)r.RenderTransform).X, 800)
            .Property(r => r.Fill, new SolidColorBrush(Colors.Orange))
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
            });

    // Delayed animation: reverse rotation
    private static readonly Transition<Rectangle> Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(5))
            .Property(r => r.RenderTransform, [new RotateTransform(180)], RotationDirection.CounterClockWise)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
            });

    // Combined animation
    private static readonly Transition<Rectangle> Animation2 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform,
            [
                new TranslateTransform(200, 0),
                new ScaleTransform(1.3, 1.3)
            ])
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                FPS = 144,
                Ease = Eases.Circ.InOut,
                LoopTime = 2,
            })
            .AwaitThen(TimeSpan.FromSeconds(5)) // wait 5 seconds before starting the next animation
            .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                Ease = Eases.Sine.In
            });

    // -----------------------------------------------------------------------------------------------
    // Overshoot
    //
    // Every ease used above returns a value inside [0,1], so none of them can pass its target and come
    // back. Back peaks at 1.10 and Elastic at 1.37, and what an overshoot means is decided per sampler:
    // R/G/B move by one shared progress and stop at the first channel to reach its limit, width and
    // height do the same at zero, and a channel with a range of its own — alpha, opacity — keeps the
    // full eased time. The readout is what makes it observable: the number passes the target and
    // returns, which the eye alone cannot tell apart from a slower ease.
    // -----------------------------------------------------------------------------------------------

    private const double ShiftTarget = 300d;
    private const double WidthTarget = 220d;
    // 时长同时喂给 effect 与载荷：载荷靠它推出 done，两处若各写一份就会漂移。
    private const int BackDurationMs = 900;
    private const int ElasticDurationMs = 1100;

    private static readonly Transition<Rectangle> OverScalarBack =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform).X, ShiftTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

    private static readonly Transition<Rectangle> OverScalarElastic =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform).X, ShiftTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

    // The target is deliberately mid-range on every channel: with headroom left, the shared progress lets the
    // colour overshoot and still stops at the first channel to reach 255, so what moves is the brightness and not
    // the hue. A per-channel clamp would let green and blue run past red and shift it.
    private static readonly Transition<Rectangle> OverColor =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0xD0)))
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

    // Width is a double, so it extrapolates without a bound — nothing here stops it going negative, and a Width
    // setter rejects that. This scenario therefore only grows (80 to 220) and the elastic curve never dips below
    // its start, so the invalid range is not reachable. A *shrinking* width under an overshooting ease would reach
    // it: a double carries no range for the sampler to respect. Animating a Size-typed property instead is what
    // gets the width/height bound.
    private static readonly Transition<Rectangle> OverSize =
        Transition<Rectangle>.Create()
            .Property(r => r.Width, WidthTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

    // A non-solid Fill goes through the blended-brush path rather than the solid one: a cross-fade, whose factor is
    // a fraction and therefore stops at either end instead of overshooting.
    private static readonly Transition<Rectangle> OverBrush =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, CreateShiftedBs1())
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Back.Out });

    private static LinearGradientBrush CreateShiftedBs1()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Colors.Cyan, 0.25),
                new GradientStop(Colors.Orange, 0.75)
            }
        };
    }

    // Each run first puts its own target back to the starting value, synchronously rather than by animating back.
    // Prepare reads the target's live value as the animation's start, so without this a second click — or the
    // sibling button that shares the element — would animate from the target to the target and appear to do
    // nothing. Only this element is reset, so a run on another one keeps going and the strip stays comparable.
    private void OverShootScalarBack(object sender, RoutedEventArgs e) => RunScalar(OverScalarBack, "back", BackDurationMs);
    private void OverShootScalarElastic(object sender, RoutedEventArgs e) => RunScalar(OverScalarElastic, "elastic", ElasticDurationMs);

    private void RunScalar(Transition<Rectangle> animation, string scenario, int durationMs)
    {
        Transition.Exit(Over0, IncludeMutual: true, IncludeNoMutual: true);
        ((TranslateTransform)Over0.RenderTransform).X = 0;
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
        Over2.Width = 80;
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

    // 颜色场景的起点，即过冲条在 XAML 里声明的那一支。
    private static readonly Color OverColorStart = Color.FromRgb(0x3A, 0x6E, 0xA5);

    private void ResetOverShoot()
    {
        foreach (var target in new[] { Over0, Over1, Over2, Over3 })
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

        // Written directly, like the reset above: these are the values the XAML declares.
        Over0.RenderTransform = new TranslateTransform();
        Over1.Fill = new SolidColorBrush(OverColorStart);
        Over2.Width = 80;
        Over3.Fill = CreateBs1Brush();
    }
}