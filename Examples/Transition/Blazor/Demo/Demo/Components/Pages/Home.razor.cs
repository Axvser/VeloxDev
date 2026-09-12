using System.Diagnostics;
using Demo.Models;
using Microsoft.AspNetCore.Components;
using VeloxDev.TransitionSystem;

namespace Demo.Components.Pages;

/// <summary>
/// VeloxDev Transition 的 Blazor 演示。
/// </summary>
/// <remarks>
/// 版面与其余几个平台的 Transition demo 对齐（见 <c>Home.razor</c>）：顶栏（全局三件 + 五种加载方式）压在上面，
/// 下面是读数，再往下是一张**案例列表** —— 一条案例一行，行里是那条案例真正在动的元素、一句"这条在验什么"，
/// 以及这一行自己的 启动 / 关闭 / 重置。
/// <para>
/// 三种行（加载 3 条、过冲 5 条、每条采样器 1 条）长得一样，区别只在各自的令牌、描述与动作里，三条合成一处的
/// 定义在 <see cref="RebuildCaseList"/>。桌面那几侧的行携带的是在屏控件；浏览器里没有"控件属性"可写，行携带的
/// 是那块元素的 ViewModel（或采样器那个着色 div），重渲染由它的 PropertyChanged 驱动。
/// </para>
/// <para>
/// <b>位移那两条（Back / Elastic）各有自己的一块目标。</b>一个元素只能有一个父级，而每一行各自是一个父级 ——
/// 所以"两条曲线同屏对比"在列表里是"上下相邻两行同时看得见"：同一条时间轴上，仍然是同一次对比。
/// </para>
/// </remarks>
public partial class Home : ComponentBase, IDisposable
{
    // ---------------------------------------------------------------
    // 案例列表里那几块元素的声明色与尺寸
    //
    // 三块加载目标与五块过冲目标以前摊在整页上（那正是它们行程按页宽定下来的原因），现在每一块是列表里的一行。
    // 尺寸只有这一处声明 —— 构造与重置（CreateReset）都从这里取，否则重置会把方块放大回默认尺寸，
    // "重置回到静止态"就成了假命题。
    // ---------------------------------------------------------------

    private const string Box0Color = "#00bcd4";
    private const string Box1Color = "#66bb6a";
    private const string Box2Color = "#ab47bc";

    // 过冲五行的起点色与两个目标色。Razor 适配器把 string 注册成颜色采样器，所以颜色就是 CSS 字符串。
    private const string OverStartColor = "#3a6ea5";

    // 目标色每个通道都留有余量（128/128/208），红通道要到进度约 1.99 才撞上限，
    // 远高于 Back.Out 的峰值 1.10，所以共享进度不被截断，过冲只提亮而不移动色相。
    private const string OverColorTarget = "#8080d0";

    // 目标色刻意让红通道顶到上限：58 → 246 的退出进度约 1.048，低于 Back.Out 的峰值 1.10，
    // 于是进度在边界停住（红＝255），而不是绕回（无钳位时裸转 byte 会把 263 变成 7）。
    private const string OverSaturateTarget = "#f6e68c";

    // 动画与读数共用同一份常量，避免各写一份字面量后漂移。
    private const double ShiftTarget = 220d;
    private const double WidthTarget = 220d;
    private const int BackDurationMs = 900;
    private const int ElasticDurationMs = 1100;

    /// <summary>
    /// 加载那三条动画的行程。
    /// </summary>
    /// <remarks>
    /// 行程原来按窗口宽度定（500 / 400），那是在三块目标直接摊在整页上的时候。现在每一块在列表的一行里，台子
    /// 就是它的边界，跑出去会被裁掉 —— 而"跑出去看不见"与"没在跑"在屏幕上分不开。所以行程缩到台子里放得下：
    /// 36 的起边 + 200 的行程 + 60 的方块 = 296，台子给 300。
    /// </remarks>
    private const double LoadTravel = 200d;

    // ---------------------------------------------------------------
    // 每一行元素的场地宽度
    //
    // 台子的宽由那一条案例的行程决定：这是"这一段动哪儿"的唯一来源，窄一点就会让元素在最该被看见的那一瞬跑出
    // 边界。台子的高是统一的（SamplerBench.StageHeight），行高也是。
    // ---------------------------------------------------------------

    /// <summary>加载那三行的元素区宽度：36 的起边、200 的行程与 60 的方块都落在里面。</summary>
    private const double LoadStageWidth = 300d;

    /// <summary>
    /// 位移那两条的元素区宽度：端点 220 外加上方块自己的 60 —— Elastic 的峰值是目标的 1.373 倍（302），
    /// 不留出来它一跑就整块滑出格子，看上去和"没动"一模一样。
    /// </summary>
    private const double ShiftStageWidth = 420d;

    /// <summary>尺寸那一条的元素区宽度：宽度从 60 长到 220，Elastic 峰值处（约 302）也还留在台子里。</summary>
    private const double SizeStageWidth = 320d;

    /// <summary>颜色与饱和那两条不改变尺寸，元素区就是方块本身加一点边。</summary>
    private const double BodyStageWidth = 96d;

    // ---------------------------------------------------------------
    // 元素
    // ---------------------------------------------------------------

    private BoxModel Box0 { get; } = new() { Width = SamplerBench.BodyWidth, Height = SamplerBench.BodyHeight, Color = Box0Color };

    private BoxModel Box1 { get; } = new() { Width = SamplerBench.BodyWidth, Height = SamplerBench.BodyHeight, Color = Box1Color };

    private BoxModel Box2 { get; } = new() { Width = SamplerBench.BodyWidth, Height = SamplerBench.BodyHeight, Color = Box2Color };

    private BoxModel Over0 { get; } = new() { Width = SamplerBench.BodyWidth, Height = SamplerBench.BodyHeight, Color = OverStartColor };

    /// <summary>
    /// 位移那第二条曲线（Elastic）自己的一块目标。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Over0"/> **不能**是同一块：一个元素只能有一个父级，而每一行各自是一个父级。
    /// </remarks>
    private BoxModel Over4 { get; } = new() { Width = SamplerBench.BodyWidth, Height = SamplerBench.BodyHeight, Color = OverStartColor };

    private BoxModel Over1 { get; } = new() { Width = SamplerBench.BodyWidth, Height = SamplerBench.BodyHeight, Color = OverStartColor };

    private BoxModel Over2 { get; } = new() { Width = SamplerBench.BodyWidth, Height = SamplerBench.BodyHeight, Color = OverStartColor };

    private BoxModel Over3 { get; } = new() { Width = SamplerBench.BodyWidth, Height = SamplerBench.BodyHeight, Color = OverStartColor };

    /// <summary>过冲那五块目标，顺序即载荷里 t0…t4 的编号顺序。</summary>
    private BoxModel[] OvershootTargets => [Over0, Over1, Over2, Over3, Over4];

    /// <summary>加载那三块目标。</summary>
    private BoxModel[] LoadTargets => [Box0, Box1, Box2];

    // ---------------------------------------------------------------
    // 加载：那三条动画
    //
    // 上面每一条用到的缓动返回值都落在 [0,1] 内，所以没有一条能越过目标再回来。行程按**这一行自己的台子**定，
    // 不是按页面宽度定的：台子就是它的边界，跑出行外会被裁掉。验收侧只钉静止态（CreateReset），行程是观感，
    // 不是契约。
    // ---------------------------------------------------------------

    // Animation0: simple animation — translate + color + opacity, auto reverse loop
    private static readonly Transition<BoxModel> Animation0 =
        Transition<BoxModel>.Create()
            .Property(b => b.X, LoadTravel)
            .Property(b => b.Color, "#ff7043")
            .Property(b => b.Opacity, 0.2)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Sine.InOut,
            });

    // Animation1: delayed animation — rotate a whole turn while scaling up.
    // CSS 的 rotate/scale 都以元素自己的中心为轴（见 BoxModel.Style 里那串 transform），所以方块是原地转大的；
    // 台子按它的最大外接尺寸给高（见 SamplerBench.StageHeight），转到 45° 附近也不会被裁掉一个角。
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

    // Animation2: combined animation — move right first, then recolor + shrink after a 3s wait
    private static readonly Transition<BoxModel> Animation2 =
        Transition<BoxModel>.Create()
            .Property(b => b.X, LoadTravel - 40)
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

    // -------------------------------------------------------------------
    // 过冲（Overshoot）
    //
    // 上面三条动画的缓动全部落在 [0,1] 内，越不过目标值；Back.Out 峰值 1.100、Elastic.Out 1.373，只有它们会
    // 冲过目标再回弹。位移的两条曲线各有自己一行、同时跑就是同一次对比；颜色与尺寸各一行。读数必须由定时器
    // 采样目标的真实属性：流水线每段都会 Clone() effect，订阅在原始 effect 上的处理函数不会触发。
    // -------------------------------------------------------------------

    // 位移：同一个端点上两条不同缓动，用于对比过冲幅度（峰值 Back 1.100、Elastic 1.373）
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

    // 案例列表里每一行的元素与动作。列表在 RebuildCaseList 里拼成，渲染在 Home.razor 里。
    private readonly List<CaseRow> _caseRows = [];

    // -----------------------------------------------------------------------------------------------
    // 案例列表
    //
    // 一张统一的表：一条案例一行，行里有元素、有一句"这条在验什么"、有这一行自己的三个动作。行的种类只有
    // 三种 —— 加载、过冲、采样器 —— 但对读表的人来说它们长得一样，区别只在各自的令牌、描述与动作里。
    // 三种行的元素都**不再**声明在标记里：元素属于行，而行的顺序与数量由表决定。
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// 把三种案例行拼成一张表：先加载那三行，再过冲那五行，最后每个采样器一行。
    /// </summary>
    /// <remarks>
    /// 采样器排在最后是有意的：它们是套件逐条驱动的对象，也是最需要滚动的部分，而滚动这一步本身要被真的走到。
    /// </remarks>
    private void RebuildCaseList()
    {
        if (_caseRows.Count > 0) return;

        _caseRows.Add(SamplerBench.LoadRow("translate", "平移",
            "嵌套属性路径：动画直接写 BoxModel.X —— 普通属性，PropertyChanged 驱动整页重渲染。",
            Box0, LoadStageWidth, () => Animation0.Execute(Box0), () => ResetLoadRow(Box0, Box0Color)));

        _caseRows.Add(SamplerBench.LoadRow("rotate", "旋转",
            "整个变换被重画：旋转与缩放两个属性同时外推，CSS transform 把二者合成到同一个元素上。",
            Box1, LoadStageWidth, () => Animation1.Execute(Box1), () => ResetLoadRow(Box1, Box1Color)));

        _caseRows.Add(SamplerBench.LoadRow("combine", "平移 + 缩放",
            "两段 effect 串在一条线上：先移动，等 3 秒再变色 + 缩小。",
            Box2, LoadStageWidth, () => Animation2.Execute(Box2), () => ResetLoadRow(Box2, Box2Color)));

        _caseRows.Add(SamplerBench.OvershootRow("shift-back", "位移 Back.Out",
            "共享进度的标量过冲：Back 越过目标 10% 再落回来，数字看得出来、眼睛看不出来。",
            Over0, ShiftStageWidth, () => RunOvershoot(Over0, OverScalarBack, "back", BackDurationMs, 0, RestoreShiftStart), RestoreShiftStart));

        _caseRows.Add(SamplerBench.OvershootRow("shift-elastic", "位移 Elastic.Out",
            "上面那一行的另一条曲线：Elastic 峰值更高、回弹次数更多，两行同时跑就是同一次对比。",
            Over4, ShiftStageWidth, () => RunOvershoot(Over4, OverScalarElastic, "elastic", ElasticDurationMs, 4, RestoreElasticStart), RestoreElasticStart));

        _caseRows.Add(SamplerBench.OvershootRow("color", "颜色过冲",
            "四通道按颜色的规则走：共用进度撞到 255 就整组停住，所以动的是亮度而不是色相。",
            Over1, BodyStageWidth, () => RunOvershoot(Over1, OverColor, "color", BackDurationMs, 1, RestoreColorStart), RestoreColorStart));

        _caseRows.Add(SamplerBench.OvershootRow("size", "尺寸过冲",
            "宽度是 double，没有上下限：外推照走，只是这一条只会变大，碰不到负的那一端。",
            Over2, SizeStageWidth, () => RunOvershoot(Over2, OverSize, "size", ElasticDurationMs, 2, RestoreSizeStart), RestoreSizeStart));

        _caseRows.Add(SamplerBench.OvershootRow("brush", "颜色饱和",
            "非纯色画刷走交叉淡出：混合系数是个分数，两端各自饱和，不跟着缓动越过端点。",
            Over3, BodyStageWidth, () => RunOvershoot(Over3, OverSaturate, "brush", BackDurationMs, 3, RestoreSaturateStart), RestoreSaturateStart));

        // 采样器行由探针表生成。行里的"启动"就是验收套件点的那个把手令牌，点它写闭式解载荷并起那条真动画 ——
        // 所以加一条采样器仍然只需要改 SamplerProbe 一处。
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            var name = sampler;
            _watches[name] = new SamplerProbe.LiveWatch();
            _batchFrames[name] = string.Empty;

            _caseRows.Add(SamplerBench.SamplerRow(
                name,
                () => RunSamplerProbe(name),
                () => StopSamplerRows(),
                () => ResetSamplerRows(),
                // 批量路径：只起这一条自己的动画，不碰别的行，也不写逐行载荷 ——
                // 十几条各自写 over.live 只会互相覆盖，并发那一路走的是 over.batch。
                () =>
                {
                    Transition.Exit(_benchTarget, IncludeMutual: true, IncludeNoMutual: true);

                    try
                    {
                        StartSamplerAnimation(name);
                    }
                    catch (Exception exception)
                    {
                        // 记在**这一行自己的**观察里，所以十几条并发时认得出是谁起不来。
                        _watches[name].Fail($"{exception.GetType().Name}: {exception.Message}");
                    }
                }));
        }
    }

    /// <summary>一条加载行的"重置"：停下它，再把声明的那份静止态同步写回去。</summary>
    private static void ResetLoadRow(BoxModel element, string color)
    {
        Transition.Exit(element, IncludeMutual: true, IncludeNoMutual: true);
        CreateReset(color).Effect(TransitionEffects.Empty).Execute(element);
    }

    /// <summary>
    /// 跑一条过冲案例：先把目标放回起点，再起动画。
    /// </summary>
    /// <remarks>
    /// 先放回起点是必须的：<c>Prepare</c> 读的是**目标此刻的值**当起点，不放回去就变成"从终点动到终点"，
    /// 看上去什么都没发生。
    /// </remarks>
    private void RunOvershoot(
        BoxModel target, Transition<BoxModel> animation, string scenario, int durationMs, int targetIndex, Action restoreStart)
    {
        Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        restoreStart();
        BeginScenario(scenario, durationMs, targetIndex);
        animation.Execute(target);
    }

    // ---- 每一行"重置"的目标状态：就是这些元素声明时的那份值 ----

    private void RestoreShiftStart() => Over0.X = 0;

    private void RestoreElasticStart() => Over4.X = 0;

    private void RestoreColorStart() => Over1.Color = OverStartColor;

    private void RestoreSizeStart() => Over2.Width = SamplerBench.BodyWidth;

    private void RestoreSaturateStart() => Over3.Color = OverStartColor;

    private void ResetOverShoot()
    {
        foreach (var target in OvershootTargets)
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

        RestoreShiftStart();
        RestoreElasticStart();
        RestoreColorStart();
        RestoreSizeStart();
        RestoreSaturateStart();
    }

    // The load-mode row's declared rest state, expressed as explicit paths and taken from the same constants the boxes
    // are built with. Color is animatable here as well — the Razor adapter registers a sampler for string — and the
    // three boxes start from different colors, so each box needs its own reset rather than one shared instance.
    private static Transition<BoxModel> CreateReset(string color)
    {
        return Transition<BoxModel>.Create()
            .Property(b => b.X, 0)
            .Property(b => b.Y, 0)
            .Property(b => b.Width, SamplerBench.BodyWidth)
            .Property(b => b.Height, SamplerBench.BodyHeight)
            .Property(b => b.Opacity, 1)
            .Property(b => b.Rotate, 0)
            .Property(b => b.Scale, 1)
            .Property(b => b.Color, color);
    }

    // -----------------------------------------------------------------------------------------------
    // 顶栏那三件：整块界面一起启动 / 停止 / 重置
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// 顶栏的"全部启动"：每一行同时起一条真动画。
    /// </summary>
    /// <remarks>
    /// 十几条真 <c>Transition</c> 并发跑，是这个库要经得住的一种真实用法；逐行载荷（over.conf / over.live）在
    /// 这一路里刻意不写 —— 十几条各自写只会互相覆盖，而那些载荷的归属是"点了哪一行"，由行里的"启动"负责。
    /// 要每一行的结果，读 over.batch。
    /// <para>
    /// 走过行自己的动作而不是另写一套：行里那个动作就是这条案例的定义，两处各写一份必然漂移。采样器那一类走的是
    /// <see cref="CaseRow.BulkStart"/> —— 行里那个"启动"会掐掉上一次被观察的那一行，批量这一路谁都不能动别人。
    /// </para>
    /// </remarks>
    private void StartAllCases()
    {
        // 正在被观察的那一行先停掉观察：它的载荷已经发过了，别让它在"全部启动"之后又补发一份属于别人的。
        _benchTimer?.Dispose();
        _benchTimer = null;
        _batchTimer?.Dispose();
        _batchTimer = null;

        var sequence = ++_probeSequence;

        // 闭式解那半是一次性算出来的，不需要动画 —— 十几行一起算完，拼进同一份载荷。
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            _batchFrames[sampler] = SamplerProbe.RunFrames(sampler);
            _watches[sampler].Reset();
        }

        WriteBatch(sequence, done: false);

        // 三种行一起发起 —— 加载三行、过冲五行、采样器若干行，十几条真 Transition 并发跑，
        // 这是这个库要经得住的一种真实用法。
        foreach (var row in _caseRows)
        {
            (row.BulkStart ?? row.Start)();
        }

        StartBatchWatch(sequence);
    }

    /// <summary>
    /// 批量运行期间每一拍喂一次每一行，全部落定之后把这一份载荷收尾。
    /// </summary>
    /// <remarks>
    /// "跑完了"是**每一行都落定**，不是某一拍过去 —— 十几条并发，先跑完的等后跑完的。落定判据仍是那个逐行用的
    /// 稳定窗口（见 <see cref="SamplerProbe.LiveWatch"/>），兜底上限也一样。
    /// </remarks>
    private void StartBatchWatch(long sequence)
    {
        var clock = Stopwatch.StartNew();

        _batchTimer = new System.Threading.Timer(
            _ =>
            {
                if (_disposed) return;

                var allSettled = true;

                foreach (var sampler in SamplerProbe.SamplerNames)
                {
                    var watch = _watches[sampler];
                    var measurement = SamplerProbe.Read(_benchTarget, sampler);
                    watch.Observe(measurement.TypeTag, measurement.Components);

                    if (!watch.Settled) allSettled = false;
                }

                // 采样器那一行的元素就是屏幕上那块 swatch：它跟着目标走，批量这一路也照走。
                _benchColor = _benchTarget.Value;

                if (clock.Elapsed < SamplerProbe.BenchDuration
                    || (!allSettled && clock.Elapsed < SamplerProbe.BenchDuration + SamplerProbe.BenchSettleCap))
                {
                    InvokeAsync(StateHasChanged);
                    return;
                }

                _batchTimer?.Dispose();
                _batchTimer = null;
                WriteBatch(sequence, done: true);
                InvokeAsync(StateHasChanged);
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(16));
    }

    /// <summary>
    /// 写批量载荷：每一行的五帧闭式解，加上每一行的观察摘要。
    /// </summary>
    /// <remarks>
    /// 点那一刻先落一份 <c>done=0</c>（帧已经齐了，观察还没跑完），每一行都落定之后再落 <c>done=1</c>。
    /// 与逐行载荷同一套握手：对方靠序号越过基线、且 done 为真，才认这一份是新的、且是跑完了的。
    /// </remarks>
    private void WriteBatch(long sequence, bool done)
    {
        var payload = new System.Text.StringBuilder(
            $"v=1;seq={sequence};done={(done ? 1 : 0)};rows={SamplerProbe.SamplerNames.Count};");

        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            payload.Append(_batchFrames[sampler]);

            if (done) payload.Append(_watches[sampler].BatchFields(sampler));
        }

        _batchSnapshot = payload.ToString();
    }

    /// <summary>顶栏"停止全部"：整块界面一起停下，否则这个按钮的名字就是假的。原地冻结，不回起点。</summary>
    private void ExitAll()
    {
        foreach (var target in LoadTargets)
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

        foreach (var target in OvershootTargets)
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

        _benchTimer?.Dispose();
        _benchTimer = null;
        _batchTimer?.Dispose();
        _batchTimer = null;

        StopSamplerRows();
    }

    /// <summary>
    /// 顶栏"重置"：整个界面回到各自的起点。
    /// </summary>
    /// <remarks>
    /// 加载、过冲、采样器三类各自走自己那行的重置动作 —— 少一类，"重置"这个名字就与它做的事对不上。
    /// 采样器那一行收在这里也是有意的：按下它之后，没有一行还偏离声明的起点，顶栏那几个数才有意义。
    /// </remarks>
    private void ResetAllCases()
    {
        _benchTimer?.Dispose();
        _benchTimer = null;
        _batchTimer?.Dispose();
        _batchTimer = null;

        ResetLoadRow(Box0, Box0Color);
        ResetLoadRow(Box1, Box1Color);
        ResetLoadRow(Box2, Box2Color);
        ResetOverShoot();

        ResetSamplerRows();
    }

    // -----------------------------------------------------------------------------------------------
    // 加载模式那五个按钮
    // -----------------------------------------------------------------------------------------------

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

    // -----------------------------------------------------------------------------------------------
    // 采样器：演出与批量
    // -----------------------------------------------------------------------------------------------

    /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
    private long _probeSequence;

    /// <summary>演示台的被写对象当前该是什么颜色 —— 就是 StringSampler 最后写出的那个 CSS 颜色。</summary>
    private string _benchColor = "#808080";

    /// <summary>
    /// 演示台常驻的被写目标：真动画写它，那一行的 swatch 每帧读它。
    /// </summary>
    /// <remarks>
    /// 桌面那几侧的目标是在屏控件，浏览器里没有"控件属性"可写，这个对象就是那个位置 ——
    /// 它和 <c>over.bench</c> 上真正生效的计算样式之间只隔一个 <c>_benchColor</c> 赋值。
    /// </remarks>
    private readonly SamplerProbe.Target _benchTarget = new();

    // 只留一支演出用的定时器：连点两个把手时，后一次要能叫停前一次，否则两条采样器会同时往各自的格子里写。
    private System.Threading.Timer? _benchTimer;

    // 批量运行时每一行的闭式解帧（点那一刻算一次，不随动画走）与那支采样定时器。
    private readonly Dictionary<string, string> _batchFrames = new(StringComparer.Ordinal);
    private System.Threading.Timer? _batchTimer;

    // 每一条采样器**各有一份**采样累积：逐行路径只用被点的那一份，批量路径十几份同时喂。
    // 异常也记在各自那一份里，所以十几条并发时谁的错是认得出的。
    private readonly Dictionary<string, SamplerProbe.LiveWatch> _watches = new(StringComparer.Ordinal);

    /// <summary>上一拍每一行的值，用来判断"这一拍还在不在变"。按键是采样器名。</summary>
    private readonly Dictionary<string, double[]> _rowPrevious = new(StringComparer.Ordinal);

    /// <summary>
    /// 这一行的"启动"：验五个固定缓动时间，再起一条真动画把这一条跑一遍。
    /// </summary>
    /// <remarks>
    /// 采样器写在**这一格的在屏元素**上、载荷也从它读回，所以下面两件事是同一件事的两种读法。
    /// 这一条同时是套件点的那个把手令牌走的路径，也是行里"启动"按钮走的路径 —— 两者不可能各说各话。
    /// </remarks>
    private void RunSamplerProbe(string sampler)
    {
        // 验：五个固定的缓动时间各跑一帧，写进载荷。
        _confSnapshot = SamplerProbe.Run(sampler, ++_probeSequence);

        // 跑：用真的 Transition 流水线把这条采样器跑一遍 —— 屏幕上看到的就是它 —— 全程从目标属性采样，
        // 结果写进 over.live。验收要看的正是这条路径：目标在一段真动画里每一刻持有的数据是否合法。
        PlaySampler(sampler, _probeSequence);

        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 起一条真动画把某个案例从起点跑到终点。
    /// </summary>
    /// <remarks>
    /// 起点必须先同步写回目标：<c>Prepare</c> 读的是**目标此刻的值**当起点，不写回去就变成"从终点动到终点"，
    /// 屏幕上什么都不会发生。属性路径与采样器都取自同一张探针表。
    /// </remarks>
    private void StartSamplerAnimation(string sampler)
    {
        var property = SamplerProbe.Property(sampler);
        property.SetValue(_benchTarget, SamplerProbe.Start(sampler));

        // Effect 的其余默认值正是这里要的：FPS 60、不自动反向、只跑一趟 —— 于是末帧精确落在终点。
        var animation = Transition<SamplerProbe.Target>.Create()
            .Effect(new TransitionEffect { Duration = SamplerProbe.BenchDuration, Ease = Eases.Back.Out });

        animation.GetState().SetValue(property, SamplerProbe.End(sampler));
        animation.GetState().SetInterpolator(property, SamplerProbe.Create(sampler));

        animation.Execute(_benchTarget);
    }

    /// <summary>
    /// 演出：用真的 <c>Transition</c> 把一条采样器从起点跑到终点，全程对目标属性采样，最后写进 <c>over.live</c>。
    /// </summary>
    /// <remarks>
    /// Razor 适配器只有 StringSampler，产物就是 CSS 颜色字符串，所以目标直接把它当背景色用 ——
    /// 这里不需要 WPF 那边的标尺：颜色本来就没有"行程"可言。
    /// <para>
    /// 末帧是排队投递的，所以落定判据是"值连续几拍不再变"而不是一个固定的余量 —— 负载重的机器上固定余量会读早，
    /// 把一次正常的动画报成"没跑到终点"。
    /// </para>
    /// </remarks>
    private void PlaySampler(string sampler, long sequence)
    {
        _benchTimer?.Dispose();
        _benchTimer = null;
        Transition.Exit(_benchTarget, IncludeMutual: true, IncludeNoMutual: true);

        var watch = _watches[sampler];
        watch.Reset();

        try
        {
            StartSamplerAnimation(sampler);
        }
        catch (Exception exception)
        {
            // 这里同步抛出的都是"这条路径根本起不来"（例如声明的路径不可采样）。照实报给验收，
            // 而不是让一次点击把电路打断。
            watch.Fail($"{exception.GetType().Name}: {exception.Message}");
        }

        // 屏幕上那块 swatch 从起点色开始跟着走：点击那一刻先写一次，动画期间每一拍再跟着目标走。
        _benchColor = _benchTarget.Value;

        // 点击那一刻先落一份 seq，验收侧靠它把"新的"与"上一次剩下的"分开。
        _liveSnapshot = SamplerProbe.LiveWatch.Pending(sampler, sequence);

        var clock = Stopwatch.StartNew();

        _benchTimer = new System.Threading.Timer(
            _ =>
            {
                if (_disposed) return;

                // 背景色永远跟着目标走：屏幕上看到的就是采样器最后写进目标的那一支。
                _benchColor = _benchTarget.Value;
                var measurement = SamplerProbe.Read(_benchTarget, sampler);
                watch.Observe(measurement.TypeTag, measurement.Components);

                if (clock.Elapsed >= SamplerProbe.BenchDuration
                    && (watch.Settled || clock.Elapsed >= SamplerProbe.BenchDuration + SamplerProbe.BenchSettleCap))
                {
                    // 落定了，或者等够了：报出去。等够还没落定本身就是一条要被看见的异常。
                    _liveSnapshot = watch.Digest(sampler, sequence);
                    _benchTimer?.Dispose();
                    _benchTimer = null;
                }

                InvokeAsync(StateHasChanged);
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(16));
    }

    /// <summary>停下每一行。原地冻结，与库的退出语义一致。</summary>
    private void StopSamplerRows()
    {
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            Transition.Exit(_benchTarget, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    /// <summary>
    /// 把每一行停回它声明的起点。
    /// </summary>
    /// <remarks>
    /// 进界面时也走这一条：不这么做的话，静息时那一行持有的是属性的默认值（空字符串），不是采样器声明的起点，
    /// 于是"偏离起点"的行数在什么都没跑的时候就是满的 —— 顶栏那个数会变成一句假命题。
    /// </remarks>
    private void ResetSamplerRows()
    {
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            Transition.Exit(_benchTarget, IncludeMutual: true, IncludeNoMutual: true);
            SamplerProbe.Property(sampler).SetValue(_benchTarget, SamplerProbe.Start(sampler));
        }

        _benchColor = _benchTarget.Value;
    }

    // 读数：显示目标的真实当前值与目标值，过冲只有靠数字才看得出来
    private string BuildReadout() =>
        $"位移 Back   当前 {Over0.X,7:F1}"
        + $"   |   Elastic   当前 {Over4.X,7:F1}"
        + $"   |   目标 {ShiftTarget,6:F1}     宽度 目标 {WidthTarget,6:F1}   当前 {Over2.Width,7:F1}\n"
        + $"颜色 余量    目标 {OverColorTarget}    当前 {Over1.Color}\n"
        + $"颜色 饱和    目标 {OverSaturateTarget}    当前 {Over3.Color}";

    // -----------------------------------------------------------------------------------------------
    // 验收观测面
    //
    // 人类读数之外再写一份机器可读的载荷：同一次采样、同一批值，但用固定的 key=value 而不是散文，
    // 测试就不必去解析一份随时可能被重新排版的版式。载荷只报告"每个目标当前/峰值是多少"，至于哪个场景
    // 动哪个目标、起止与时长，由测试侧的 manifest 声明 —— 观测与语义各自只有一处来源。
    // Blazor 的位移目标是 220 而不是参考实现的 300，载荷保留它自己的数字。
    // -----------------------------------------------------------------------------------------------

    private readonly Stopwatch _scenarioClock = new();

    /// <summary>每个过冲目标各自的峰值。第五条是位移那第二条曲线（Elastic）自己的目标。</summary>
    private readonly double[] _targetPeaks = new double[5];

    private string _scenario = "none";
    private int _scenarioDurationMs;
    private long _sequence;

    private System.Threading.Timer? _readoutTimer;
    private volatile bool _disposed;

    // 定时器每拍采一次样，同时产出人类读数与机器可读载荷两份快照：同一次采样、同一批值，
    // 两者不可能互相矛盾，渲染只读这两份快照。
    private string _readoutSnapshot = "按上面任一按钮；读数显示目标的真实属性值与目标值";
    private string _stateSnapshot = "v=1;seq=0";

    // 采样器一致性载荷：点一次把手跑一条、写一次，所以不在定时器里更新。
    private string _confSnapshot = "v=1;seq=0;n=0;";

    /// <summary>真动画的载荷。点击先写一份 done=0，跑完再写 done=1。</summary>
    private string _liveSnapshot = "v=1;seq=0;done=1;";

    /// <summary>
    /// 批量载荷：点一次"全部启动"，每一行的五帧闭式解与这一行的观察摘要都在这一份里。
    /// </summary>
    /// <remarks>
    /// 逐行载荷是"点哪一行写哪一行"的形状，十几行一起跑只会互相覆盖，所以并发这一路另开一份。初值就是一份
    /// 合法的空载荷：套件在点击之前先读它一次，拿那个序号当基线。
    /// </remarks>
    private string _batchSnapshot = $"v=1;seq=0;done=1;rows={SamplerProbe.SamplerNames.Count};";

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
        var elasticX = Over4.X;
        var width = Over2.Width;
        _targetPeaks[0] = Math.Max(_targetPeaks[0], x);
        _targetPeaks[2] = Math.Max(_targetPeaks[2], width);
        _targetPeaks[4] = Math.Max(_targetPeaks[4], elasticX);

        // done 由时长推出，而不是订阅 effect.Completed：流水线每段克隆 effect，订阅在原件上的处理函数不触发。
        // 它是必需的，不能靠"值等于目标"判断结束 —— 两条曲线都会中途再次穿过目标（Elastic 在 1.1s 内穿越七次）。
        var elapsed = _scenarioClock.ElapsedMilliseconds;
        var done = _scenario != "none" && elapsed >= _scenarioDurationMs + 50 ? 1 : 0;

        var row = RowState();

        return $"v=1;seq={_sequence};scen={_scenario};t={elapsed};done={done};"
             + $"t0.cur={x:F3};t0.peak={Peak(0)};"
             + $"t1.cur={Describe(Over1.Color)};"
             + $"t2.cur={width:F3};t2.peak={Peak(2)};"
             + $"t3.cur={Describe(Over3.Color)};"
             + $"t4.cur={elasticX:F3};t4.peak={Peak(4)};"
             + RecState("r0", Box0) + RecState("r1", Box1) + RecState("r2", Box2)
             // rows/away/moving 排在 nomutual **之前**：后者是加载模式那半必须读到的最后一个字段，
             // 所以它排在最后，载荷再长也不会被它挡住。
             + $"rows={row.Rows};away={row.Away};moving={row.Moving};"
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
    /// 行数 / 偏离声明起点的行数 / 相对上一拍仍在变的行数。
    /// </summary>
    /// <remarks>
    /// 顶栏那三个按钮唯一的可观测量，与加载模式那边的 <c>nomutual</c> 同一个思路：它不解释动画该到哪，
    /// 只说明有没有在动、有没有回到起点。只数采样器那一行 —— 别的行的"起点"由各自的台子宣布，不在这里说话。
    /// <para>
    /// <b>两个数得分开，缺一个就有假命题。</b> 只看"偏离起点"，静息时也是满的 —— 那一行初始持有的是属性的默认值
    /// （空字符串，连颜色都不是），不是采样器声明的起点，于是"全部启动后 &gt; 0"在什么都没跑时也成立。所以进界面
    /// 时先把它写成声明的起点（见 OnAfterRenderAsync），并另记一个"这一拍还在变"：静息 0 / 启动后满 /
    /// 停止后 0 / 重置后 0 且回到起点。
    /// </para>
    /// <para>
    /// <b>两个数都是"这一拍"的快照，不是"这一批跑起来了"的判据。</b> 一行刚起动画的头几帧里，它的分量可能与
    /// 声明的起点**逐位相同**（字符串采样器在 t=0 原样写回起点那一串），所以要问"这一批起来了没有"，得**等**
    /// <c>away</c> 追上该有的行数，不能拿某一拍去断言（见验收侧的 ScanToolbar）。
    /// </para>
    /// </remarks>
    private (int Rows, int Away, int Moving) RowState()
    {
        var away = 0;
        var moving = 0;

        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            var now = SamplerProbe.Read(_benchTarget, sampler);

            if (!SamplerProbe.MatchesStart(_benchTarget, sampler)) away++;

            if (_rowPrevious.TryGetValue(sampler, out var before)
                && !SamplerProbe.SameComponents(before, now.Components))
            {
                moving++;
            }

            _rowPrevious[sampler] = now.Components;
        }

        return (SamplerProbe.SamplerNames.Count, away, moving);
    }

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

        foreach (var target in LoadTargets)
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

    protected override void OnInitialized()
    {
        // Blazor animation targets POCO ViewModels with no dispatcher affinity, so a background
        // thread cannot infer the circuit context. It must be captured here (OnInitialized, on the
        // circuit thread). This is an inherent limitation of the Blazor model.
        UIThreadInspector.CaptureUIThread();

        RebuildCaseList();

        // Subscribe to property changes to drive Blazor re-rendering. 加载与过冲那八块目标全在这里 ——
        // 列表里每一行的元素都靠它重画，少一块那一行在屏幕上就是死的。
        foreach (var box in LoadTargets)
            box.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);

        foreach (var box in OvershootTargets)
            box.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return Task.CompletedTask;

        // 采样器那一行先写成它自己声明的起点：静息时"偏离起点"与"还在动"才都是 0。
        ResetSamplerRows();

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

    public void Dispose()
    {
        // 页面销毁：停掉每一支定时器并终止每一块目标上的动画（否则回路断开后它们还在写属性）。
        _disposed = true;

        _readoutTimer?.Dispose();
        _readoutTimer = null;
        _benchTimer?.Dispose();
        _benchTimer = null;
        _batchTimer?.Dispose();
        _batchTimer = null;

        foreach (var target in LoadTargets)
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

        foreach (var target in OvershootTargets)
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

        StopSamplerRows();
    }
}
