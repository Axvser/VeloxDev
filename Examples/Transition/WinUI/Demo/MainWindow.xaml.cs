using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using Windows.Foundation;

namespace Demo
{
    public sealed partial class MainWindow : Window
    {
        private bool _resetInitialized;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _readoutTimer;

        public MainWindow()
        {
            InitializeComponent();

            Rec0.RenderTransform = CreateRec0Transform();
            Rec0.Fill = CreateRec0Brush();

            // The overshoot strip drives RenderTransform.X in place, so its transform is created once here.
            Over0.RenderTransform = new TranslateTransform();

            // The animation targets a DependencyObject; the library marshals directly to its owning
            // UI thread via its DispatcherQueue, so no capture is needed even when the animation is
            // first started from a background thread (Task.Run) below.

            // The core concept of VeloxDev animations is "everything is state": a transition is a set of
            // explicitly specified property paths, built with Transition<T>.Create().Property(...).

            // The reset is wired only after the window is Activated, so it describes an established initial state.
            // Rec0's properties (RenderTransform.X, Fill.StartPoint/EndPoint) are modified in place, so Rec0 is
            // reset with a new object.
            ((FrameworkElement)Content).Loaded += (s, e) =>
            {
                if (_resetInitialized) return;
                _resetInitialized = true;

                // Explicitly initialize to a definite state first, so the reset paths below describe a known state.
                Rec0.RenderTransform = CreateRec0Transform();
                Rec0.Fill = CreateRec0Brush();
                Rec0.Projection = null;
                Rec1.RenderTransform = null;
                Rec1.Projection = null;
                Rec2.RenderTransform = null;
                Rec2.Projection = null;

                btnReset.Click += (s, e) =>
                {
                    Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                    Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                    Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                    // Explicitly clear Projection / RenderTransform first to release WinUI's mutual
                    // exclusion constraint (Scale and Projection cannot coexist), otherwise writing
                    // RenderTransform(Scale) while the element still has a Projection throws
                    // UnauthorizedAccessException.
                    Rec0.Projection = null; Rec0.RenderTransform = null;
                    Rec1.Projection = null; Rec1.RenderTransform = null;
                    Rec2.Projection = null; Rec2.RenderTransform = null;

                    // Apply the reset synchronously: bypasses the async Execute pipeline
                    // (unreliable for Transform reset on some platforms), and Rec0 gets a fresh object
                    // each time so its references are not polluted by in-place animation edits.
                    ApplyReset(CreateRec0Reset(), Rec0);
                    ApplyReset(CreateRec1Reset(), Rec1);
                    ApplyReset(CreateRec2Reset(), Rec2);

                    ResetOverShoot();
                };

                // Readout for the overshoot strip. Sampled on a timer rather than from the effect's events: the
                // pipeline clones the effect once per segment, so the handlers subscribed here are not the ones
                // that fire.
                // 定时器必须存进字段：只放在局部变量里，C#/WinRT 的 CCW 会被 GC 回收，Tick 随之停掉，
                // 观测面在读了几十拍之后就不再前进（验收套件会把这看成"功能坏了"）。
                _readoutTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()!.CreateTimer();
                _readoutTimer.Interval = TimeSpan.FromMilliseconds(40);
                _readoutTimer.Tick += (s, e) => UpdateReadout();
                _readoutTimer.Start();

                // 采样器一致性把手：一条采样器一个按钮，点一下只跑那一条、把结果写进载荷。把手与探针表同源，
                // 所以加一条采样器只需要改 SamplerProbe 一处。
                // 按钮在 UI 线程上建，它的处理函数也在 UI 线程上跑 —— 扫描要构造画刷、投影、变换这类有线程
                // 亲和性的对象，正是这一点要求。
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
                SamplerBenchHost.Children.Add(_bench.Build(SamplerProbe.SamplerNames));
            };
        }

        /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
        private long _probeSequence;

        private readonly SamplerBench _bench = new();

        // 只留一支演出用的定时器：连点两个把手时，后一次要能叫停前一次，否则两条采样器会同时往各自的格子里写。
        // 定时器还必须存进字段：只放在局部变量里，C#/WinRT 的 CCW 会被 GC 回收，Tick 随之停掉，演出半途而废。
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _benchTimer;

        private void RunSamplerProbe(object sender, RoutedEventArgs e)
        {
            var sampler = (string)((Button)sender).Tag;

            // 采样器写在**这一格的在屏控件**上、载荷也从它读回，所以下面两件事是同一件事的两种读法。
            var subject = _bench.SubjectFor(sampler);

            // 验：五个固定的缓动时间各跑一帧，写进载荷。
            OverConf.Text = SamplerProbe.Run(subject, sampler, ++_probeSequence);

            // 看：按 Back.Out 把这条采样器跑一遍，它会越过端点再落回来 —— 肉眼看得到的就是这个。
            PlaySampler(sampler, subject);
        }

        /// <summary>
        /// 演出：把缓动进度从 0 走到 1，每一拍把该采样器在当前缓动时间上写出的值画到它那一格上。
        /// </summary>
        private void PlaySampler(string sampler, SamplerSubject subject)
        {
            _benchTimer?.Stop();

            var duration = TimeSpan.FromMilliseconds(800);
            var clock = Stopwatch.StartNew();
            var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()!.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            _benchTimer = timer;

            timer.Tick += (s, e) =>
            {
                var progress = Math.Min(1d, clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
                SamplerProbe.Frame(subject, sampler, Eases.Back.Out.Ease(progress));

                if (progress >= 1d) timer.Stop();
            };

            timer.Start();
        }

        private void LoadMainThread(object sender, RoutedEventArgs e)
        {
            // Start directly on the main (UI) thread; mutual exclusion (CanMutualTask: true by default)
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        }

        private void LoadAnimations(object sender, RoutedEventArgs e)
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

        private void ExitAnimations(object sender, RoutedEventArgs e)
        {
            // End animations held by the object
            // IncludeMutual   indicates whether to end animations configured with CanMutualTask: true
            // IncludeNoMutual indicates whether to end animations configured with CanMutualTask: false

            Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

            // Of course, you can also find an animation's Scheduler via the methods provided by the core library
            // The Scheduler object has Execute() and Exit() capabilities

            //if (TransitionSchedulerCore.TryGetMutualScheduler(Rec0, out var MutualScheduler) &&
            //   TransitionSchedulerCore.TryGetNoMutualScheduler(Rec0, out var noMutualSchedulers))
            //{
            //    ITransitionSchedulerCore[] schedulers = [MutualScheduler!, .. noMutualSchedulers];
            //    foreach (var scheduler in schedulers)
            //    {
            //        scheduler.Exit();
            //    }
            //}
        }

        private void LoadAnimationsNonMutual(object sender, RoutedEventArgs e)
        {
            // CanMutualTask: false — the three animations run concurrently without interference and
            // are not cancelled by one another
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0, CanMutualTask: false);
                Animation1.Execute(Rec1, CanMutualTask: false);
                Animation2.Execute(Rec2, CanMutualTask: false);
            });
        }

        private void LoadRepeatedMutual(object sender, RoutedEventArgs e)
        {
            // Each click starts a mutually-exclusive animation on Rec0: the new animation cancels the
            // previous one (tests scheduler gating and cancellation).
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0); // CanMutualTask: true (default)
            });
        }
    }

    public sealed partial class MainWindow
    {
        // ⚠ Creating Transition<> in WinUI requires special care:
        //
        // A static Transition<> field used on a non-UI thread may throw exceptions such as TypeInitialization
        // If you want it to be static, make sure the field is instantiated on the UI thread

        // Simple animation: demonstrates a nested property path, directly modifying RenderTransform.X,
        // together with gradient changes.
        private readonly Transition<Rectangle> Animation0 =
            Transition<Rectangle>.Create()
                .Property(r => ((TranslateTransform)r.RenderTransform).X, 400)
                .Property(r => ((LinearGradientBrush)r.Fill).StartPoint, new Point(0, 1))
                .Property(r => ((LinearGradientBrush)r.Fill).EndPoint, new Point(1, 1))
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(1),
                    IsAutoReverse = true,
                    LoopTime = 2
                });

        private static TranslateTransform CreateRec0Transform()
        {
            return new TranslateTransform() { X = 0, Y = 0 };
        }

        private static LinearGradientBrush CreateRec0Brush()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Cyan, Offset = 0 },
                    new GradientStop() { Color = Colors.Yellow, Offset = 1 },
                ]
            };
        }

        private static Transition<Rectangle> CreateRec0Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.RenderTransform, [CreateRec0Transform()])
                .Property(r => r.Fill, CreateRec0Brush())
                .Effect(TransitionEffects.Empty);
        }

        private static Transition<Rectangle> CreateRec1Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.Fill, new SolidColorBrush(Colors.Lime))
                .Effect(TransitionEffects.Empty);
        }

        private static Transition<Rectangle> CreateRec2Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.Fill, CreateBs1Brush())
                .Effect(TransitionEffects.Empty);
        }

        // Rec2's brush as declared inline in MainWindow.xaml (Yellow → Violet, 0,0 → 1,1), rebuilt in code. Keep the
        // two in step: Rec2's reset animates Fill back to this brush, so editing the XAML alone would leave the
        // reset animating to a stale one.
        private static LinearGradientBrush CreateBs1Brush()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Yellow, Offset = 0 },
                    new GradientStop() { Color = Colors.Violet, Offset = 1 },
                ]
            };
        }

        // Apply snapshot values synchronously (bypassing the async Execute pipeline so Transform/3D
        // resets are deterministic and reliable).
        // WinUI constraint: Projection and RenderTransform(Scale) are mutually exclusive — clear
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

        // Delayed animation: reverse rotation
        private readonly Transition<Rectangle> Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(3))
                .Property(r => r.RenderTransform, [new RotateTransform() { Angle = 180 }], RotationDirection.CounterClockWise)
                .Property(r => r.Fill, new LinearGradientBrush()
                {
                    GradientStops =
                    [
                        new GradientStop(){Color = Colors.Cyan,Offset=0},
                        new GradientStop(){Color= Colors.Red,Offset=0},
                    ]
                })
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2
                });

        // Combined animation: reverse projection rotation + color change
        private readonly Transition<Rectangle> Animation2 =
            Transition<Rectangle>.Create()
                .Property(r => r.Projection,
                    new PlaneProjection()
                    {
                        RotationX = 180,
                        RotationY = 180,
                        CenterOfRotationX = 0.5,
                        CenterOfRotationY = 0.5
                    }, RotationDirection.CounterClockWise)
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
        // 过冲
        //
        // 上面用到的缓动全都落在 [0,1] 内，没有一条能越过目标再回来。Back 峰值 1.10、Elastic 峰值 1.37，
        // 而"过冲"到底意味着什么由各采样器自己决定：R/G/B 共用一个进度、在第一个触限的通道处停住，
        // 宽/高同理在边界处停住，而自带值域的单通道（alpha、opacity）保留完整的缓动时间。
        // 读数是过冲能被观察到的关键：数字越过目标再回来，肉眼分辨不出这和一个更慢的缓动。
        // -----------------------------------------------------------------------------------------------

        private const double ShiftTarget = 300d;
        private const double WidthTarget = 220d;
        private const double BarWidth = 80d;

        // 时长同时喂给 effect 与载荷：载荷靠它推出 done，两处若各写一份就会漂移。
        private const int BackDurationMs = 900;
        private const int ElasticDurationMs = 1100;

        // 初始色与目标色都留在中段，各通道都有余量：共享进度让颜色能过冲，又会在第一个触限的通道上停住，
        // 于是变的是亮度而不是色相。注意 WinUI 的刷子采样器走预乘 alpha 混合，颜色的过冲进度取自
        // 停靠色本身而不是预乘后的通道——每个预乘通道里都带着 alpha，在那种空间里色相无从约束。
        private static readonly Windows.UI.Color ColorStart = Windows.UI.Color.FromArgb(255, 0x3A, 0x6E, 0xA5);
        private static readonly Windows.UI.Color ColorTarget = Windows.UI.Color.FromArgb(255, 0x80, 0x80, 0xD0);

        // 位移 Back：读数在 300 之前越过 300，再回落到 300。
        private readonly Transition<Rectangle> OverScalarBack =
            Transition<Rectangle>.Create()
                .Property(r => ((TranslateTransform)r.RenderTransform).X, ShiftTarget)
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

        // 位移 Elastic：越过幅度更大（峰值约 1.37 倍），回落次数也更多。
        private readonly Transition<Rectangle> OverScalarElastic =
            Transition<Rectangle>.Create()
                .Property(r => ((TranslateTransform)r.RenderTransform).X, ShiftTarget)
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

        // 颜色：过冲不能回绕（回绕会得到完全错误的颜色），也不能让某个通道单独跑出去（那会偏移色相）。
        private readonly Transition<Rectangle> OverColor =
            Transition<Rectangle>.Create()
                .Property(r => r.Fill, new SolidColorBrush(ColorTarget))
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

        // 尺寸：从 80 涨到 220，弹性回落的极小值约 0.98 倍，宽度始终落在零以上。
        // 注意 double 类型的宽度并没有"零边界"：采样器把缓动值原样透传，朝下的过冲会把宽度压成负值，
        // 而 WinUI 的 Width setter 会直接抛异常。所以这一步的目标值只朝上走，避免跌到零以下。
        private readonly Transition<Rectangle> OverSize =
            Transition<Rectangle>.Create()
                .Property(r => r.Width, WidthTarget)
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

        // 非纯色刷走的是混合刷子路径而不是纯色路径：交叉淡出系数与停靠点共享一个进度，在端点饱和而不是越界。
        private readonly Transition<Rectangle> OverBrush =
            Transition<Rectangle>.Create()
                .Property(r => r.Fill, CreateShiftedBs1())
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Back.Out });

        private static LinearGradientBrush CreateShiftedBs1()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Cyan, Offset = 0.25 },
                    new GradientStop() { Color = Colors.Orange, Offset = 0.75 },
                ]
            };
        }

        // 每次运行前先把自己那个目标同步地放回起始值（而不是用一次动画退回去）：Prepare 把目标的当前值当作
        // 动画起点，少了这一步，同一条按钮的第二次点击、或共用该元素的另一条按钮，就会从目标动画到目标，
        // 看起来什么都没发生——真机验收时这就等于"功能是坏的"。
        // 只重置本处理器自己的元素：重置整条过冲条会取消别的元素上正在跑的那一次，并排对比也就没了。
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
            Over1.Fill = new SolidColorBrush(ColorStart);
            BeginScenario("color", BackDurationMs, targetIndex: 1);
            OverColor.Execute(Over1);
        }

        private void OverShootSize(object sender, RoutedEventArgs e)
        {
            Transition.Exit(Over2, IncludeMutual: true, IncludeNoMutual: true);
            Over2.Width = BarWidth;
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

            // Written directly, like the reset above: these are the values the XAML declares.
            Over0.RenderTransform = new TranslateTransform();
            Over1.Fill = new SolidColorBrush(ColorStart);
            Over2.Width = BarWidth;
            Over3.Fill = CreateBs1Brush();
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
            var x = Over0.RenderTransform is TranslateTransform translate ? translate.X : double.NaN;
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
                 + $"t3.cur={Describe(Over3.Fill)};"
                 + RecState("r0", Rec0) + RecState("r1", Rec1) + RecState("r2", Rec2)
                 + $"nomutual={NoMutualCount()};";
        }

        /// <summary>
        /// 加载模式那一排三块目标的状态：动画真正写的那几个属性，读出来报给测试。
        /// </summary>
        /// <remarks>
        /// 与过冲条同一支定时器、同一次采样，所以两者不可能不一致。报的是"动的是什么"而不是"应该动到哪" ——
        /// 那三个动画各自带 auto-reverse 与 loop，终点要靠复算库的语义才知道，测试不去复算它。
        /// 三个字段与 WPF 侧同名同形，七个平台读起来是同一件事。
        /// </remarks>
        private static string RecState(string prefix, Rectangle target)
            => $"{prefix}.x={TranslateX(target):F3};"
             + $"{prefix}.fill={Describe(target.Fill)};"
             + $"{prefix}.opacity={target.Opacity:F3};";

        /// <summary>
        /// 动画真正写的那个位移，无论它被写成单个 TranslateTransform 还是组合进 TransformGroup。
        /// </summary>
        /// <remarks>
        /// 这里和 WPF 侧同名同义：动画写的是旋转（Rec1 的 RotateTransform）或投影（Rec2 的 PlaneProjection）
        /// 时它读作 0 —— 那个 0 是"这块目标上没有位移"，不是"测不到"。
        /// </remarks>
        private static double TranslateX(Rectangle target)
        {
            switch (target.RenderTransform)
            {
                case TranslateTransform translate:
                    return translate.X;
                case TransformGroup group:
                    foreach (var child in group.Children)
                    {
                        if (child is TranslateTransform nested) return nested.X;
                    }
                    return 0d;
                default:
                    return 0d;
            }
        }

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
                // WinUI 的 TransitionScheduler 是泛型的（TransitionScheduler<TTarget>），非泛型的静态入口在基类上。
                if (TransitionSchedulerCore.TryGetNoMutualScheduler(target, out var schedulers))
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
            var shift = Over0.RenderTransform is TranslateTransform t ? t.X : double.NaN;
            var fill = Over1.Fill is SolidColorBrush b
                ? $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}"
                : Over1.Fill?.GetType().Name ?? "无";

            Readout.Text =
                $"位移 X   目标 {ShiftTarget,6:F1}   当前 {shift,7:F1}"
                + $"     |     宽度   目标 {WidthTarget,6:F1}   当前 {Over2.Width,7:F1}"
                + $"     |     填充   {fill}";
            OverState.Text = BuildState();
        }
    }
}
