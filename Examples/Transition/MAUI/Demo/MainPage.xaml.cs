using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;
using VeloxDev.TransitionSystem;

namespace Demo
{
    public partial class MainPage : ContentPage
    {
        private bool _resetInitialized;

        public MainPage()
        {
            InitializeComponent();

            Rec0.Fill = CreateRec0Brush();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (_resetInitialized) return;
            _resetInitialized = true;

            // Explicitly initialize to a definite state first, so the reset paths below describe a known state.
            Rec0.Fill = CreateRec0Brush();
            Rec0.RotationX = 0; Rec0.RotationY = 0; Rec0.Scale = 1; Rec0.TranslationX = 0; Rec0.TranslationY = 0;
            Rec1.RotationX = 0; Rec1.RotationY = 0; Rec1.Scale = 1; Rec1.TranslationX = 0; Rec1.TranslationY = 0;
            Rec2.RotationX = 0; Rec2.RotationY = 0; Rec2.Scale = 1; Rec2.TranslationX = 0; Rec2.TranslationY = 0;

            btnReset.Clicked += (s, e) =>
            {
                Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                // Apply the reset synchronously: bypasses the async Execute pipeline
                // (unreliable for Transform reset on some platforms), and Rec0 gets a fresh object
                // each time so its references are not polluted by in-place animation edits.
                ApplyReset(CreateRec0Reset(), Rec0);
                ApplyReset(CreateRec1Reset(), Rec1);
                ApplyReset(CreateRec2Reset(), Rec2);

                ResetOverShoot();
            };

            // 过冲读数：用定时器轮询目标的真实属性，而不是订阅 effect 的事件——流水线每段都会 Clone 一份
            // effect，订阅在原始 effect 上的处理函数根本不会触发。MAUI 没有 DispatcherPriority，
            // 走的是 NonPriority 路径，这里也是那条无优先级分支的真机覆盖。
            var readoutTimer = Dispatcher.CreateTimer();
            readoutTimer.Interval = TimeSpan.FromMilliseconds(40);
            readoutTimer.Tick += (s, e) => UpdateReadout();
            readoutTimer.Start();

            // 采样器一致性把手：一条采样器一个按钮，点一下跑那一条、把结果写进载荷。把手与探针表同源，
            // 所以加一条采样器只需要改 SamplerProbe 一处。
            // 扫描落在点击处理函数里、也就是 UI 线程上 —— 它要构造画刷、阴影、变换这类有线程亲和性的对象；
            // 也正因为不在 Tick 里，那条「Tick 里的异常在 MAUI 上没人接」的陷阱不必碰。

            // 演示台插在把手条上方：把手是"验"，台子是"看"，点一次两件事一起发生。
            // 台子也从 SamplerProbe.SamplerNames 生成，所以它和把手条不可能各数各的。
            if (SamplerButtons.Parent is Layout benchHost)
            {
                benchHost.Children.Insert(
                    benchHost.Children.IndexOf(SamplerButtons),
                    _bench.Build(SamplerProbe.SamplerNames));
            }

            foreach (var sampler in SamplerProbe.SamplerNames)
            {
                var handle = new Button
                {
                    Text = sampler,
                    WidthRequest = 132,
                    HeightRequest = 30,
                    FontSize = 11,
                    Margin = new Thickness(2),
                    CommandParameter = sampler,
                };
                // MAUI 用的是自己的 AutomationId 属性，不是 WPF/WinUI 那个附加属性 AutomationProperties。
                handle.AutomationId = $"over.sampler.{sampler}";
                handle.Clicked += RunSamplerProbe;
                SamplerButtons.Children.Add(handle);
            }
        }

        /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
        private long _probeSequence;

        private readonly SamplerBench _bench = new();

        // 只留一支演出用的定时器：连点两个把手时，后一次要能叫停前一次，否则两条采样器会同时往各自的格子里写。
        private IDispatcherTimer? _benchTimer;

        // 每一步都不许抛：MAUI 会把未处理异常直接冒泡出去，而这里的 sender / CommandParameter 都由界面提供，
        // 不该因为某个把手没带上令牌就让整个 app 倒下。
        private void RunSamplerProbe(object? sender, EventArgs e)
        {
            if ((sender as Button)?.CommandParameter is not string sampler) return;

            // 采样器写在**这一格的在屏控件**上、载荷也从它读回，所以下面两件事是同一件事的两种读法。
            var subject = _bench.SubjectFor(sampler);

            // 验：五个固定的缓动时间各跑一帧，写进载荷。
            OverConf.Text = SamplerProbe.Run(subject, sampler, ++_probeSequence);

            // 看：按 Back.Out 把这条采样器跑一遍，它会越过端点再落回来 —— 肉眼看得到的就是这个。
            PlaySampler(sampler, subject);
        }

        /// <summary>
        /// 演出：把缓动进度从 0 走到 1，每一拍把该采样器在当前缓动时间上写出的值写到它那一格上。
        /// </summary>
        /// <remarks>
        /// Tick 里的每一步都不许抛：MAUI 不接 Tick 里的异常，它直接冒泡成未处理异常（dotnet/maui #12245）。
        /// 这里用到的每一处 —— 时钟、<see cref="SamplerProbe.Frame"/> —— 都不依赖外来的 null：
        /// 探针表里没有这个名字会抛，但那要等到把手与表不同步，而把手就是从那张表生成的。
        /// </remarks>
        private void PlaySampler(string sampler, SamplerSubject subject)
        {
            _benchTimer?.Stop();

            var duration = TimeSpan.FromMilliseconds(800);
            var clock = Stopwatch.StartNew();
            var timer = Dispatcher.CreateTimer();
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

        private void LoadMainThread(object sender, EventArgs e)
        {
            // Start directly on the main (UI) thread; CanMutualTask: true (default) — mutually
            // exclusive, the new animation interrupts the old one
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        }

        private void LoadBackground(object sender, EventArgs e)
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

        private void LoadMainThreadNonMutual(object sender, EventArgs e)
        {
            // Main thread + CanMutualTask: false — run concurrently, neither cancels the other
            Animation0.Execute(Rec0, CanMutualTask: false);
            Animation1.Execute(Rec1, CanMutualTask: false);
            Animation2.Execute(Rec2, CanMutualTask: false);
        }

        private void LoadBackgroundNonMutual(object sender, EventArgs e)
        {
            // Non-UI thread + CanMutualTask: false — run concurrently
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0, CanMutualTask: false);
                Animation1.Execute(Rec1, CanMutualTask: false);
                Animation2.Execute(Rec2, CanMutualTask: false);
            });
        }

        private void RepeatMutual(object sender, EventArgs e)
        {
            // Each click starts a mutually-exclusive animation on Rec0, and the new animation cancels
            // the previous one (tests scheduler gating and cancellation).
            _ = Task.Run(() => Animation0.Execute(Rec0));
        }

        private void ExitAll(object sender, EventArgs e)
        {
            // IncludeMutual   indicates whether to end animations configured with CanMutualTask: true
            // IncludeNoMutual indicates whether to end animations configured with CanMutualTask: false
            Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    public partial class MainPage
    {
        // Simple animation: translate + demonstrates a nested property path, directly modifying
        // Fill.StartPoint / Fill.EndPoint
        private static readonly Transition<Rectangle> Animation0 =
            Transition<Rectangle>.Create()
                .Property(r => r.TranslationX, 240)
                .Property(r => ((LinearGradientBrush)r.Fill!).StartPoint, new Point(0, 1))
                .Property(r => ((LinearGradientBrush)r.Fill!).EndPoint, new Point(1, 1))
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        private static LinearGradientBrush CreateRec0Brush()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new GradientStop(Colors.Cyan, 0),
                    new GradientStop(Colors.Yellow, 1)
                ]
            };
        }

        private static Transition<Rectangle> CreateRec0Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.TranslationX, 0)
                .Property(r => r.Fill, CreateRec0Brush())
                .Effect(TransitionEffects.Empty);
        }

        // Covers every property Animation1 touches — restoring only TranslationX/Fill would leave Rec1
        // stopped at whatever rotation the animation had reached.
        private static Transition<Rectangle> CreateRec1Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.RotationX, 0)
                .Property(r => r.TranslationX, 0)
                .Property(r => r.Fill, new SolidColorBrush(Colors.Lime))
                .Effect(TransitionEffects.Empty);
        }

        // Covers every property Animation2 touches: RotationX/Y, TranslationX/Y, Scale and Fill.
        private static Transition<Rectangle> CreateRec2Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.RotationX, 0)
                .Property(r => r.RotationY, 0)
                .Property(r => r.TranslationX, 0)
                .Property(r => r.TranslationY, 0)
                .Property(r => r.Scale, 1d)
                .Property(r => r.Fill, CreateBs1Brush())
                .Effect(TransitionEffects.Empty);
        }

        // The Bs1 resource of MainPage.xaml (Yellow → Violet, 0,0 → 1,1), rebuilt in code because a resource is not
        // a value a transition path can point at. Keep the two in step: Rec2's reset animates Fill back to this
        // brush, so editing the XAML alone would leave the reset animating to a stale one.
        private static LinearGradientBrush CreateBs1Brush()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop(Colors.Yellow, 0),
                    new GradientStop(Colors.Violet, 1)
                ]
            };
        }

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

        // Delayed animation - rotation
        private static readonly Transition<Rectangle> Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(2))
                .Property(r => r.RotationX, 180)     // MAUI X rotation
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        // Combined animation - composite transforms
        private static readonly Transition<Rectangle> Animation2 =
            Transition<Rectangle>.Create()
                // First segment: translate + scale
                .Property(r => r.RotationX, 180)
                .Property(r => r.RotationY, 180)
                .Property(r => r.TranslationX, 200)
                .Property(r => r.TranslationY, 0)
                .Property(r => r.Scale, 1.3)         // MAUI overall scale
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    FPS = 144,
                    Ease = Eases.Circ.InOut,
                    LoopTime = 2,
                })
                .AwaitThen(TimeSpan.FromSeconds(5))
                // Second segment: color change
                .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    Ease = Eases.Sine.In
                });

        // -----------------------------------------------------------------------------------------------
        // 过冲（overshoot）
        //
        // 上面用到的缓动全部落在 [0,1] 内，一条都不可能越过目标再回来。Back 峰值 1.10、Elastic 峰值 1.37，
        // 而过冲在每个采样器上意味着什么由采样器自己决定：double（TranslationX / WidthRequest）不设限，
        // 真的越过目标再回来；颜色的 R/G/B 共用一个进度、整组一起走、在第一个触边的通道处停下，
        // 色相才不会飘；渐变刷的色标偏移同样共用一个进度、停在 [0,1] 内，不会交叉翻面。
        // 读数才是过冲可见的证据：数字越过目标再回来，肉眼分辨不出它与一条更慢的缓动。
        // -----------------------------------------------------------------------------------------------

        private const double ShiftTarget = 300d;
        private const double WidthTarget = 220d;

        // 过冲条在 XAML 里声明的初始尺寸与初始填充色。起始值也只有这一份来源，逐场景重置和整条重置都读它。
        private const double SizeStart = 80d;
        private static readonly Color OverColorStart = new(0x3A / 255f, 0x6E / 255f, 0xA5 / 255f, 1f);

        // 首选色标偏移，两个色标关于中点对称。动画与读数共用同一个常量，不各写一份字面量。
        private const float GradientStopTarget = 0.25f;

        // 时长同时喂给 effect 与载荷：载荷靠它推出 done，两处若各写一份就会漂移。
        private const int BackDurationMs = 900;
        private const int ElasticDurationMs = 1100;

        private static readonly Transition<Rectangle> OverScalarBack =
            Transition<Rectangle>.Create()
                .Property(r => r.TranslationX, ShiftTarget)
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

        private static readonly Transition<Rectangle> OverScalarElastic =
            Transition<Rectangle>.Create()
                .Property(r => r.TranslationX, ShiftTarget)
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

        // 目标色每个通道都留有余量：有空间可走，共用的进度就能真的把三个通道一起推过目标，
        // 而不是在第一个触顶的通道处被截断（那会让明度上升、色相偏掉）。
        private static readonly Transition<Rectangle> OverColor =
            Transition<Rectangle>.Create()
                .Property(r => r.Fill, new SolidColorBrush(new Color(0x80 / 255f, 0x80 / 255f, 0xD0 / 255f, 1f)))
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

        // MAUI 的 VisualElement 上没有可写的 Size 型属性（适配器注册的 Size/Rect/SizeF/RectF 采样器
        // 从普通控件上够不到），最接近的落点是 double 型的 WidthRequest：同样是"尺寸"，
        // 由 DoubleSampler 采样、上界不设限，高度不动，正好看得出它只改宽不加高。
        private static readonly Transition<Rectangle> OverSize =
            Transition<Rectangle>.Create()
                .Property(r => r.WidthRequest, WidthTarget)
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

        // 非纯色刷不走纯色那条路：交叉淡出。色标偏移共用一个进度并停在 [0,1] 内，所以过冲既不会让两个色标
        // 交叉翻面，也不会从首色标这一端漏出去。
        private static readonly Transition<Rectangle> OverBrush =
            Transition<Rectangle>.Create()
                .Property(r => r.Fill, CreateShiftedBs1Brush())
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Back.Out });

        // 与 CreateBs1Brush 同形（起点/终点一致），只把两个色标从 0/1 拉到 0.25/0.75。
        private static LinearGradientBrush CreateShiftedBs1Brush()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop(Colors.Cyan, GradientStopTarget),
                    new GradientStop(Colors.Orange, 1f - GradientStopTarget)
                ]
            };
        }

        // 每次运行先把自己那个目标同步地放回起始值，而不是动画回去：Prepare 以目标的当前值作为动画起点，
        // 少了这一步，第二次点击（或共用该元素的兄弟按钮）就是从目标动到目标，看起来什么都没发生。
        // 只重置这一个元素，别的场景继续跑，过冲条才保持可对比。
        private void OverShootScalarBack(object sender, EventArgs e) => RunScalar(OverScalarBack, "back", BackDurationMs);
        private void OverShootScalarElastic(object sender, EventArgs e) => RunScalar(OverScalarElastic, "elastic", ElasticDurationMs);

        private void RunScalar(Transition<Rectangle> animation, string scenario, int durationMs)
        {
            Transition.Exit(Over0, IncludeMutual: true, IncludeNoMutual: true);
            Over0.TranslationX = 0;
            BeginScenario(scenario, durationMs, targetIndex: 0);
            animation.Execute(Over0);
        }

        private void OverShootColor(object sender, EventArgs e)
        {
            Transition.Exit(Over1, IncludeMutual: true, IncludeNoMutual: true);
            Over1.Fill = new SolidColorBrush(OverColorStart);
            BeginScenario("color", BackDurationMs, targetIndex: 1);
            OverColor.Execute(Over1);
        }

        private void OverShootSize(object sender, EventArgs e)
        {
            Transition.Exit(Over2, IncludeMutual: true, IncludeNoMutual: true);
            Over2.WidthRequest = SizeStart;
            BeginScenario("size", ElasticDurationMs, targetIndex: 2);
            OverSize.Execute(Over2);
        }

        private void OverShootGradient(object sender, EventArgs e)
        {
            Transition.Exit(Over3, IncludeMutual: true, IncludeNoMutual: true);
            Over3.Fill = CreateBs1Brush();
            BeginScenario("brush", ElasticDurationMs, targetIndex: 3);
            OverBrush.Execute(Over3);
        }

        private void OverShootReset(object sender, EventArgs e) => ResetOverShoot();

        private void ResetOverShoot()
        {
            foreach (var target in new[] { Over0, Over1, Over2, Over3 })
                Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

            // 直接写回 XAML 声明的初值，与上面的重置同理：绕开异步 Execute 管线。
            Over0.TranslationX = 0;
            Over1.Fill = new SolidColorBrush(OverColorStart);
            Over2.WidthRequest = SizeStart;
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
            var x = Over0.TranslationX;
            var width = Over2.WidthRequest;
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
        /// 加载模式那一排三块目标的状态：动画真正写的那些属性，读出来报给测试。
        /// </summary>
        /// <remarks>
        /// 与过冲条同一支定时器、同一次采样，所以两者不可能不一致。报的是"动的是什么"而不是"应该动到哪" ——
        /// 那三条动画各自带 auto-reverse 与 loop，终点要靠复算库的语义才知道，测试不去复算它。
        /// MAUI 没有 Transform 集合：位置是 TranslationX/TranslationY，旋转是 RotationX/RotationY，缩放是 Scale。
        /// </remarks>
        private static string RecState(string prefix, Rectangle target)
            => $"{prefix}.x={target.TranslationX:F3};"
             + $"{prefix}.y={target.TranslationY:F3};"
             + $"{prefix}.rotx={target.RotationX:F3};"
             + $"{prefix}.roty={target.RotationY:F3};"
             + $"{prefix}.scale={target.Scale:F3};"
             + $"{prefix}.fill={Describe(target.Fill)};"
             + $"{prefix}.opacity={target.Opacity:F3};";

        /// <summary>
        /// 这三块目标上还有几条**并发**（非互斥）动画在跑。
        /// </summary>
        /// <remarks>
        /// 这是唯一能把"互斥加载"和"并发加载"区分开的可观测量：互斥调度器是按目标缓存的一辈子不释放，
        /// `TryGetMutualScheduler` 返回 true 只说明"这目标跑过互斥动画"；而非互斥的那张表在每条动画结束时
        /// 真的会清空。要点是取**数组长度**而不是那个 bool —— 表项本身不随运行结束移除。
        /// 每一步都不许抛：这是在 IDispatcherTimer.Tick 里跑的。
        /// </remarks>
        private int NoMutualCount()
        {
            var running = 0;

            foreach (var target in new[] { Rec0, Rec1, Rec2 })
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
        {
            // MAUI 的 Color 是引用类型，未赋值时可能为 null —— 读数每一步都不许抛。
            if (brush is not SolidColorBrush solid || solid.Color is not { } color)
                return brush?.GetType().Name ?? "none";

            // MAUI 的 Color 分量是 0..1 的浮点，先转回字节再写成 #rrggbb。
            return $"#{Channel(color.Red):X2}{Channel(color.Green):X2}{Channel(color.Blue):X2}";
        }

        private static byte Channel(float value) => (byte)Math.Clamp(Math.Round(value * 255f), 0f, 255f);

        // 读数只读目标的真实属性，且每一步都不许抛：IDispatcherTimer.Tick 里的异常在 MAUI/WinUI 上
        // 不会被框架接住，会直接冒泡成未处理异常（dotnet/maui #12245）。
        // MAUI 没有 DispatcherPriority，这条读数同时是 NonPriority 采样路径的真机覆盖。
        // 载荷与读数共用这一次采样，两者不可能互相矛盾。
        private void UpdateReadout()
        {
            var color = Over1.Fill is SolidColorBrush solid ? solid.Color : Colors.Transparent;
            var stops = Over3.Fill is LinearGradientBrush gradient ? gradient.GradientStops : null;
            var offset = stops is { Count: > 0 } ? stops[0].Offset : float.NaN;

            Readout.Text =
                $"位移 X  目标 {ShiftTarget,5:F1}  当前 {Over0.TranslationX,7:F1}\n"
                + $"宽度    目标 {WidthTarget,5:F1}  当前 {Over2.WidthRequest,7:F1}\n"
                + $"颜色 R/G/B 当前 {255 * color.Red,4:F0}/{255 * color.Green,4:F0}/{255 * color.Blue,4:F0}"
                + $"   |   渐变 stop0 目标 {GradientStopTarget,4:F2}  当前 {offset,5:F3}";
            OverState.Text = BuildState();
        }
    }
}
