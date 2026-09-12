using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace Demo.Views;

/// <summary>
/// Standalone animation test for the VeloxDev.Avalonia PlatformAdapters (TransitionSystem).
/// </summary>
/// <remarks>
/// 版面与其余几个平台的 Transition demo 对齐（见 <c>Views\MainWindow.axaml</c>）：顶栏（全局三件 + 五种加载方式
/// + 载荷读数）压在上面，下面是一张**可滚动的案例列表** —— 一条案例一行，行里是那条案例真正在动的元素、
/// 一句"这条在验什么"，以及这一行自己的 启动 / 关闭 / 重置。
/// <para>
/// 三种行（加载 3 条、过冲 5 条、每条采样器 1 条）长得一样，区别只在各自的令牌、描述与动作里，三条合成一处的
/// 定义在 <see cref="RebuildCaseList"/>。元素由代码造、由行带进列表：一个元素只能有一个父级，而每一行各自是
/// 一个父级 —— 位移那两条曲线因此各有一块自己的目标。
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>进界面时把每一行写成它自己声明的起点，且只做一次。</summary>
    private bool _seeded;

    public MainWindow()
    {
        InitializeComponent();

        // 旋转与放大的中心放在元素自己身上：行里的台子是裁边的，绕左上角转 180° 会把方块整个甩到台子外，
        // 而"跑出去看不见"与"没在跑"在屏幕上分不开 —— 这正是每一行都要装得下自己那段行程的意思。
        // 台子里的三个元素只有这两个带旋转/缩放，所以只有它们需要这一句。
        _rec1.RenderTransformOrigin = new RelativePoint(0.5d, 0.5d, RelativeUnit.Relative);
        _rec2.RenderTransformOrigin = new RelativePoint(0.5d, 0.5d, RelativeUnit.Relative);

        RebuildCaseList();
        CaseRows.Children.Add(SamplerBench.Build(_caseRows));

        // "重置"是整块界面的重置，所以它收下三类行各自的重置动作 —— 少一类，这个名字就与它做的事对不上。
        btnReset.Click += (_, _) => ResetAllCases();

        // 读数用定时器采样目标属性，而不是订阅 effect 的事件——流水线每段都会 Clone() effect，
        // 在这里订阅的处理函数不是真正触发的那个。
        var readout = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        readout.Tick += (_, _) => UpdateReadout();
        readout.Start();

        // 每一行先写成它自己声明的起点。不这么做的话，静息时每行持有的是控件的默认值（null 画刷、默认圆角），
        // 都不是采样器声明的起点，于是"偏离起点"的行数在什么都没跑的时候就是满的 —— 顶栏那个数会变成一句假命题。
        Loaded += (_, _) =>
        {
            if (_seeded) return;
            _seeded = true;
            ResetSamplerRows();
        };
    }

    // -----------------------------------------------------------------------------------------------
    // 案例列表
    //
    // 一张统一的表：一条案例一行，行里有元素、有一句"这条在验什么"、有这一行自己的三个动作。行的种类只有
    // 三种 —— 加载、过冲、采样器 —— 但对读表的人来说它们长得一样，区别只在各自的令牌、描述与动作里。
    // -----------------------------------------------------------------------------------------------

    /// <summary>列表里的每一行，按顺序 —— 顶栏那个"全部启动"走的就是这里的动作。</summary>
    private readonly List<CaseRow> _caseRows = [];

    private readonly SamplerBench _bench = new();

    /// <summary>
    /// 把三种案例行拼成一张表：先加载那三行，再过冲那五行，最后每个采样器一行。
    /// </summary>
    /// <remarks>
    /// 采样器排在最后是有意的：它们是套件逐条驱动的对象，也是最需要滚动的部分，而滚动这一步本身要被真的走到。
    /// </remarks>
    private void RebuildCaseList()
    {
        _caseRows.Clear();

        _caseRows.Add(LoadRow("translate", "平移",
            "嵌套属性路径：动画写的是 RenderTransform.X —— 子对象上的成员，而且是被就地改的那一个。",
            _rec0, () => Animation0.Execute(_rec0), RestoreTranslateRow));

        _caseRows.Add(LoadRow("rotate", "旋转",
            "整个变换对象被换掉：端点是另一个同类型的 RotateTransform，逐字段外推而不是逐分量改。",
            _rec1, () => Animation1.Execute(_rec1), RestoreRotateRow));

        _caseRows.Add(LoadRow("combine", "平移 + 缩放",
            "两个变换合成一个 TransformGroup，按类型配对插值；第二段还会转去动颜色。",
            _rec2, () => Animation2.Execute(_rec2), RestoreCombineRow));

        _caseRows.Add(OvershootRow("shift-back", "位移 Back.Out",
            "共享进度的标量过冲：Back 越过目标 10% 再落回来，数字看得出来、眼睛看不出来。",
            _over0, ShiftStageWidth, () => RunOvershoot(_over0, OverScalarBack, "back", BackDurationMs, 0, RestoreShiftStart), RestoreShiftStart));

        _caseRows.Add(OvershootRow("shift-elastic", "位移 Elastic.Out",
            "上面那一行的另一条曲线：Elastic 峰值更高、回弹次数更多，两行同时跑就是同一次对比。",
            _over4, ShiftStageWidth, () => RunOvershoot(_over4, OverScalarElastic, "elastic", ElasticDurationMs, 4, RestoreElasticStart), RestoreElasticStart));

        _caseRows.Add(OvershootRow("color", "颜色过冲",
            "四通道按颜色的规则走：共用进度撞到 255 就整组停住，所以动的是亮度而不是色相。",
            _over1, BodyStageWidth, () => RunOvershoot(_over1, OverColor, "color", BackDurationMs, 1, RestoreColorStart), RestoreColorStart));

        _caseRows.Add(OvershootRow("size", "尺寸过冲",
            "宽度是 double，没有上下限：外推照走，只是这一条只会变大，碰不到负的那一端。",
            _over2, SizeStageWidth, () => RunOvershoot(_over2, OverSize, "size", ElasticDurationMs, 2, RestoreSizeStart), RestoreSizeStart));

        _caseRows.Add(OvershootRow("brush", "渐变过冲",
            "非纯色画刷走交叉淡出：混合系数是个分数，两端各自饱和，不跟着缓动越过端点。",
            _over3, BodyStageWidth, () => RunOvershoot(_over3, OverBrush, "brush", ElasticDurationMs, 3, RestoreGradientStart), RestoreGradientStart));

        // 采样器行由探针表生成。行里的"启动"就是验收套件点的那个把手令牌，点它写闭式解载荷并起那条真动画 ——
        // 所以加一条采样器仍然只需要改 SamplerProbe 一处。
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            var name = sampler;
            _watches[name] = new SamplerProbe.LiveWatch();
            _batchFrames[name] = string.Empty;

            _caseRows.Add(_bench.SamplerRow(
                name,
                () => RunSamplerProbe(name),
                () => StopSamplerRows(name),
                () => ResetSamplerRows(name),
                // 批量路径：只起这一条自己的动画，不碰别的行，也不写逐行载荷 ——
                // 十几条各自写 over.live 只会互相覆盖，并发那一路走的是 over.batch。
                () =>
                {
                    var subject = _bench.SubjectFor(name);
                    Transition.Exit(subject, IncludeMutual: true, IncludeNoMutual: true);

                    try
                    {
                        StartSamplerAnimation(name, subject);
                    }
                    catch (Exception exception)
                    {
                        // 记在**这一行自己的**观察里，所以十几条并发时认得出是谁起不来。
                        _watches[name].Fail($"{exception.GetType().Name}: {exception.Message}");
                    }
                }));
        }
    }

    /// <summary>
    /// 一条加载案例：元素就是那块被三个动画之一驱动的方块。
    /// </summary>
    private static CaseRow LoadRow(
        string id, string title, string description, Rectangle element, Action start, Action restore)
        => new(
            title,
            description,
            PlayStage(element, LoadStageWidth),
            LoadStageWidth,
            $"over.row.start.{id}",
            $"over.row.stop.{id}",
            $"over.row.reset.{id}",
            start,
            () => Transition.Exit(element, IncludeMutual: true, IncludeNoMutual: true),
            () => ResetCase(element, restore));

    /// <summary>
    /// 一条过冲案例：元素是那块目标自己。
    /// </summary>
    /// <param name="stageWidth">
    /// 元素区的宽度。位移那两条要放得下整段行程（端点是 230，方块本身 80），不按原值留出位置的话，
    /// 它一跑就整块滑出格子 —— 看上去和"没动"一模一样，正是这个演示要消除的错觉。
    /// </param>
    private static CaseRow OvershootRow(
        string id, string title, string description, Rectangle target, double stageWidth,
        Action start, Action restore)
        => new(
            title,
            description,
            PlayStage(target, stageWidth),
            stageWidth,
            $"over.row.start.{id}",
            $"over.row.stop.{id}",
            $"over.row.reset.{id}",
            start,
            () => Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true),
            () => ResetCase(target, restore));

    /// <summary>
    /// 把元素放进一块定宽、裁边的台子里 —— 它的行程跑到端点也不会压到旁边的字上。
    /// </summary>
    /// <remarks>
    /// 台子的宽由那一条案例的行程决定（见各条案例的 <c>*StageWidth</c>）：这是"这一段动哪儿"的唯一来源，
    /// 窄一点就会让元素在最该被看见的那一瞬跑出边界 —— 而"跑出去看不见"和"没在跑"在屏幕上分不开。
    /// </remarks>
    private static Canvas PlayStage(Control element, double width)
    {
        var stage = SamplerBench.Stage(width);
        Canvas.SetLeft(element, SamplerBench.ElementInset);
        Canvas.SetTop(element, SamplerBench.ElementTop);
        stage.Children.Add(element);
        return stage;
    }

    /// <summary>停掉并把它放回声明的静止态。</summary>
    private static void ResetCase(Rectangle element, Action restore)
    {
        Transition.Exit(element, IncludeMutual: true, IncludeNoMutual: true);
        restore();
    }

    // -----------------------------------------------------------------------------------------------
    // 案例列表里那几块元素
    //
    // 三块加载目标与五块过冲目标以前声明在 XAML 里、摊在窗口的格子上；现在每一块是列表里的一行，所以由代码造、
    // 由行带进列表。顺带一个好处：Bs1 只在这里定义一次 —— 以前 XAML 里一份、代码里一份，两处得手动保持一致，
    // 而这个仓库里已经为"两处漂移"踩过一次。
    // -----------------------------------------------------------------------------------------------

    private readonly Rectangle _rec0 = new()
    {
        Fill = new SolidColorBrush(Colors.Cyan), Width = 80, Height = 60, RenderTransform = new TranslateTransform(),
    };

    private readonly Rectangle _rec1 = new() { Fill = new SolidColorBrush(Colors.Lime), Width = 80, Height = 60 };

    private readonly Rectangle _rec2 = new() { Fill = CreateBs1Brush(), Width = 80, Height = 60 };

    // 位移那两条就地驱动 RenderTransform.X，所以变换在这里造一次。
    private readonly Rectangle _over0 = new()
    {
        Fill = new SolidColorBrush(OverColorStart), Width = 80, Height = 60, RenderTransform = new TranslateTransform(),
    };

    /// <summary>
    /// 位移那第二条曲线（Elastic）自己的一块目标。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="_over0"/> **不能**是同一块：一个元素只能有一个父级，而每一行各自是一个父级。
    /// 所以"两条曲线同屏对比"改成"上下相邻两行同时看得见" —— 同一条时间轴上，仍然是同一次对比。
    /// </remarks>
    private readonly Rectangle _over4 = new()
    {
        Fill = new SolidColorBrush(OverColorStart), Width = 80, Height = 60, RenderTransform = new TranslateTransform(),
    };

    private readonly Rectangle _over1 = new()
    {
        Fill = new SolidColorBrush(OverColorStart), Width = 80, Height = 60,
    };

    private readonly Rectangle _over2 = new()
    {
        Fill = new SolidColorBrush(Color.FromRgb(0x7A, 0x5A, 0xA5)), Width = 80, Height = 60,
    };

    private readonly Rectangle _over3 = new() { Fill = CreateBs1Brush(), Width = 80, Height = 60 };

    // -----------------------------------------------------------------------------------------------
    // 每一行元素的场地
    //
    // 行高是统一的（SamplerBench.RowHeight），所以每一行都装得下自己那段行程 —— 装不下就等于元素跑没了。
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// 位移那两条的元素区宽度：端点 230 外加上方块自己的 80，不留出来它一跑就整块滑出格子。
    /// </summary>
    /// <remarks>
    /// 目标定 230 而不是 300：Elastic 的峰值是目标的 1.37 倍，300 的峰值（411）连着方块一起要 499，
    /// 台子得给到 500 才不裁 —— 那种台子会把这一行的元素框撑得比别的行宽一倍。行程收进台子里更划算：
    /// 验收侧只钉静止态，行程是观感，不是契约。
    /// </remarks>
    private const double ShiftStageWidth = 420d;

    private const double ShiftTarget = 230d;

    /// <summary>
    /// 加载那三行的元素区宽度，以及那三条动画的行程。
    /// </summary>
    /// <remarks>
    /// 行程原来按窗口宽度定（400），那是在三块目标直接摊在窗口上的时候。现在每一块在列表的一行里，
    /// 台子就是它的边界，跑出去会被裁掉 —— 而"跑出去看不见"与"没在跑"在屏幕上是一样的。
    /// 所以行程缩到台子里放得下（80 宽的方块 + 200 的行程）。
    /// </remarks>
    private const double LoadStageWidth = 300d;

    private const double LoadTravel = 200d;

    /// <summary>尺寸那一条的元素区宽度：宽度从 80 长到 220，Elastic 峰值处（约 272）也还留在台子里。</summary>
    private const double SizeStageWidth = 288d;

    /// <summary>颜色与渐变那两条不改变尺寸，元素区就是方块本身加一点边。</summary>
    private const double BodyStageWidth = 96d;

    // -----------------------------------------------------------------------------------------------
    // 过冲
    //
    // 上面每一条用到的缓动返回值都落在 [0,1] 内，所以没有一条能越过目标再回来。Back 峰值 1.10、
    // Elastic 峰值 1.37。过冲能不能真的看到，取决于目标属性落在哪个采样器上：
    //   - 纯数值（TranslateTransform.X、Width）自由外推，是真正的过冲；
    //   - 颜色共用一个进度、在第一个触到上限的通道处整组停住；
    //   - 非纯色画刷走交叉淡出，系数是个分数，只在两端饱和。
    // 读数是让过冲可见的东西：数字越过目标再回来，肉眼无法把它和一条更慢的缓动区分开。
    // -----------------------------------------------------------------------------------------------

    private const double WidthTarget = 220d;
    private const double WidthStart = 80d;

    // 时长同时喂给 effect 与载荷：载荷靠它推出 done，两处若各写一份就会漂移。
    private const int BackDurationMs = 900;
    private const int ElasticDurationMs = 1100;

    // 颜色场景的起始色：过冲行声明时的那份填充色。起始色只有这一份来源，逐场景重置与整条重置都读它。
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

    // Width 是 double，采样器不给它任何边界：这里没有任何东西阻止它变成负数，而 Width 的 setter 会拒绝负值。
    // 所以这个场景只做放大（80 → 220），弹性曲线也不会跌破起点，非法区间不可达。
    private static readonly Transition<Rectangle> OverSize =
        Transition<Rectangle>.Create()
            .Property(r => r.Width, WidthTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

    // 非纯色 Fill 走的是混合笔刷路径而不是纯色路径：交叉淡化系数在两端饱和，越界的那段不会写出去。
    private static readonly Transition<Rectangle> OverBrush =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, CreateShiftedBs1())
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Back.Out });

    /// <summary>
    /// 跑一条过冲案例：先把目标放回起点，再起动画。
    /// </summary>
    /// <remarks>
    /// 先放回起点是必须的：<c>Prepare</c> 读的是**目标此刻的值**当起点，不放回去就变成"从终点动到终点"，
    /// 看上去什么都没发生。
    /// </remarks>
    private void RunOvershoot(
        Rectangle target, Transition<Rectangle> animation, string scenario, int durationMs, int targetIndex, Action restoreStart)
    {
        Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        restoreStart();
        BeginScenario(scenario, durationMs, targetIndex);
        animation.Execute(target);
    }

    // ---- 每一行"重置"的目标状态：就是这些元素声明时的那份值 ----

    // 加载那三行走的是与全局重置同一条路径：同步套用，不走异步流水线。
    private void RestoreTranslateRow() => ApplyReset(CreateResetRec0(), _rec0);

    // Rec1/Rec2 的 RenderTransform 得**直接赋 null**，不走构建器：适配器的 Transform 重载收的是集合，
    // 传空集合建出来的是一个空的 TransformGroup，不是 null —— 而声明的静止态就是"没有变换"。
    private void RestoreRotateRow()
    {
        _rec1.RenderTransform = null;
        ApplyReset(CreateResetRec1(), _rec1);
    }

    private void RestoreCombineRow()
    {
        _rec2.RenderTransform = null;
        ApplyReset(CreateResetRec2(), _rec2);
    }

    private void RestoreShiftStart() => ((TranslateTransform)_over0.RenderTransform!).X = 0;

    private void RestoreElasticStart() => ((TranslateTransform)_over4.RenderTransform!).X = 0;

    private void RestoreColorStart() => _over1.Fill = new SolidColorBrush(OverColorStart);

    private void RestoreSizeStart() => _over2.Width = WidthStart;

    private void RestoreGradientStart() => _over3.Fill = CreateBs1Brush();

    private void ResetOverShoot()
    {
        foreach (var target in OvershootTargets())
        {
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        }

        RestoreShiftStart();
        RestoreElasticStart();
        RestoreColorStart();
        RestoreSizeStart();
        RestoreGradientStart();
    }

    /// <summary>三类场景共用的八块目标：加载三块 + 过冲五块。采样器行自成一套，不在这里。</summary>
    private Rectangle[] ScenarioTargets() => [_rec0, _rec1, _rec2, _over0, _over1, _over2, _over3, _over4];

    /// <summary>过冲那五块目标。</summary>
    private Rectangle[] OvershootTargets() => [_over0, _over1, _over2, _over3, _over4];

    // -----------------------------------------------------------------------------------------------
    // 采样器：演出与批量
    // -----------------------------------------------------------------------------------------------

    /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
    private long _probeSequence;

    // 只留一支演出用的定时器：连点两个把手时，后一次要能叫停前一次，否则两条采样器会同时往各自的格子里写。
    private DispatcherTimer? _benchTimer;

    // 上一次演出写的那一格：换一格时要把它那条真动画停掉，不然它会在没人看的时候继续往旧格子上写。
    private SamplerSubject? _liveSubject;

    // 每一条采样器**各有一份**采样累积：逐行路径只用被点的那一份，批量路径十几份同时喂。
    // 异常也记在各自那一份里，所以十几条并发时谁的错是认得出的。
    private readonly Dictionary<string, SamplerProbe.LiveWatch> _watches = new(StringComparer.Ordinal);

    // 批量运行时每一行的闭式解帧（点那一刻算一次，不随动画走）与那支采样定时器。
    private readonly Dictionary<string, string> _batchFrames = new(StringComparer.Ordinal);
    private DispatcherTimer? _batchTimer;

    /// <summary>
    /// 这一行的"启动"：验五个固定缓动时间，再起一条真动画把这一条跑一遍。
    /// </summary>
    /// <remarks>
    /// 采样器写在**这一格的在屏控件**上、载荷也从它读回，所以下面两件事是同一件事的两种读法。
    /// 这一条同时是套件点的那个把手令牌走的路径，也是行里"启动"按钮走的路径 —— 两者不可能各说各话。
    /// </remarks>
    private void RunSamplerProbe(string sampler)
    {
        var subject = _bench.SubjectFor(sampler);

        // 验：五个固定的缓动时间各跑一帧，写进载荷。
        OverConf.Text = SamplerProbe.Run(subject, sampler, ++_probeSequence);

        // 跑：用真的 Transition 流水线把这条采样器跑一遍 —— 屏幕上看到的就是它 —— 全程从控件属性采样，
        // 结果写进 over.live。验收要看的正是这条路径：控件在一段真动画里每一刻持有的数据是否合法。
        PlaySampler(sampler, subject, _probeSequence);
    }

    /// <summary>
    /// 起一条真动画把某个案例从起点跑到终点。
    /// </summary>
    /// <remarks>
    /// 起点必须先同步写回目标：<c>Prepare</c> 读的是**目标此刻的值**当起点，不写回去就变成"从终点动到终点"，
    /// 屏幕上什么都不会发生。属性路径与采样器都取自同一张探针表。
    /// </remarks>
    private static void StartSamplerAnimation(string sampler, SamplerSubject subject)
    {
        var property = SamplerProbe.Path(sampler);
        property.SetValue(subject, SamplerProbe.Start(sampler));

        // Effect 的其余默认值正是这里要的：FPS 60、不自动反向、只跑一趟 —— 于是末帧精确落在终点。
        var animation = Transition<SamplerSubject>.Create()
            .Effect(new TransitionEffect { Duration = SamplerProbe.BenchDuration, Ease = Eases.Back.Out });

        animation.GetState().SetValue(property, SamplerProbe.End(sampler));
        animation.GetState().SetInterpolator(property, SamplerProbe.Create(sampler));

        animation.Execute(subject);
    }

    /// <summary>
    /// 顶栏的"全部启动"：每一行同时起一条真动画。
    /// </summary>
    /// <remarks>
    /// 十几条真 <c>Transition</c> 并发跑，是这个库要经得住的一种真实用法；逐行载荷（over.conf / over.live）
    /// 在这一路里刻意不写 —— 十几条各自写只会互相覆盖，而那些载荷的归属是"点了哪一行"，由行里的"启动"负责。
    /// 要每一行的结果，读 over.batch。
    /// </remarks>
    private void StartAllCases(object? sender, RoutedEventArgs e)
    {
        // 正在被观察的那一行先停掉观察：它的载荷已经发过了，别让它在"全部启动"之后又补发一份属于别人的。
        _benchTimer?.Stop();
        _batchTimer?.Stop();
        _liveSubject = null;

        var sequence = ++_probeSequence;

        // 闭式解那半是一次性算出来的，不需要动画 —— 十几行一起算完，拼进同一份载荷。
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            _batchFrames[sampler] = SamplerProbe.RunFrames(_bench.SubjectFor(sampler), sampler);
            _watches[sampler].Reset();
        }

        WriteBatch(sequence, done: false);

        // 三种行一起发起 —— 加载三行、过冲五行、采样器若干行，十几条真 Transition 并发跑。
        //
        // 走过行自己的动作而不是另写一套：行里那个动作就是这条案例的定义，两处各写一份必然漂移。
        // 采样器那一类走的是 BulkStart —— 行里那个"启动"会掐掉上一次被观察的行，批量这一路谁都不能动别人。
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
    /// "跑完了"是**每一行都落定**，不是某一拍过去 —— 十几条并发，先跑完的等后跑完的。落定判据仍是那个
    /// 逐行用的稳定窗口（见 <see cref="SamplerProbe.LiveWatch"/>），兜底上限也一样。
    /// </remarks>
    private void StartBatchWatch(long sequence)
    {
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _batchTimer = timer;

        timer.Tick += (_, _) =>
        {
            var allSettled = true;

            foreach (var sampler in SamplerProbe.SamplerNames)
            {
                var watch = _watches[sampler];
                var measurement = SamplerProbe.Read(_bench.SubjectFor(sampler), sampler);
                watch.Observe(measurement.TypeTag, measurement.Components);

                if (!watch.Settled) allSettled = false;
            }

            if (clock.Elapsed < SamplerProbe.BenchDuration) return;
            if (!allSettled && clock.Elapsed < SamplerProbe.BenchDuration + SamplerProbe.BenchSettleCap) return;

            timer.Stop();
            WriteBatch(sequence, done: true);
        };

        timer.Start();
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
        var payload = new StringBuilder(
            $"v=1;seq={sequence};done={(done ? 1 : 0)};rows={SamplerProbe.SamplerNames.Count};");

        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            payload.Append(_batchFrames[sampler]);

            if (done) payload.Append(_watches[sampler].BatchFields(sampler));
        }

        OverBatch.Text = payload.ToString();
    }

    /// <summary>停下每一行（或指定的一行）。原地冻结，与库的退出语义一致。</summary>
    private void StopSamplerRows(string? only = null)
    {
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            if (only is not null && sampler != only) continue;
            Transition.Exit(_bench.SubjectFor(sampler), IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    /// <summary>把每一行（或指定的一行）停回它声明的起点。</summary>
    private void ResetSamplerRows(string? only = null)
    {
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            if (only is not null && sampler != only) continue;

            var subject = _bench.SubjectFor(sampler);
            Transition.Exit(subject, IncludeMutual: true, IncludeNoMutual: true);
            SamplerProbe.Path(sampler).SetValue(subject, SamplerProbe.Start(sampler));
        }
    }

    /// <summary>
    /// 演出：用真的 <c>Transition</c> 把一条采样器从起点跑到终点，全程对控件属性采样，最后写进 <c>over.live</c>。
    /// </summary>
    /// <remarks>
    /// 这不是原先那个手写循环 —— scheduler、effect、端点归一化、按属性类型解析采样器、每帧往 UI 线程投递，
    /// 走的全是库自己那条路径。
    /// <para>
    /// 起点必须先同步写回目标：<c>Prepare</c> 读的是**目标此刻的值**当起点，不写回去就变成"从终点动到终点"，
    /// 屏幕上什么都不会发生。
    /// </para>
    /// <para>
    /// 末帧是排队投递的，所以落定判据是"值连续几拍不再变"而不是一个固定的余量 —— 负载重的机器上固定余量会读早，
    /// 把一次正常的动画报成"没跑到终点"。
    /// </para>
    /// </remarks>
    private void PlaySampler(string sampler, SamplerSubject subject, long sequence)
    {
        _benchTimer?.Stop();
        if (_liveSubject is not null) Transition.Exit(_liveSubject, IncludeMutual: true, IncludeNoMutual: true);
        _liveSubject = subject;

        // 这一行自己的观察。共享一份认不出是谁的错，所以每行一份。
        var watch = _watches[sampler];
        watch.Reset();

        try
        {
            StartSamplerAnimation(sampler, subject);
        }
        catch (Exception exception)
        {
            // 这里同步抛出的都是"这条路径根本起不来"（例如声明的路径不可采样）。照实报给验收，
            // 而不是让一次点击把 demo 打挂。
            watch.Fail($"{exception.GetType().Name}: {exception.Message}");
        }

        // 点击那一刻先落一份 seq，验收侧靠它把"新的"与"上一次剩下的"分开。
        OverLive.Text = SamplerProbe.LiveWatch.Pending(sampler, sequence);

        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _benchTimer = timer;

        timer.Tick += (_, _) =>
        {
            var measurement = SamplerProbe.Read(subject, sampler);
            watch.Observe(measurement.TypeTag, measurement.Components);

            if (clock.Elapsed < SamplerProbe.BenchDuration) return;
            if (!watch.Settled && clock.Elapsed < SamplerProbe.BenchDuration + SamplerProbe.BenchSettleCap) return;

            // 落定了，或者等够了：报出去。等够还没落定本身就是一条要被看见的异常。
            timer.Stop();
            OverLive.Text = watch.Digest(sampler, sequence);
        };

        timer.Start();
    }

    // -----------------------------------------------------------------------------------------------
    // 顶栏那三件：整块界面一起启动 / 停止 / 重置
    // -----------------------------------------------------------------------------------------------

    /// <summary>顶栏"停止全部"：整块界面一起停下，否则这个按钮的名字就是假的。原地冻结，不回起点。</summary>
    private void ExitAll(object? sender, RoutedEventArgs e)
    {
        foreach (var target in ScenarioTargets())
        {
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        }

        _benchTimer?.Stop();
        _batchTimer?.Stop();
        _liveSubject = null;
        StopSamplerRows();
    }

    /// <summary>
    /// 顶栏"重置"：整个界面回到各自的起点。
    /// </summary>
    /// <remarks>
    /// 加载、过冲、采样器三类各自走自己那行的重置动作 —— 少一类，"重置"这个名字就与它做的事对不上。
    /// </remarks>
    private void ResetAllCases()
    {
        _benchTimer?.Stop();
        _batchTimer?.Stop();
        _liveSubject = null;

        ResetCase(_rec0, RestoreTranslateRow);
        ResetCase(_rec1, RestoreRotateRow);
        ResetCase(_rec2, RestoreCombineRow);
        ResetOverShoot();

        // "重置"是整块界面的重置，所以它结束采样器那些行 —— 这正是顶栏那几个数的意义：
        // 按下它之后，没有一行还偏离声明的起点。
        ResetSamplerRows();
    }

    // -----------------------------------------------------------------------------------------------
    // 验收观测面
    //
    // 人类读数之外再写一份机器可读的载荷：同一支定时器、同一批值，但用固定的 key=value 而不是散文，
    // 测试就不必去解析一份随时可能被重新排版的版式。载荷只报告"每个目标当前/峰值是多少"，至于哪个场景
    // 动哪个目标、起止与时长，由测试侧的 manifest 声明 —— 观测与语义各自只有一处来源。
    // 目标编号与列表同序：t0=位移 Back、t1=颜色、t2=尺寸、t3=渐变、t4=位移 Elastic 的第二块目标。
    // -----------------------------------------------------------------------------------------------

    private readonly Stopwatch _scenarioClock = new();

    /// <summary>每个过冲目标各自的峰值。第五条是位移那第二条曲线（Elastic）自己的目标。</summary>
    private readonly double[] _targetPeaks = new double[5];

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

    // 载荷只取目标的真实属性，不缓存也不伪造。
    private void UpdateReadout() => OverState.Text = BuildState();

    private string BuildState()
    {
        _sequence++;

        // 只有标量目标记录峰值。峰值存在的意义是"刀刃型峰"——采样落在尖峰两侧就会低估它；颜色的过冲是一段
        // 形状而不是一个尖峰，报当前值就够，测试轮询取最大即可。
        var x = (_over0.RenderTransform as TranslateTransform)?.X ?? 0d;
        var elasticX = (_over4.RenderTransform as TranslateTransform)?.X ?? 0d;
        var width = _over2.Width;
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
             + $"t1.cur={Describe(_over1.Fill)};"
             + $"t2.cur={width:F3};t2.peak={Peak(2)};"
             + $"t3.cur={Describe(_over3.Fill)};"
             + $"t4.cur={elasticX:F3};t4.peak={Peak(4)};"
             + RecState("r0", _rec0) + RecState("r1", _rec1) + RecState("r2", _rec2)
             // 时间轴那四个字段同样排在 nomutual 之前。pos 按毫秒取整报出：读的是当前这一程内的偏移，
             // 而不是整条动画的位置 —— 程是独立的，跨程的位置没有意义。
             + TimelineState(_rec0)
             // rows/away/moving 排在 nomutual **之前**：后者是加载模式那半必须读到的最后一个字段，
             // 所以它排在最后，标签也得为这一份更长载荷留出折行的位置。
             + $"rows={row.Rows};away={row.Away};moving={row.Moving};"
             + $"nomutual={NoMutualCount()};";
    }

    /// <summary>
    /// 时间轴控制那排的回读：暂停与否、速率、当前程内位置、第几程。速率用不变文化格式化，免得小数点跟着
    /// 机器区域设置变，验收侧读到 "0,25" 就解析不了。
    /// </summary>
    private static string TimelineState(Rectangle target)
    {
        const bool mutual = true, noMutual = true;
        return $"paused={(Transition.IsPaused(target, mutual, noMutual) ? 1 : 0)};"
             + $"rate={Transition.Rate(target, mutual, noMutual).ToString("0.###", CultureInfo.InvariantCulture)};"
             + $"pos={(int)Transition.Position(target, mutual, noMutual).TotalMilliseconds};"
             + $"cycle={Transition.Cycle(target, mutual, noMutual)};";
    }

    /// <summary>
    /// 加载模式那一排三块目标的状态：动画真正写的那几个属性，读出来报给测试。
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

        foreach (var target in new Rectangle[] { _rec0, _rec1, _rec2 })
        {
            if (TransitionSchedulerCore.TryGetNoMutualScheduler(target, out var schedulers))
            {
                running += schedulers.Length;
            }
        }

        return running;
    }

    /// <summary>上一拍每一行的值，用来判断"这一拍还在不在变"。按键是采样器名。</summary>
    private readonly Dictionary<string, double[]> _rowPrevious = new(StringComparer.Ordinal);

    /// <summary>
    /// 行数 / 偏离声明起点的行数 / 相对上一拍仍在变的行数。
    /// </summary>
    /// <remarks>
    /// 顶栏那三个按钮唯一的可观测量，与加载模式那边的 <c>nomutual</c> 同一个思路：它不解释动画该到哪，
    /// 只说明有没有在动、有没有回到起点。只数采样器行 —— 别的行的"起点"由各自的台子宣布，不在这里说话。
    /// <para>
    /// <b>两个数得分开，缺一个就有假命题。</b> 只看"偏离起点"，静息时也是满的 —— 每行初始持有的是控件的默认值
    /// （<c>null</c> 画刷、默认圆角），不是采样器声明的起点，于是"全部启动后 &gt; 0"在什么都没跑时也成立。
    /// 所以进界面时先把每一行写成它声明的起点（见 Loaded），并另记一个"这一拍还在变"：
    /// 静息 0 / 启动后满 / 停止后 0 / 重置后 0 且回到起点。
    /// </para>
    /// <para>
    /// <b>两个数都是"这一拍"的快照，不是"这一批跑起来了"的判据。</b> 一行刚起动画的头几帧里，它的分量可能与
    /// 声明的起点**逐位相同**：颜色那一条尤其如此（<c>ColorSampler</c> 的通道是 <c>byte</c>，截断之后连续几帧
    /// 都还停在起点的字节上）。所以要问"这一批起来了没有"，得**等** <c>away</c> 追上该有的行数，
    /// 不能拿某一拍去断言（见验收侧的 ScanToolbar）。
    /// </para>
    /// </remarks>
    private (int Rows, int Away, int Moving) RowState()
    {
        var away = 0;
        var moving = 0;

        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            var subject = _bench.SubjectFor(sampler);
            var now = SamplerProbe.Read(subject, sampler);

            if (!SamplerProbe.MatchesStart(subject, sampler)) away++;

            if (_rowPrevious.TryGetValue(sampler, out var before)
                && !SamplerProbe.SameComponents(before, now.Components))
            {
                moving++;
            }

            _rowPrevious[sampler] = now.Components;
        }

        return (SamplerProbe.SamplerNames.Count, away, moving);
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
        Animation0.Execute(_rec0);
        Animation1.Execute(_rec1);
        Animation2.Execute(_rec2);
    }

    private void LoadBackground(object sender, RoutedEventArgs e)
    {
        // Started from a non-UI thread; the framework automatically switches back to the UI thread
        // (thread marshaling derived from the target).
        _ = Task.Run(() =>
        {
            Animation0.Execute(_rec0);
            Animation1.Execute(_rec1);
            Animation2.Execute(_rec2);
        });
    }

    private void LoadMainThreadNonMutual(object sender, RoutedEventArgs e)
    {
        // Main thread + CanMutualTask: false — run concurrently, neither cancels the other
        Animation0.Execute(_rec0, CanMutualTask: false);
        Animation1.Execute(_rec1, CanMutualTask: false);
        Animation2.Execute(_rec2, CanMutualTask: false);
    }

    private void LoadBackgroundNonMutual(object sender, RoutedEventArgs e)
    {
        // Non-UI thread + CanMutualTask: false — run concurrently
        _ = Task.Run(() =>
        {
            Animation0.Execute(_rec0, CanMutualTask: false);
            Animation1.Execute(_rec1, CanMutualTask: false);
            Animation2.Execute(_rec2, CanMutualTask: false);
        });
    }

    private void RepeatMutual(object sender, RoutedEventArgs e)
    {
        // Each click starts a mutually-exclusive animation on Rec0, and the new animation cancels
        // the previous one (tests scheduler gating and cancellation).
        _ = Task.Run(() => Animation0.Execute(_rec0));
    }

    // -----------------------------------------------------------------------------------------------
    // 时间轴控制
    //
    // 这一排作用在加载模式那三块长动画（Rec0/1/2）上，而不是下面 900ms 的一次性过冲：暂停一个 900ms 的过冲
    // 在屏幕上和"它就是这么快"分不开，而这里同时也要给人看。三块各自是一条真 Transition，暂停/变速/定位都按
    // target 寻址，所以是逐个调用 —— 这本身就是"控制面挂在 target 上、不挂在快照上"的一次演示。
    // -----------------------------------------------------------------------------------------------

    private Rectangle[] ControlTargets() => [_rec0, _rec1, _rec2];

    private void PauseAll(object? sender, RoutedEventArgs e)
    {
        foreach (var target in ControlTargets())
        {
            Transition.Pause(target, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    private void ResumeAll(object? sender, RoutedEventArgs e)
    {
        foreach (var target in ControlTargets())
        {
            Transition.Resume(target, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    private void RateSlow(object? sender, RoutedEventArgs e) => SetRate(0.25d);

    private void RateFast(object? sender, RoutedEventArgs e) => SetRate(4d);

    /// <summary>
    /// 正常速。把速率调回 1。时间轴只有正速率 —— 减速之后要回到原速就靠这一个，而不是再去点一次重置。
    /// </summary>
    private void RateNormal(object? sender, RoutedEventArgs e) => SetRate(1d);

    private void SetRate(double rate)
    {
        foreach (var target in ControlTargets())
        {
            Transition.SetRate(target, rate, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    /// <summary>
    /// 跳到下一程的起点。程计数器是整数，所以"第几程"可以被指名 —— 这正是绝对时间轴需要它的原因。
    /// </summary>
    private void SeekNextPass(object? sender, RoutedEventArgs e)
    {
        foreach (var target in ControlTargets())
        {
            Transition.Seek(target, Transition.Cycle(target, IncludeMutual: true, IncludeNoMutual: true) + 1,
                TimeSpan.Zero, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    // -----------------------------------------------------------------------------------------------
    // 加载：那三条动画
    //
    // 行程按**这一行自己的台子**定，不是按窗口宽度定的：三块目标现在各在列表的一行里，台子就是它的边界，
    // 跑出行外会被裁掉 —— 而"跑出去看不见"与"没在跑"在屏幕上分不开。验收侧只钉静止态（CreateResetRec0/1/2），
    // 行程是观感，不是契约。
    // -----------------------------------------------------------------------------------------------

    private static readonly Transition<Rectangle> Animation0 =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, LoadTravel)
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

    // Delayed animation: reverse rotation + background gradient.
    // 旋转绕元素自己的中心（见构造函数里那句 RenderTransformOrigin）—— 行里的台子是裁边的，
    // 绕左上角转 180° 会把方块整个甩到台子外。
    private static readonly Transition<Rectangle> Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(5))
            .Property(r => r.RenderTransform, [new RotateTransform(180)], RotationDirection.ClockWise)
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

    // Combined animation: translate + scaling + switch to a new gradient background.
    // 平移收到 130、缩放绕元素自己的中心：1.3 倍是 104×78，台子只有 300×84，
    // 不这么收着，方块在放大那半段就会被裁掉两个边。
    //
    // 这一组里**不放**三维旋转（原先那条 Rotate3DTransform 已删）：抓帧实测，180° 的三维翻转在中间帧投影成
    // 一个退化的三角形、末帧什么都不剩 —— 那一行的方块有整段时间看不见，而这正是这个演示要消除的错觉
    // （"跑出去看不见"与"没在跑"在屏幕上分不开）。WPF 与 Jalium 那两侧同样是两个变换，行里那句描述
    // 说的也是"两个变换合成一个 TransformGroup"。
    private static readonly Transition<Rectangle> Animation2 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform,
            [
                new TranslateTransform(130, 0),
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

    // The Bs1 gradient (Yellow → Violet, 0,0 → 1,1) is rebuilt in code because a resource is not a value a
    // transition path can point at. Keep this the one place it is defined: Rec2's reset animates Fill back to
    // this brush and the two gradient strokes are it too.
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
}
