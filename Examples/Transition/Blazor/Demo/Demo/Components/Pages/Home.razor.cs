using Demo.Models;
using Microsoft.AspNetCore.Components;
using System.Diagnostics;
using VeloxDev.TransitionSystem;

namespace Demo.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    // ---------------------------------------------------------------
    // ViewModel instances — animations operate directly on their properties
    // ---------------------------------------------------------------
    // Single source for the three initial colors: the reset below has to restore them, so the literals live here
    // rather than being repeated.
    private const string Box0Color = "#00bcd4";
    private const string Box1Color = "#66bb6a";
    private const string Box2Color = "#ab47bc";

    // 加载模式那一排三块目标与过冲条同尺度，而不是 BoxModel 默认的 120x80：两半读起来是一张台子，
    // 验收也用同一种方式驱动。尺寸只有这一处声明 —— 构造与重置（CreateReset）都从这里取，
    // 否则重置会把方块放大回默认尺寸，"重置回到静止态"就成了假命题。
    private const double BoxStripWidth = 60d;
    private const double BoxStripHeight = 60d;

    private BoxModel Box0 { get; } = new() { Width = BoxStripWidth, Height = BoxStripHeight, Color = Box0Color };
    private BoxModel Box1 { get; } = new() { Width = BoxStripWidth, Height = BoxStripHeight, Color = Box1Color };
    private BoxModel Box2 { get; } = new() { Width = BoxStripWidth, Height = BoxStripHeight, Color = Box2Color };

    // ---------------------------------------------------------------
    // Animation definitions (mirroring the three animations of the WPF/Avalonia Demo)
    // ---------------------------------------------------------------

    // Animation0: simple animation — translate + color + opacity, auto reverse loop
    private static readonly Transition<BoxModel> Animation0 =
        Transition<BoxModel>.Create()
            .Property(b => b.X, 500)
            .Property(b => b.Color, "#ff7043")
            .Property(b => b.Opacity, 0.2)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Sine.InOut,
            });

    // Animation1: delayed animation — rotate + scale after a 2 second wait
    private static readonly Transition<BoxModel> Animation1 =
        Transition<BoxModel>.Create()
            .Await(TimeSpan.FromSeconds(2))
            .Property(b => b.Rotate, 360)
            .Property(b => b.Scale, 1.5)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(3),
                IsAutoReverse = true,
                LoopTime = 4,
                FPS = 60,
                Ease = Eases.Circ.InOut,
            });

    // -------------------------------------------------------------------
    // 过冲演示（Overshoot）
    //
    // 上面三条动画的缓动全部落在 [0,1] 内，越不过目标值；Back.Out 峰值 1.100、Elastic.Out 1.373，
    // 只有它们会冲过目标再回弹。四个并排目标让 Back 与 Elastic、颜色与尺寸能在同一次运行里对比。
    // 读数必须由定时器采样目标的真实属性：流水线每段都会 Clone() effect，订阅在原始 effect 上的
    // 处理函数不会触发。
    // -------------------------------------------------------------------

    // 动画与读数共用同一份常量，避免各写一份字面量后漂移
    // 位移取 220 而不是参考实现的 300：Elastic.Out 峰值 1.373，300 需要约 480px 的格子才装得下，
    // 220 能让整段过冲都留在格子里，数值证明由读数给出
    private const double ShiftTarget = 220d;
    private const double WidthTarget = 220d;
    private const double OverStripWidth = 60d;

    private const string OverStartColor = "#3a6ea5";
    // 目标色每个通道都留有余量（128/128/208），红通道要到进度约 1.99 才撞上限，
    // 远高于 Back.Out 的峰值 1.10，所以共享进度不被截断，过冲只提亮而不移动色相
    private const string OverColorTarget = "#8080d0";
    // 目标色刻意让红通道顶到上限：58 → 246 的退出进度约 1.048，低于 Back.Out 的峰值 1.10，
    // 于是进度在边界停住（红＝255），而不是绕回（无钳位时裸转 byte 会把 263 变成 7）
    private const string OverSaturateTarget = "#f6e68c";

    private BoxModel Over0 { get; } = new() { Width = OverStripWidth, Height = OverStripWidth, Color = OverStartColor };
    private BoxModel Over1 { get; } = new() { Width = OverStripWidth, Height = OverStripWidth, Color = OverStartColor };
    private BoxModel Over2 { get; } = new() { Width = OverStripWidth, Height = OverStripWidth, Color = OverStartColor };
    private BoxModel Over3 { get; } = new() { Width = OverStripWidth, Height = OverStripWidth, Color = OverStartColor };

    // 时长同时喂给 effect 与载荷：载荷靠它推出 done，两处若各写一份就会漂移。
    private const int BackDurationMs = 900;
    private const int ElasticDurationMs = 1100;

    // 位移：同一个目标上两条不同缓动，用于对比过冲幅度（峰值 Back 1.100、Elastic 1.373）
    private static readonly Transition<BoxModel> OverScalarBack =
        Transition<BoxModel>.Create()
            .Property(b => b.X, ShiftTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

    private static readonly Transition<BoxModel> OverScalarElastic =
        Transition<BoxModel>.Create()
            .Property(b => b.X, ShiftTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

    // 颜色：Razor 适配器把 string 注册成 StringSampler，所以这里走的是 CSS 颜色字符串路径，
    // 中间帧产出的是 rgba(...) 文本，而两端仍然原样写回调用方给的字符串
    private static readonly Transition<BoxModel> OverColor =
        Transition<BoxModel>.Create()
            .Property(b => b.Color, OverColorTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

    // 尺寸：Blazor 的宽度只是一个 double，没有构造器边界；过冲会实打实越过 220 再回来
    private static readonly Transition<BoxModel> OverSize =
        Transition<BoxModel>.Create()
            .Property(b => b.Width, WidthTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

    // 饱和：撞上限的通道在边界停住，证明分数是被钳住而不是回绕
    private static readonly Transition<BoxModel> OverSaturate =
        Transition<BoxModel>.Create()
            .Property(b => b.Color, OverSaturateTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

    private System.Threading.Timer? _readoutTimer;
    private volatile bool _disposed;

    // 定时器每拍采一次样，同时产出人类读数与机器可读载荷两份快照：同一次采样、同一批值，
    // 两者不可能互相矛盾，渲染只读这两份快照。
    private string _readoutSnapshot = "按上面任一按钮；读数显示目标的真实属性值与目标值";
    private string _stateSnapshot = "v=1;seq=0";

    // 采样器一致性载荷：点一次把手跑一条、写一次，所以不在定时器里更新。
    private string _confSnapshot = "v=1;seq=0;n=0;";

    /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
    private long _confSequence;

    /// <summary>演示台的被写对象当前该是什么颜色 —— 就是 StringSampler 最后写出的那个 CSS 颜色。</summary>
    private string _benchColor = "#808080";

    // 只留一支演出用的定时器：连点两个把手时，后一次要能叫停前一次。
    private System.Threading.Timer? _benchTimer;

    /// <summary>跑一条采样器并把结果写进载荷。</summary>
    private void RunSamplerProbe(string sampler)
    {
        // 验：五个固定的缓动时间各跑一帧，写进载荷。
        _confSnapshot = SamplerProbe.Run(sampler, ++_confSequence);

        // 看：按 Back.Out 把这条采样器跑一遍，它会越过端点再落回来。
        PlaySampler(sampler);
    }

    /// <summary>
    /// 演出：把缓动进度从 0 走到 1，每一拍把该采样器在当前缓动时间上写出的值画到演示台上。
    /// </summary>
    /// <remarks>
    /// Razor 适配器只有 StringSampler，产物就是 CSS 颜色字符串，所以被写对象直接把它当背景色用 ——
    /// 这里不需要 WPF 那边的标尺：颜色本来就没有"行程"可言。
    /// </remarks>
    private void PlaySampler(string sampler)
    {
        _benchTimer?.Dispose();

        var duration = TimeSpan.FromMilliseconds(800);
        var clock = Stopwatch.StartNew();

        _benchTimer = new System.Threading.Timer(
            _ =>
            {
                if (_disposed) return;

                var progress = Math.Min(1d, clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
                if (SamplerProbe.FrameValue(sampler, Eases.Back.Out.Ease(progress)) is string css)
                {
                    _benchColor = css;
                }

                InvokeAsync(StateHasChanged);

                if (progress >= 1d)
                {
                    _benchTimer?.Dispose();
                    _benchTimer = null;
                }
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(16));
    }

    // 读数：显示目标的真实当前值与目标值，过冲只有靠数字才看得出来
    private string BuildReadout() =>
        $"位移 X    目标 {ShiftTarget,6:F1}    当前 {Over0.X,7:F1}"
        + $"    |    宽度    目标 {WidthTarget,6:F1}    当前 {Over2.Width,7:F1}\n"
        + $"颜色 余量    目标 {OverColorTarget}    当前 {Over1.Color}\n"
        + $"颜色 饱和    目标 {OverSaturateTarget}    当前 {Over3.Color}";

    // -------------------------------------------------------------------
    // 验收观测面
    //
    // 人类读数之外再写一份机器可读的载荷：同一次采样、同一批值，但用固定的 key=value 而不是散文，
    // 测试就不必去解析一份随时可能被重新排版的版式。载荷只报告"每个目标当前/峰值是多少"，至于哪个场景
    // 动哪个目标、起止与时长，由测试侧的 manifest 声明 —— 观测与语义各自只有一处来源。
    // Blazor 的位移目标是 220 而不是参考实现的 300，载荷保留它自己的数字。
    // -------------------------------------------------------------------

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
        var x = Over0.X;
        var width = Over2.Width;
        _targetPeaks[0] = Math.Max(_targetPeaks[0], x);
        _targetPeaks[2] = Math.Max(_targetPeaks[2], width);

        // done 由时长推出，而不是订阅 effect.Completed：流水线每段克隆 effect，订阅在原件上的处理函数不触发。
        // 它是必需的，不能靠"值等于目标"判断结束 —— 两条曲线都会中途再次穿过目标（Elastic 在 1.1s 内穿越七次）。
        var elapsed = _scenarioClock.ElapsedMilliseconds;
        var done = _scenario != "none" && elapsed >= _scenarioDurationMs + 50 ? 1 : 0;

        return $"v=1;seq={_sequence};scen={_scenario};t={elapsed};done={done};"
             + $"t0.cur={x:F3};t0.peak={Peak(0)};"
             + $"t1.cur={Describe(Over1.Color)};"
             + $"t2.cur={width:F3};t2.peak={Peak(2)};"
             + $"t3.cur={Describe(Over3.Color)};"
             + RecState("r0", Box0) + RecState("r1", Box1) + RecState("r2", Box2)
             + $"nomutual={NoMutualCount()};";
    }

    /// <summary>
    /// 加载模式那一排三块目标的状态：动画真正写的那几个属性，读出来报给测试。
    /// </summary>
    /// <remarks>
    /// 与过冲条同一支定时器、同一次采样，所以两者不可能不一致。报的是"动的是什么"而不是"应该动到哪" ——
    /// 那三条动画各自带 auto-reverse 与 loop，终点要靠复算库的语义才知道，测试不去复算它。字段形状与
    /// 参考实现的 RecState 对齐，多报一个 rotate 与 scale（Blazor 的旋转与缩放是独立属性，不是变换对象里的一支）。
    /// </remarks>
    private static string RecState(string prefix, BoxModel target)
        => $"{prefix}.x={target.X:F3};"
         + $"{prefix}.rotate={target.Rotate:F3};"
         + $"{prefix}.scale={target.Scale:F3};"
         + $"{prefix}.color={Describe(target.Color)};"
         + $"{prefix}.opacity={target.Opacity:F3};";

    /// <summary>
    /// 这三块目标上还有几条**并发**（非互斥）动画在跑。
    /// </summary>
    /// <remarks>
    /// 这是唯一能把"互斥加载"和"并发加载"区分开的可观测量：互斥调度器是按目标缓存的一辈子不释放，
    /// `TryGetMutualScheduler` 返回 true 只说明"这目标跑过互斥动画"；而非互斥的那张表在每条动画结束时
    /// 真的会清空。要点是取**数组长度**而不是那个 bool —— 表项本身不随运行结束移除。
    /// </remarks>
    private int NoMutualCount()
    {
        var running = 0;

        foreach (var target in new[] { Box0, Box1, Box2 })
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

    // 把颜色写成测试能读懂的形式：Blazor 的颜色就是 CSS 字符串，原样透传。
    private static string Describe(string? css) => string.IsNullOrEmpty(css) ? "none" : css;

    // Animation2: combined animation — move right first, then recolor + shrink after a 3s wait
    private static readonly Transition<BoxModel> Animation2 =
        Transition<BoxModel>.Create()
            .Property(b => b.X, 400)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                Ease = Eases.Expo.Out,
            })
            .AwaitThen(TimeSpan.FromSeconds(3))
            .Property(b => b.Color, "#ffee58")
            .Property(b => b.Scale, 0.6)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(1.5),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Bounce.Out,
            });

    protected override void OnInitialized()
    {
        // Blazor animation targets POCO ViewModels with no dispatcher affinity, so a background
        // thread cannot infer the circuit context. It must be captured here (OnInitialized, on the
        // circuit thread). This is an inherent limitation of the Blazor model.
        UIThreadInspector.CaptureUIThread();

        // Subscribe to property changes to drive Blazor re-rendering
        Box0.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
        Box1.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
        Box2.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);

        // 过冲区的目标同样由 PropertyChanged 驱动重渲染（定时器只负责刷新读数）
        foreach (var box in OvershootTargets)
            box.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return Task.CompletedTask;

        // 读数定时器在这里而不是 OnInitialized 里建：预渲染阶段没有交互回路，
        // OnAfterRenderAsync 只在交互回路首次渲染时执行，正好避开重复创建。
        // 回调先查 _disposed：定时器线程可能在 Dispose 之后才轮到，
        // 那时 InvokeAsync 会抛 ObjectDisposedException 且没人接。
        _readoutTimer = new System.Threading.Timer(
            _ =>
            {
                if (_disposed) return;
                // 先采样再重渲染：载荷的 seq 每拍只前进一次，读数与载荷出自同一次采样。
                _readoutSnapshot = BuildReadout();
                _stateSnapshot = BuildState();
                InvokeAsync(StateHasChanged);
            },
            null,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50));

        return Task.CompletedTask;
    }

    private BoxModel[] OvershootTargets => [Over0, Over1, Over2, Over3];

    private void LoadMainThread()
    {
        // Start directly on the main (circuit) thread; mutual exclusion (CanMutualTask: true by default)
        Animation0.Execute(Box0);
        Animation1.Execute(Box1);
        Animation2.Execute(Box2);
    }

    private void LoadAnimations()
    {
        // Can also be started from a non-UI thread; the framework switches automatically
        _ = Task.Run(() =>
        {
            Animation0.Execute(Box0);
            Animation1.Execute(Box1);
            Animation2.Execute(Box2);
        });
    }

    private void LoadMainThreadNonMutual()
    {
        // Main thread + CanMutualTask: false — run concurrently, neither cancels the other
        Animation0.Execute(Box0, CanMutualTask: false);
        Animation1.Execute(Box1, CanMutualTask: false);
        Animation2.Execute(Box2, CanMutualTask: false);
    }

    private void LoadAnimationsNonMutual()
    {
        // CanMutualTask: false — the three animations run concurrently without interference and
        // are not cancelled by one another
        _ = Task.Run(() =>
        {
            Animation0.Execute(Box0, CanMutualTask: false);
            Animation1.Execute(Box1, CanMutualTask: false);
            Animation2.Execute(Box2, CanMutualTask: false);
        });
    }

    private void LoadRepeatedMutual()
    {
        // Each click starts a mutually-exclusive animation on Box0: the new animation cancels the
        // previous one (tests scheduler gating and cancellation).
        _ = Task.Run(() => Animation0.Execute(Box0));
    }

    private void ResetBox0()
    {
        // Reset all: stop everything, then replay the initial state as a transition — the same kind of thing as any
        // other animation.
        Transition.Exit(Box0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box2, IncludeMutual: true, IncludeNoMutual: true);

        CreateReset(Box0Color).Effect(TransitionEffects.Empty).Execute(Box0);
        CreateReset(Box1Color).Effect(TransitionEffects.Empty).Execute(Box1);
        CreateReset(Box2Color).Effect(TransitionEffects.Empty).Execute(Box2);

        // 「重置」一并复位过冲区，否则残留在过冲位置的方块会让下一次对比失去基准
        ResetOvershoot();
    }

    // -------------------------------------------------------------------
    // 过冲区的按钮与重置
    // -------------------------------------------------------------------

    // Prepare 读取目标的实时值作为动画起点，所以不先归位的话，同一个按钮点第二次（或共用 Over0 的兄弟按钮）
    // 会从目标动到目标，看起来什么都没发生。只归位本次执行的那个目标：整条条带一起重置会掐掉别的元素上
    // 正在跑的那一段，并排对比就没了。归位直接写值、不走动画，和参考实现一致。
    private void OverShootScalarBack() => RunScalar(OverScalarBack, "back", BackDurationMs);
    private void OverShootScalarElastic() => RunScalar(OverScalarElastic, "elastic", ElasticDurationMs);

    private void RunScalar(Transition<BoxModel> animation, string scenario, int durationMs)
    {
        Transition.Exit(Over0, IncludeMutual: true, IncludeNoMutual: true);
        Over0.X = 0;
        BeginScenario(scenario, durationMs, targetIndex: 0);
        animation.Execute(Over0);
    }

    private void OverShootColor()
    {
        Transition.Exit(Over1, IncludeMutual: true, IncludeNoMutual: true);
        Over1.Color = OverStartColor;
        BeginScenario("color", BackDurationMs, targetIndex: 1);
        OverColor.Execute(Over1);
    }

    private void OverShootSize()
    {
        Transition.Exit(Over2, IncludeMutual: true, IncludeNoMutual: true);
        Over2.Width = OverStripWidth;
        BeginScenario("size", ElasticDurationMs, targetIndex: 2);
        OverSize.Execute(Over2);
    }

    // Razor 适配器没有刷子对象，非纯色场景由「颜色饱和」顶替（CSS 字符串路径），
    // 但 token 与场景名沿用参考实现的 brush，保持跨 demo 的观测面一致。
    private void OverShootSaturate()
    {
        Transition.Exit(Over3, IncludeMutual: true, IncludeNoMutual: true);
        Over3.Color = OverStartColor;
        BeginScenario("brush", BackDurationMs, targetIndex: 3);
        OverSaturate.Execute(Over3);
    }

    private void OverShootReset() => ResetOvershoot();

    private void ResetOvershoot()
    {
        foreach (var box in OvershootTargets)
            Transition.Exit(box, IncludeMutual: true, IncludeNoMutual: true);

        // 直接写回初始值：绕过异步 Execute 流水线，重置立即生效且确定
        Over0.X = 0;
        Over1.Color = OverStartColor;
        Over2.X = 0;
        Over2.Width = OverStripWidth;
        Over3.Color = OverStartColor;
    }

    // The load-mode row's declared rest state, expressed as explicit paths and taken from the same constants the boxes
    // are built with. Color is animatable here as well — the Razor adapter registers a sampler for string — and the
    // three boxes start from different colors, so each box needs its own reset rather than one shared instance.
    private static Transition<BoxModel> CreateReset(string color)
    {
        return Transition<BoxModel>.Create()
            .Property(b => b.X, 0)
            .Property(b => b.Y, 0)
            .Property(b => b.Width, BoxStripWidth)
            .Property(b => b.Height, BoxStripHeight)
            .Property(b => b.Opacity, 1)
            .Property(b => b.Rotate, 0)
            .Property(b => b.Scale, 1)
            .Property(b => b.Color, color);
    }

    private void ExitAnimations()
    {
        // IncludeMutual   indicates whether to end animations configured with CanMutualTask: true
        // IncludeNoMutual indicates whether to end animations configured with CanMutualTask: false
        Transition.Exit(Box0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box2, IncludeMutual: true, IncludeNoMutual: true);
    }

    public void Dispose()
    {
        ExitAnimations();

        // 页面销毁时停掉读数定时器，并终止过冲区的动画（否则回路断开后它们还在写属性）
        _disposed = true;
        _readoutTimer?.Dispose();
        _readoutTimer = null;

        _benchTimer?.Dispose();
        _benchTimer = null;
        foreach (var box in OvershootTargets)
            Transition.Exit(box, IncludeMutual: true, IncludeNoMutual: true);
    }
}
