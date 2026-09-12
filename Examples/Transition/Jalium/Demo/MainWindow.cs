using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Shapes;
using Jalium.UI.Threading;
using System.Diagnostics;
using System.Globalization;
using VeloxDev.TransitionSystem;

namespace Demo;

/// <summary>
/// Standalone animation test for the VeloxDev.Jalium PlatformAdapters (TransitionSystem).
/// </summary>
/// <remarks>
/// 版面与其余几个平台的 Transition demo 对齐：顶栏（全局三件 + 五种加载方式 + 载荷读数）压在上面，
/// 下面是一张**可滚动的案例列表** —— 一条案例一行，行里是那条案例真正在动的元素、一句"这条在验什么"，
/// 以及这一行自己的 启动 / 关闭 / 重置。
/// <para>
/// 三种行（加载 3 条、过冲 5 条、每条采样器 1 条）长得一样，区别只在各自的令牌、描述与动作里，
/// 三条合成一处的定义在 <see cref="RebuildCaseList"/>。
/// </para>
/// <para>
/// 界面全部在代码里造（Jalium 这侧没有 XAML）：AutomationId 是验收套件抓这些控件的把手，
/// 所以测试永远不匹配中文标签，token 与语言无关且稳定。
/// </para>
/// </remarks>
internal sealed class MainWindow : Window
{
    // ── 在屏元素 ────────────────────────────────────────────────────────────
    //
    // 三块加载目标与五块过冲目标以前摊在窗口的格子上；现在每一块是列表里的一行，所以由代码造、由行带进列表。
    // 位移那两条各有一块自己的目标：一个元素只能有一个父级，而每一行各自是一个父级。

    private readonly Rectangle _rec0;
    private readonly Rectangle _rec1;
    private readonly Rectangle _rec2;
    private readonly Rectangle _over0;
    private readonly Rectangle _over1;
    private readonly Rectangle _over2;
    private readonly Rectangle _over3;
    private readonly Rectangle _over4;

    // ── 载荷载体 ────────────────────────────────────────────────────────────

    /// <summary>人类读数：过冲只有从数字上才看得出来（眼睛分不出"越过再回来"与"一条更慢的缓动"）。</summary>

    /// <summary>读数的机器可读孪生体：同一批值、固定的 key=value 载荷，测试不必解析版式。</summary>
    private readonly TextBlock _overState;

    /// <summary>最后一次点某一行跑出来的那一帧序列。</summary>
    private readonly TextBlock _overConf;

    /// <summary>那次点击把控件属性写成了什么、跑完才写全。</summary>
    private readonly TextBlock _overLive;

    /// <summary>批量载荷：点一次"全部启动"，每一行的五帧闭式解与这一行的观察摘要都在这一份里。</summary>
    private readonly TextBlock _overBatch;

    public MainWindow()
    {
        Title = "VeloxDev Transition - Jalium";
        Width = 1240;
        Height = 900;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        // 先建元素：下面的按钮 lambda 会捕获它们，字段必须在捕获前完成赋值。
        _rec0 = MakeRect(new SolidColorBrush(Colors.Cyan));
        _rec1 = MakeRect(new SolidColorBrush(Colors.Lime));
        // Rec2 起止两端都是渐变 —— 上侧这一排里特意留一块跑"渐变 → 渐变"，因为那条路走的是画刷
        // 交叉淡出（非纯色分支），与另外两块的纯色路径不是同一段代码。
        _rec2 = MakeRect(CreateBs1Brush());

        // 位移那两条就地驱动 RenderTransform.X，所以变换在这里造一次。
        _over0 = MakeRect(new SolidColorBrush(OverColorStart));
        _over4 = MakeRect(new SolidColorBrush(OverColorStart));
        _over1 = MakeRect(new SolidColorBrush(OverColorStart));
        _over2 = MakeRect(new SolidColorBrush(Color.FromRgb(0x7A, 0x5A, 0xA5)));
        _over3 = MakeRect(CreateBs1Brush());

        _over0.RenderTransform = new TranslateTransform();
        _over4.RenderTransform = new TranslateTransform();

        // 旋转与放大都是**绕元素自己**转的（RenderTransformOrigin 默认在左上角）。绕左上角转 180° 会把方块整个
        // 甩到台子的左上方去 —— 而台子是裁边的，那一瞬看上去就和"没在跑"分不开。绕自己转则全程留在台子里，
        // 这正是每一行都要装得下自己那段行程的意思。
        _rec1.RenderTransformOrigin = new Point(0.5d, 0.5d);
        _rec2.RenderTransformOrigin = new Point(0.5d, 0.5d);

        // 折行不是可有可无：这份载荷现在多出 rows/away/moving，而 nomutual 必须仍然看得见 —— 它排在最后，
        // 所以标签要留出折行的位置，否则那个最要紧的字段会被裁掉。
        _overState = MakeReadout("over.state", "v=1;seq=0", wrap: true, fontSize: 10);
        _overConf = MakeReadout("over.conf", "v=1;seq=0;n=0;", wrap: false, fontSize: 10);
        _overLive = MakeReadout("over.live", "v=1;seq=0;done=1;", wrap: false, fontSize: 10);
        _overBatch = MakeReadout("over.batch", "v=1;seq=0;done=1;rows=0;", wrap: true, fontSize: 10);

        var toolbar = BuildToolbar();
        var list = new ScrollViewer
        {
            Margin = new Thickness(4, 2, 4, 4),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        RebuildCaseList();
        list.Content = SamplerBench.Build(_caseRows);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(list, 1);
        root.Children.Add(toolbar);
        root.Children.Add(list);
        Content = root;

        // 读数用定时器采样目标属性，不用 effect 的事件：流水线每段都会 Clone() effect，
        // 订阅在原始 effect 上的处理函数不会触发。
        var readoutTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        readoutTimer.Tick += (_, _) => UpdateReadout();
        readoutTimer.Start();

        // 每一行先写成它自己声明的起点。不这么做的话，静息时每行持有的是控件的默认值（null 画刷、默认圆角），
        // 于是"偏离起点"的行数在什么都没跑的时候就是满的 —— 顶栏那个数会变成一句假命题。
        Loaded += (_, _) => ResetSamplerRows();
    }

    // -----------------------------------------------------------------------------------------------
    // 顶栏
    //
    // 全局面板一次动到下面每一行；加载模式那五件是"怎么加载"，不是"加载什么" —— 它们驱动的三个形状在下面
    // 各占一行、各自有自己的启动。把模式留在顶栏，列表才可能只有一种行：每行恰好 启动 / 关闭 / 重置。
    // -----------------------------------------------------------------------------------------------

    private StackPanel BuildToolbar()
    {
        var toolbar = new StackPanel { Margin = new Thickness(4) };

        var global = new WrapPanel();
        global.Children.Add(MakeToolbarButton("全部启动", "over.btn.start.all", StartAllCases));
        global.Children.Add(MakeToolbarButton("停止全部", "over.btn.stop.all", ExitAll));
        global.Children.Add(MakeToolbarButton("重置", "over.btn.reset.all", ResetAllCases));
        toolbar.Children.Add(global);

        var modes = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };

        // 令牌与其余六个平台逐字一致 —— 验收套件靠它点这一排。
        // 四条会跑 Animation2 的按钮都先把 Rec2 的画刷写回渐变起点 —— 见 SeedRec2 的说明。
        modes.Children.Add(MakeToolbarButton("主线程互斥", "over.btn.load.main", () => { SeedRec2(); LoadMainThread(); }, 118, 38));
        modes.Children.Add(MakeToolbarButton("后台线程互斥", "over.btn.load.background", () => { SeedRec2(); _ = Task.Run(LoadMainThread); }, 118, 38));
        modes.Children.Add(MakeToolbarButton("主线程并发", "over.btn.load.main.concurrent", () => { SeedRec2(); LoadMainThreadNonMutual(); }, 118, 38));
        modes.Children.Add(MakeToolbarButton("后台线程并发", "over.btn.load.background.concurrent", () => { SeedRec2(); _ = Task.Run(LoadMainThreadNonMutual); }, 118, 38));
        modes.Children.Add(MakeToolbarButton("连续互斥", "over.btn.load.repeat", () => _ = Task.Run(() => Animation0.Execute(_rec0)), 110, 38));
        toolbar.Children.Add(modes);

        // 时间轴控制。作用对象是上面那排加载模式驱动的三块长动画（十来秒的循环），不是下面 900ms 的一次性
        // 过冲 —— 后者暂停与不暂停在屏幕上分不出来，而这一排既要给验收套件当把手，也要给人看。
        var timeline = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };

        // 令牌与其余六个平台逐字一致 —— 验收套件靠它点这一排。
        timeline.Children.Add(MakeToolbarButton("暂停", "over.btn.pause", PauseAll, 86, 34));
        timeline.Children.Add(MakeToolbarButton("恢复", "over.btn.resume", ResumeAll, 86, 34));
        timeline.Children.Add(MakeToolbarButton("慢速 ×0.25", "over.btn.rate.slow", RateSlow, 96, 34));
        timeline.Children.Add(MakeToolbarButton("快速 ×4", "over.btn.rate.fast", RateFast, 96, 34));
        timeline.Children.Add(MakeToolbarButton("正常 ×1", "over.btn.rate.normal", RateNormal, 96, 34));
        timeline.Children.Add(MakeToolbarButton("下一程", "over.btn.seek.next", SeekNextPass, 86, 34));
        toolbar.Children.Add(timeline);

        toolbar.Children.Add(_overState);
        toolbar.Children.Add(_overConf);
        toolbar.Children.Add(_overLive);
        toolbar.Children.Add(_overBatch);

        return toolbar;
    }

    private Button MakeToolbarButton(string text, string token, Action action, double width = 110, double height = 34)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, Foreground = new SolidColorBrush(Colors.White) },
            Margin = new Thickness(2),
            FontSize = 12,
            Width = width,
            Height = height,
        };

        SamplerBench.SetToken(button, token);
        button.Click += (_, _) => action();
        return button;
    }

    private static TextBlock MakeReadout(string token, string initial, bool wrap, double fontSize)
    {
        var readout = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Text = initial,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,

            // 驱动的读取面，但不占版面：Height=0 而元素仍在自动化树里，套件照读控件属性。不能用不可见 ——
            // 那会连同自动化对等体一起摘掉，而 over.state 正是每个驱动的就绪握手。
            Height = 0d,
        };

        SamplerBench.SetToken(readout, token);
        return readout;
    }

    // -----------------------------------------------------------------------------------------------
    // 案例列表
    //
    // 一张统一的表：一条案例一行，行里有元素、有一句"这条在验什么"、有这一行自己的三个动作。行的种类只有
    // 三种 —— 加载、过冲、采样器 —— 但对读表的人来说它们长得一样，区别只在各自的令牌、描述与动作里。
    // -----------------------------------------------------------------------------------------------

    /// <summary>列表里的每一行，按顺序 —— 顶栏那个"全部启动"走的就是这里的动作。</summary>
    private readonly List<CaseRow> _caseRows = [];

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

    /// <summary>一条加载案例：元素就是那块被三个动画之一驱动的方块。</summary>
    private static CaseRow LoadRow(
        string id, string title, string description, Rectangle element, Action start, Action restore)
    {
        var stage = PlayStage(element, LoadStageWidth);

        return new CaseRow(
            title,
            description,
            stage,
            LoadStageWidth,
            $"over.row.start.{id}",
            $"over.row.stop.{id}",
            $"over.row.reset.{id}",
            start,
            () => Transition.Exit(element, IncludeMutual: true, IncludeNoMutual: true),
            () => ResetCase(element, restore));
    }

    /// <summary>
    /// 一条过冲案例：元素是那块目标自己，台子只负责给它一个走得出的格子。
    /// </summary>
    /// <param name="stageWidth">
    /// 元素区的宽度。位移那两条要放得下整段行程（端点是 230，方块本身 80），不按原值留出位置的话，
    /// 它一跑就整块滑出格子 —— 看上去和"没动"一模一样，正是这个演示要消除的错觉。
    /// </param>
    private static CaseRow OvershootRow(
        string id, string title, string description, Rectangle target, double stageWidth,
        Action start, Action restore)
    {
        var stage = PlayStage(target, stageWidth);

        return new CaseRow(
            title,
            description,
            stage,
            stageWidth,
            $"over.row.start.{id}",
            $"over.row.stop.{id}",
            $"over.row.reset.{id}",
            start,
            () => Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true),
            () => ResetCase(target, restore));
    }

    /// <summary>
    /// 把元素放进一块定宽、裁边的台子里 —— 它的行程跑到端点也不会压到旁边的字上。
    /// </summary>
    /// <remarks>
    /// 台子的宽由那一条案例的行程决定（见各条案例的 <c>*StageWidth</c>）：这是"这一段动哪儿"的唯一来源，
    /// 窄一点就会让元素在最该被看见的那一瞬跑出边界 —— 而"跑出去看不见"和"没在跑"在屏幕上分不开。
    /// </remarks>
    private static Canvas PlayStage(FrameworkElement element, double width)
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
    // 每一行元素的场地
    //
    // 行高是统一的（SamplerBench.RowHeight），所以每一行都装得下自己那段行程 —— 装不下就等于元素跑没了。
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// 加载那三行的元素区宽度，以及那三条动画的行程。
    /// </summary>
    /// <remarks>
    /// 行程不是按窗口宽度定的：三块目标现在各在列表的一行里，台子就是它的边界，跑出去会被裁掉 ——
    /// 而"跑出去看不见"和"没在跑"在屏幕上是一样的。所以行程缩到台子里放得下（80 宽的方块 + 200 的行程）。
    /// 验收侧只钉静止态，行程是观感，不是契约。
    /// </remarks>
    private const double LoadStageWidth = 300d;

    private const double LoadTravel = 200d;

    /// <summary>位移那两条的元素区宽度：端点 230 外加上方块自己的 80，不留出来它一跑就整块滑出格子。</summary>
    private const double ShiftStageWidth = 420d;

    /// <summary>尺寸那一条的元素区宽度：宽度从 80 长到 220，Elastic 峰值处（约 272）也还留在台子里。</summary>
    private const double SizeStageWidth = 288d;

    /// <summary>颜色与渐变那两条不改变尺寸，元素区就是方块本身加一点边。</summary>
    private const double BodyStageWidth = 96d;

    // 与过冲目标同尺寸的方块（不是被拉长的条），与参考 demo 一致。
    private static Rectangle MakeRect(Brush fill) => new() { Width = 80, Height = 60, Fill = fill };

    // -----------------------------------------------------------------------------------------------
    // 过冲：把目标放回起点，再起动画
    //
    // 上面那些缓动全都落在 [0,1] 内，没有一个能越过目标再回来。Back 峰值 1.10、Elastic 峰值 1.37，
    // 过冲具体意味着什么由采样器决定：R/G/B 共用一个进度、在第一个触边的通道处停下；尺寸在 0 处停下；
    // 而自带区间的通道与普通 double 保留完整的缓动时间。读数才让过冲可观测。
    // -----------------------------------------------------------------------------------------------

    // 位移的目标：定 240 时 Elastic 的峰值（1.37 倍）刚好顶到台子边缘，只差两三个像素 —— 看上去像被裁掉。
    // 收到 230 之后，峰值处（约 316）连方块一共 404，离台子的 420 还有一截。
    private const double ShiftTarget = 230d;
    private const double WidthTarget = 220d;

    // 过冲目标的起始值：构造函数、每次运行前的归位、重置三处共用同一份定义。
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

    // ── Animations (aligned with the other platforms) ───────────────────────

    // 简单：嵌套 TranslateTransform.X 路径 + 纯色填充。
    private static readonly Transition<Rectangle> Animation0 =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, LoadTravel)
            .Property(r => r.Fill, new SolidColorBrush(Colors.OrangeRed))
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Sine.InOut,
            });

    // 延迟：整个变换对象被换掉（端点是另一个同类型的 RotateTransform）+ 填充。
    // 旋转是绕元素自己的中心转的（见构造里那句 RenderTransformOrigin）：端点是同一个 RotateTransform，
    // 只是从 0° 外推到 180°。行里的台子是裁边的，绕左上角转会把它整块甩出格子。
    private static readonly Transition<Rectangle> Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(5))
            .Property(r => r.RenderTransform, [new RotateTransform(180)], RotationDirection.ClockWise)
            .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(4),
                IsAutoReverse = true,
                FPS = 144,
                LoopTime = 4,
            });

    // 合成：变换集合（平移 + 缩放）+ 填充，然后 AwaitThen + 填充。
    // 平移收到 130、缩放绕元素自己的中心（见构造里那句 RenderTransformOrigin）：1.3 倍是 104×78，台子只有
    // 300×84，不这么收着，方块在放大那半段就会被裁掉两个边。
    private static readonly Transition<Rectangle> Animation2 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform,
                [new TranslateTransform(130, 0), new ScaleTransform(1.3, 1.3)],
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

    /// <summary>
    /// 把 Rec2 的画刷同步写回渐变起点，让 <c>Animation2</c> 无论开始还是终结都拿渐变当端点。
    /// </summary>
    /// <remarks>
    /// <c>Prepare</c> 把目标的**当前值**当作动画起点。上一轮若是被"停止全部"或新动画打断停在半路，
    /// <c>Rec2.Fill</c> 会停在交叉淡出混出来的**纯色**上，下一次加载就成了"纯色 → 渐变" —— 合成效果的
    /// 输入端不再确定。这与渐变过冲那一行的起点（<c>RestoreGradientStart</c>）是同一件事、同一个理由。
    /// <para>
    /// 必须在 UI 线程上写：所以后台加载那两个也先在这里写回、再派发，而不是塞进 Task.Run 里面。
    /// </para>
    /// </remarks>
    private void SeedRec2() => _rec2.Fill = CreateBs1Brush();

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

    // -----------------------------------------------------------------------------------------------
    // 时间轴控制
    //
    // 这一排作用在加载模式那三块长动画（Rec0/1/2）上，而不是下面 900ms 的一次性过冲：暂停一个 900ms 的过冲
    // 在屏幕上和"它就是这么快"分不开，而这里同时也要给人看。三块各自是一条真 Transition，暂停/变速/定位都按
    // target 寻址，所以是逐个调用 —— 这本身就是"控制面挂在 target 上、不挂在快照上"的一次演示。
    // -----------------------------------------------------------------------------------------------

    private Rectangle[] ControlTargets() => [_rec0, _rec1, _rec2];

    private void PauseAll()
    {
        foreach (var target in ControlTargets())
        {
            Transition.Pause(target, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    private void ResumeAll()
    {
        foreach (var target in ControlTargets())
        {
            Transition.Resume(target, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    private void RateSlow() => SetRate(0.25d);

    private void RateFast() => SetRate(4d);

    /// <summary>
    /// 正常速。把速率调回 1。时间轴只有正速率 —— 减速之后要回到原速就靠这一个，而不是再去点一次重置。
    /// </summary>
    private void RateNormal() => SetRate(1d);

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
    private void SeekNextPass()
    {
        foreach (var target in ControlTargets())
        {
            Transition.Seek(target, Transition.Cycle(target, IncludeMutual: true, IncludeNoMutual: true) + 1,
                TimeSpan.Zero, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    /// <summary>顶栏"停止全部"：整块界面一起停下，否则这个按钮的名字就是假的。原地冻结，不回起点。</summary>
    private void ExitAll()
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
        ResetSamplerRows();
    }

    private void ResetOverShoot()
    {
        foreach (var target in new[] { _over0, _over1, _over2, _over3, _over4 })
        {
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        }

        RestoreShiftStart();
        RestoreElasticStart();
        RestoreColorStart();
        RestoreSizeStart();
        RestoreGradientStart();
    }

    // ---- 每一行"重置"的目标状态：就是这些元素声明时的那份值 ----

    private void RestoreTranslateRow()
    {
        _rec0.RenderTransform = new TranslateTransform();
        _rec0.Fill = new SolidColorBrush(Colors.Cyan);
    }

    // Rec1/Rec2 的 RenderTransform 得**直接赋 null**，不走构建器：适配器的 Transform 重载收的是集合，
    // 传空集合建出来的是一个空的 TransformGroup，不是 null —— 而声明的静止态就是"没有变换"。
    private void RestoreRotateRow()
    {
        _rec1.RenderTransform = null;
        _rec1.Fill = new SolidColorBrush(Colors.Lime);
    }

    private void RestoreCombineRow()
    {
        _rec2.RenderTransform = null;
        _rec2.Fill = CreateBs1Brush();
    }

    private void RestoreShiftStart() => ((TranslateTransform)_over0.RenderTransform!).X = 0;

    private void RestoreElasticStart() => ((TranslateTransform)_over4.RenderTransform!).X = 0;

    private void RestoreColorStart() => _over1.Fill = new SolidColorBrush(OverColorStart);

    private void RestoreSizeStart() => _over2.Width = OverWidthStart;

    private void RestoreGradientStart() => _over3.Fill = CreateBs1Brush();

    /// <summary>三类场景共用的八块目标：加载三块 + 过冲五块。采样器行自成一套，不在这里。</summary>
    private Rectangle[] ScenarioTargets()
        => [_rec0, _rec1, _rec2, _over0, _over1, _over2, _over3, _over4];

    // -----------------------------------------------------------------------------------------------
    // 采样器：演出与批量
    // -----------------------------------------------------------------------------------------------

    /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
    private long _probeSequence;

    private readonly SamplerBench _bench = new();

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
        _overConf.Text = SamplerProbe.Run(subject, sampler, ++_probeSequence);

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
    private void StartAllCases()
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
        var payload = new System.Text.StringBuilder(
            $"v=1;seq={sequence};done={(done ? 1 : 0)};rows={SamplerProbe.SamplerNames.Count};");

        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            payload.Append(_batchFrames[sampler]);

            if (done) payload.Append(_watches[sampler].BatchFields(sampler));
        }

        _overBatch.Text = payload.ToString();
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
        _overLive.Text = SamplerProbe.LiveWatch.Pending(sampler, sequence);

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
            _overLive.Text = watch.Digest(sampler, sequence);
        };

        timer.Start();
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

    private string BuildState()
    {
        _sequence++;

        // 只有标量目标记录峰值。峰值存在的意义是"刀刃型峰"——采样落在尖峰两侧就会低估它；颜色的过冲是一段
        // 形状而不是一个尖峰，报当前值就够，测试轮询取最大即可。
        var x = (_over0.RenderTransform as TranslateTransform)?.X ?? double.NaN;
        var elasticX = (_over4.RenderTransform as TranslateTransform)?.X ?? double.NaN;
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
    /// （null 画刷、默认圆角），不是采样器声明的起点，于是"全部启动后 &gt; 0"在什么都没跑时也成立。所以进界面时
    /// 先把每一行写成它声明的起点（见 Loaded），并另记一个"这一拍还在变"：
    /// 静息 0 / 启动后满 / 停止后 0 / 重置后 0 且回到起点。
    /// </para>
    /// <para>
    /// <b>两个数都是"这一拍"的快照，不是"这一批跑起来了"的判据。</b> 一行刚起动画的头几帧里，它的分量可能与
    /// 声明的起点**逐位相同**：这一侧动画的第一帧是在点击处理里同步写下的，而按定义它就是起点；颜色那一条更久
    /// 一点 —— <c>ColorSampler</c> 的通道是 <c>byte</c>（截断），于是连续几帧都还停在起点的字节上。所以要问
    /// "这一批起来了没有"，得**等** <c>away</c> 追上该有的行数，不能拿某一拍去断言（见验收侧的 ScanToolbar）。
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

    // 把刷子写成测试能读懂的形式：纯色给 #rrggbb，其余给类型名。
    private static string Describe(Brush? brush)
        => brush is SolidColorBrush solid ? $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
           : brush?.GetType().Name ?? "none";

    // 载荷只取目标的真实属性，不缓存也不伪造。
    private void UpdateReadout() => _overState.Text = BuildState();


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
