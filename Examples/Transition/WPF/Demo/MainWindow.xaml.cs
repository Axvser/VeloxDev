using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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

        // 每块台子收下它那块目标。位移那两条共用 Over0，所以也共用 Over0Stage —— 一个元素只能有一个父级。
        Over0Stage.Children.Add(Over0);
        Over1Stage.Children.Add(Over1);
        Over2Stage.Children.Add(Over2);
        Over3Stage.Children.Add(Over3);
        Over4Stage.Children.Add(Over4);

        RebuildCaseList();

        // Reset snapshots are taken only after the element is Loaded, avoiding information loss from
        // an initial state that has not yet been established.
        // Rec0's RenderTransform.X is modified in place, which would pollute the references held by
        // the snapshot, so Rec0 is reset with a new object; Rec1/Rec2 Fills are replaced wholesale
        // (not mutated), so the initial snapshots fully restore them.
        Loaded += (s, e) =>
        {
            if (_resetInitialized) return;
            _resetInitialized = true;

            btnReset.Click += (s, e) =>
            {
                ResetCase(Rec0, RestoreTranslateRow);
                ResetCase(Rec1, RestoreRotateRow);
                ResetCase(Rec2, RestoreCombineRow);
                ResetOverShoot();

                // "重置" is the whole surface's reset, so it ends the sampler rows too — that is what makes the
                // toolbar's numbers mean something: after this, no row is away from its declared start.
                ResetSamplerRows();
            };

            // Sampled on a timer rather than from the effect's events: the pipeline clones the effect once per
            // segment, so the handlers subscribed on the builder's effect are not the ones that fire.
            var readout = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            readout.Tick += (s, e) =>
            {
                Readout.Text =
                    $"位移 Back   当前 {((TranslateTransform)Over0.RenderTransform).X,7:F1}"
                    + $"   |   Elastic   当前 {((TranslateTransform)Over4.RenderTransform).X,7:F1}"
                    + $"   |   目标 {ShiftTarget,6:F1}     宽度 目标 {WidthTarget,6:F1}   当前 {Over2.Width,7:F1}";
                OverState.Text = BuildState();
            };
            readout.Start();

            // 每一行先写成它自己声明的起点。不这么做的话，静息时每行持有的是控件的默认值（null 画刷、灰底色、
            // 默认圆角），于是"偏离起点"的行数在什么都没跑的时候就是满的 —— 顶栏那个数会变成一句假命题。
            ResetSamplerRows();
        };
    }

    // -----------------------------------------------------------------------------------------------
    // 案例列表
    //
    // 一张统一的表：一条案例一行，行里有元素、有一句"这条在验什么"、有这一行自己的三个动作。行的种类只有
    // 三种 —— 加载、过冲、采样器 —— 但对读表的人来说它们长得一样，区别只在各自的令牌、描述与动作里。
    // 三种行的元素都**不再**声明在 XAML 里：元素属于行，而行的顺序与数量由表决定。
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// 把三种案例行拼成一张表：先加载那三行，再过冲那五行，最后每个采样器一行。
    /// </summary>
    /// <remarks>
    /// 采样器排在最后是有意的：它们是套件逐条驱动的对象，也是最需要滚动的部分，而滚动这一步本身要被真的走到。
    /// </remarks>
    private void RebuildCaseList()
    {
        _caseRows.Clear();

        List<CaseRow> rows =
        [
            LoadRow("translate", "平移",
                "嵌套属性路径：动画写的是 RenderTransform.X —— 子对象上的成员，而且是被就地改的那一个。",
                Rec0, () => Animation0.Execute(Rec0), RestoreTranslateRow),

            LoadRow("rotate", "旋转",
                "整个变换对象被换掉：端点是另一个同类型的 RotateTransform，逐字段外推而不是逐分量改。",
                Rec1, () => Animation1.Execute(Rec1), RestoreRotateRow),

            LoadRow("combine", "平移 + 缩放",
                "两个变换合成一个 TransformGroup，按类型配对插值；第二段还会转去动颜色。",
                Rec2, () => Animation2.Execute(Rec2), RestoreCombineRow),

            OvershootRow("shift-back", "位移 Back.Out",
                "共享进度的标量过冲：Back 越过目标 10% 再落回来，数字看得出来、眼睛看不出来。",
                Over0, Over0Stage, ShiftElementWidth, () => RunOvershoot(Over0, OverScalarBack, "back", BackDurationMs, 0, RestoreShiftStart), RestoreShiftStart),

            OvershootRow("shift-elastic", "位移 Elastic.Out",
                "上面那一行的另一条曲线：Elastic 峰值更高、回弹次数更多，两行同时跑就是同一次对比。",
                Over4, Over4Stage, ShiftElementWidth, () => RunOvershoot(Over4, OverScalarElastic, "elastic", ElasticDurationMs, 4, RestoreElasticStart), RestoreElasticStart),

            OvershootRow("color", "颜色过冲",
                "四通道按颜色的规则走：共用进度撞到 255 就整组停住，所以动的是亮度而不是色相。",
                Over1, Over1Stage, BodyWidth, () => RunOvershoot(Over1, OverColor, "color", BackDurationMs, 1, RestoreColorStart), RestoreColorStart),

            OvershootRow("size", "尺寸过冲",
                "宽度是 double，没有上下限：外推照走，只是这一条只会变大，碰不到负的那一端。",
                Over2, Over2Stage, SizeElementWidth, () => RunOvershoot(Over2, OverSize, "size", ElasticDurationMs, 2, RestoreSizeStart), RestoreSizeStart),

            OvershootRow("brush", "渐变过冲",
                "非纯色画刷走交叉淡出：混合系数是个分数，两端各自饱和，不跟着缓动越过端点。",
                Over3, Over3Stage, BodyWidth, () => RunOvershoot(Over3, OverBrush, "brush", ElasticDurationMs, 3, RestoreGradientStart), RestoreGradientStart),
        ];

        // 采样器行由探针表生成。行里的"启动"就是验收套件点的那个把手令牌，点它写闭式解载荷并起那条真动画 ——
        // 所以加一条采样器仍然只需要改 SamplerProbe 一处。
        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            var name = sampler;
            _watches[name] = new SamplerProbe.LiveWatch();
            _batchFrames[name] = string.Empty;

            rows.Add(_bench.SamplerRow(
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

        _caseRows.AddRange(rows);
        CaseRows.Children.Add(SamplerBench.Build(rows));
    }

    /// <summary>
    /// 列表里每一行的三个动作 —— 顶栏那个"全部启动"走的就是这里的 <c>Start</c>，所以它和行里那个按钮
    /// 不可能各说各话。
    /// </summary>
    private readonly List<CaseRow> _caseRows = [];

    /// <summary>
    /// 把元素放进一块定宽、裁边的台子里 —— 它的行程跑到端点也不会压到旁边的字上。
    /// </summary>
    /// <remarks>
    /// 加载行与过冲行共用它。台子宽度由那一条案例的行程决定（<see cref="LoadElementWidth"/> 与
    /// <see cref="ShiftElementWidth"/>）：这是"这一段动哪儿"的唯一来源，窄一点就会让元素在最该被看见的
    /// 那一瞬跑出边界 —— 而"跑出去看不见"和"没在跑"在屏幕上分不开。
    /// </remarks>
    private static Canvas PlayStage(FrameworkElement element, double width)
    {
        var stage = new Canvas { Width = width, Height = 62, ClipToBounds = true };
        stage.Children.Add(element);
        return stage;
    }

    /// <summary>一条加载案例：元素就是那块被三个动画之一驱动的方块。</summary>
    private static CaseRow LoadRow(
        string id, string title, string description, Rectangle element, Action start, Action restore)
        => new(
            title,
            description,
            PlayStage(element, LoadElementWidth),
            LoadElementWidth,
            $"over.row.start.{id}",
            $"over.row.stop.{id}",
            $"over.row.reset.{id}",
            start,
            () => Transition.Exit(element, IncludeMutual: true, IncludeNoMutual: true),
            () => ResetCase(element, restore));

    /// <summary>
    /// 一条过冲案例：元素是那块目标本身。
    /// </summary>
    /// <param name="elementWidth">
    /// 元素区的宽度。位移那两条要放得下整段行程（端点是 300，方块本身 80），不按原值留出位置的话，
    /// 它一跑就整块滑出格子 —— 看上去和"没动"一模一样，正是这个演示要消除的错觉。
    /// </param>
    private static CaseRow OvershootRow(
        string id, string title, string description, Rectangle target, FrameworkElement stage, double elementWidth,
        Action start, Action restore)
        => new(
            title,
            description,
            stage,
            elementWidth,
            $"over.row.start.{id}",
            $"over.row.stop.{id}",
            $"over.row.reset.{id}",
            start,
            () => Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true),
            () => ResetCase(target, restore));

    /// <summary>停掉并把它放回声明的静止态。</summary>
    private static void ResetCase(Rectangle element, Action restore)
    {
        Transition.Exit(element, IncludeMutual: true, IncludeNoMutual: true);
        restore();
    }

    /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
    private long _probeSequence;

    private readonly SamplerBench _bench = new();

    // -----------------------------------------------------------------------------------------------
    // 案例列表里那几块元素
    //
    // 三块加载目标与四块过冲目标以前声明在 XAML 里、排在顶栏的条上；现在每一块是列表里的一行，所以由代码造、
    // 由行带进列表。顺带一个好处：Bs1 只在这里定义一次 —— 以前 XAML 里一份、代码里一份，两处得手动保持一致，
    // 而这个仓库里已经为"两处漂移"踩过一次。
    // -----------------------------------------------------------------------------------------------

    private readonly Rectangle Rec0 = new()
    {
        Fill = Brushes.Cyan, Width = 80, Height = 60, RenderTransform = new TranslateTransform(),
    };

    private readonly Rectangle Rec1 = new() { Fill = Brushes.Lime, Width = 80, Height = 60 };

    private readonly Rectangle Rec2 = new() { Fill = CreateBs1Brush(), Width = 80, Height = 60 };

    // 位移那两条就地驱动 RenderTransform.X，所以变换在这里造一次。
    private readonly Rectangle Over0 = new()
    {
        Fill = new SolidColorBrush(OverColorStart), Width = 80, Height = 60, RenderTransform = new TranslateTransform(),
    };

    /// <summary>
    /// 位移那第二条曲线（Elastic）自己的一块目标。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Over0"/> **不能**是同一块：一个元素只能有一个父级，而每一行各自是一个父级。
    /// 所以"两条曲线同屏对比"改成"上下相邻两行同时看得见" —— 同一条时间轴上，仍然是同一次对比。
    /// </remarks>
    private readonly Rectangle Over4 = new()
    {
        Fill = new SolidColorBrush(OverColorStart), Width = 80, Height = 60, RenderTransform = new TranslateTransform(),
    };

    private readonly Rectangle Over1 = new()
    {
        Fill = new SolidColorBrush(OverColorStart), Width = 80, Height = 60,
    };

    private readonly Rectangle Over2 = new()
    {
        Fill = new SolidColorBrush(Color.FromRgb(0x7A, 0x5A, 0xA5)), Width = 80, Height = 60,
    };

    private readonly Rectangle Over3 = new() { Fill = CreateBs1Brush(), Width = 80, Height = 60 };

    /// <summary>
    /// 过冲那几条的台子：定宽、裁边 —— 行程跑到端点也不会压到旁边的字上。
    /// </summary>
    /// <remarks>
    /// 台子与目标一样是**一条一元素**：位移那两条共用同一块目标（好让两条曲线同屏对比），而一个元素只能有
    /// 一个父级，所以那两条也必须共用同一个台子，不能各包一层。
    /// </remarks>
    private readonly Canvas Over0Stage = new() { Width = ShiftElementWidth, Height = 62, ClipToBounds = true };

    private readonly Canvas Over1Stage = new() { Width = BodyWidth, Height = 62, ClipToBounds = true };

    private readonly Canvas Over4Stage = new() { Width = ShiftElementWidth, Height = 62, ClipToBounds = true };

    private readonly Canvas Over2Stage = new() { Width = SizeElementWidth, Height = 62, ClipToBounds = true };

    private readonly Canvas Over3Stage = new() { Width = BodyWidth, Height = 62, ClipToBounds = true };

    /// <summary>位移那两条的元素区宽度：端点 300 外加上方块自己的 80，不留出来它一跑就整块滑出格子。</summary>
    private const double ShiftElementWidth = 420d;

    /// <summary>
    /// 加载那三行的元素区宽度，以及那三条动画的行程。
    /// </summary>
    /// <remarks>
    /// 行程原来按窗口宽度定（800），那是在三块目标直接摊在窗口上的时候。现在每一块在列表的一行里，
    /// 台子就是它的边界，跑出去会被裁掉 —— 而"跑出去看不见"和"没在跑"在屏幕上是一样的，
    /// 正是 <see cref="SamplerSubject"/> 那边用标尺要消除的错觉。所以行程缩到台子里放得下。
    /// </remarks>
    private const double LoadElementWidth = 300d;

    private const double LoadTravel = 200d;

    /// <summary>尺寸那一条的元素区宽度：宽度从 80 长到 220。</summary>
    private const double SizeElementWidth = 260d;

    /// <summary>颜色与渐变那两条不改变尺寸，元素区就是方块本身。</summary>
    private const double BodyWidth = 80d;

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

    private void RunSamplerProbe(string sampler)
    {
        // 采样器写在**这一格的在屏控件**上、载荷也从它读回，所以下面两件事是同一件事的两种读法。
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
        var property = SamplerProbe.Property(sampler);
        property.SetValue(subject, SamplerProbe.Start(sampler));

        // Effect 的其余默认值正是这里要的：FPS 60、不自动反向、只跑一趟 —— 于是末帧精确落在终点。
        var animation = Transition<SamplerSubject>.Create()
            .Effect(new TransitionEffect { Duration = SamplerProbe.BenchDuration, Ease = Eases.Back.Out });

        animation.GetState().SetValue(property, SamplerProbe.End(sampler));
        animation.GetState().SetInterpolator(property, SamplerProbe.Create(sampler));

        animation.Execute(subject);
    }

    /// <summary>这一行的"关闭"：把它的动画停在原地，不跳到终点。</summary>
    private void StopSamplerRow(string sampler) => StopSamplerRows(sampler);

    /// <summary>这一行的"重置"：停下它，并把声明的起点同步写回元素。</summary>
    private void ResetSamplerRow(string sampler) => ResetSamplerRows(sampler);

    /// <summary>
    /// 顶栏的"全部启动"：每一行同时起一条真动画。
    /// </summary>
    /// <remarks>
    /// 十几条真 <c>Transition</c> 并发跑，是这个库要经得住的一种真实用法；刻意不写任何载荷 ——
    /// 十几条各自写 <c>over.live</c> 只会互相覆盖，而那两份载荷的归属是"点了哪一行"，
    /// 由行里的"启动"负责。
    /// </remarks>
    private void StartAllCases(object sender, RoutedEventArgs e)
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

        // 三种行一起发起 —— 加载三行、过冲五行、采样器若干行，十几条真 Transition 并发跑，
        // 这是这个库要经得住的一种真实用法。
        //
        // 走过行自己的动作而不是另写一套：行里那个动作就是这条案例的定义，两处各写一份必然漂移。
        // 逐行载荷（over.conf / over.live）在这一路里由**最后一条被发起的采样器行**写下，这与那两行载荷
        // 自己的说法一致（"最后被激活的那一行产出了什么"）；要每一行的结果，读 over.batch。
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

        timer.Tick += (s, e) =>
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
            SamplerProbe.Property(sampler).SetValue(subject, SamplerProbe.Start(sampler));
        }
    }

    private void ResetAllSamplers()
    {
        _benchTimer?.Stop();
        _batchTimer?.Stop();
        _liveSubject = null;
        ResetSamplerRows();
    }

    /// <summary>上一拍每一行的值，用来判断"这一拍还在不在变"。按键是采样器名。</summary>
    private readonly Dictionary<string, double[]> _rowPrevious = new(StringComparer.Ordinal);

    /// <summary>
    /// 行数 / 偏离声明起点的行数 / 相对上一拍仍在变的行数。
    /// </summary>
    /// <remarks>
    /// 顶栏那三个按钮唯一的可观测量，与加载模式那边的 <c>nomutual</c> 同一个思路：它不解释动画该到哪，
    /// 只说明有没有在动、有没有回到起点。
    /// <para>
    /// <b>两个数得分开，缺一个就有假命题。</b> 只看"偏离起点"，静息时也是满的 —— 每行初始持有的是控件的默认值
    /// （<c>null</c> 画刷、灰底色、默认圆角），都不是采样器声明的起点，于是"全部启动后 &gt; 0"在什么都没跑时
    /// 也成立。所以进界面时先把每一行写成它声明的起点（见 Loaded），并另记一个"这一拍还在变"：
    /// 静息 0 / 启动后满 / 停止后 0 / 重置后 0 且回到起点。
    /// </para>
    /// </remarks>
    private (int Rows, int Away, int Moving) RowState()
    {
        var away = 0;
        var moving = 0;

        foreach (var sampler in SamplerProbe.SamplerNames)
        {
            var now = SamplerProbe.Read(_bench.SubjectFor(sampler), sampler);

            if (!SamplerProbe.MatchesStart(sampler, now)) away++;

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

        timer.Tick += (s, e) =>
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
    // 验收观测面
    //
    // 人类读数之外再写一份机器可读的载荷：同一支定时器、同一批值，但用固定的 key=value 而不是散文，
    // 测试就不必去解析一份随时可能被重新排版的版式。载荷只报告"每个目标当前/峰值是多少"，至于哪个场景
    // 动哪个目标、起止与时长，由测试侧的 manifest 声明 —— 观测与语义各自只有一处来源。
    // -----------------------------------------------------------------------------------------------

    private readonly Stopwatch _scenarioClock = new();
    /// <summary>每个过冲目标各自的峰值。第五条是位移那第二条曲线（Elastic）自己的目标。</summary>
    private readonly double[] _targetPeaks = new double[5];
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
        var elasticX = ((TranslateTransform)Over4.RenderTransform).X;
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
             + $"t1.cur={Describe(Over1.Fill)};"
             + $"t2.cur={width:F3};t2.peak={Peak(2)};"
             + $"t3.cur={Describe(Over3.Fill)};"
             + $"t4.cur={elasticX:F3};t4.peak={Peak(4)};"
             + RecState("r0", Rec0) + RecState("r1", Rec1) + RecState("r2", Rec2)
             // 时间轴那四个字段同样排在 nomutual 之前。pos 按毫秒取整报出：读的是当前这一程内的偏移，
             // 而不是整条动画的位置 —— 程是独立的，跨程的位置没有意义。
             + TimelineState(Rec0)
             // rows/away/moving 排在 nomutual **之前**：后者是加载模式那半必须读到的最后一个字段，
             // 而它在标签里本来就顶到了高度上限（WinForms 那侧为它把标签加高到两行过）。
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
    /// 那三个动画各自带 auto-reverse 与 loop，终点要靠复算库的语义才知道，测试不去复算它。
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

    // -----------------------------------------------------------------------------------------------
    // 时间轴控制
    //
    // 这一排作用在加载模式那三块长动画（Rec0/1/2）上，而不是下面 900ms 的一次性过冲：暂停一个 900ms 的过冲
    // 在屏幕上和"它就是这么快"分不开，而这里同时也要给人看。三块各自是一条真 Transition，暂停/变速/定位都按
    // target 寻址，所以是逐个调用 —— 这本身就是"控制面挂在 target 上、不挂在快照上"的一次演示。
    // -----------------------------------------------------------------------------------------------

    private Rectangle[] ControlTargets() => [Rec0, Rec1, Rec2];

    private void PauseAll(object sender, RoutedEventArgs e)
    {
        foreach (var target in ControlTargets())
        {
            Transition.Pause(target, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    private void ResumeAll(object sender, RoutedEventArgs e)
    {
        foreach (var target in ControlTargets())
        {
            Transition.Resume(target, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    private void RateSlow(object sender, RoutedEventArgs e) => SetRate(0.25d);

    private void RateFast(object sender, RoutedEventArgs e) => SetRate(4d);

    /// <summary>
    /// 正常速。把速率调回 1。时间轴只有正速率 —— 减速之后要回到原速就靠这一个，而不是再去点一次重置。
    /// </summary>
    private void RateNormal(object sender, RoutedEventArgs e) => SetRate(1d);

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
    private void SeekNextPass(object sender, RoutedEventArgs e)
    {
        foreach (var target in ControlTargets())
        {
            Transition.Seek(target, Transition.Cycle(target, IncludeMutual: true, IncludeNoMutual: true) + 1,
                TimeSpan.Zero, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    private void ExitAll(object sender, RoutedEventArgs e)
    {
        // IncludeMutual   indicates whether to end animations configured with CanMutualTask: true
        // IncludeNoMutual indicates whether to end animations configured with CanMutualTask: false
        Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

        // "停止全部"是整块界面的停止：案例列表里的每一行也在内，否则这个按钮的名字就是假的。
        _benchTimer?.Stop();
        _batchTimer?.Stop();
        _liveSubject = null;
        StopSamplerRows();
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
    //
    // 行程按**这一行自己的台子**定，不是按窗口宽度定的：三块目标现在各在列表的一行里，跑出行外会被裁掉 ——
    // 而"跑出去看不见"与"没在跑"在屏幕上分不开。验收侧只钉静止态（CreateResetRec0/1/2），行程是观感，不是契约。
    private static readonly Transition<Rectangle> Animation0 =
        Transition<Rectangle>.Create()
            .Property(r => r.Opacity, 0)
            .Property(r => ((TranslateTransform)r.RenderTransform).X, LoadTravel)
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
                new TranslateTransform(LoadTravel - 40, 0),
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

    /// <summary>
    /// 跑一条过冲案例：先把目标放回起点，再起动画。
    /// </summary>
    /// <remarks>
    /// 先放回起点是必须的：<c>Prepare</c> 读的是**目标此刻的值**当起点，不放回去就变成"从终点动到终点"，
    /// 看上去什么都没发生。位移那两条共用同一块目标（好让两条曲线同屏对比），所以这一步对它们尤其要紧。
    /// </remarks>
    private void RunOvershoot(
        Rectangle target, Transition<Rectangle> animation, string scenario, int durationMs, int targetIndex, Action restoreStart)
    {
        Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        restoreStart();
        BeginScenario(scenario, durationMs, targetIndex);
        animation.Execute(target);
    }

    // 颜色场景的起点。
    private static readonly Color OverColorStart = Color.FromRgb(0x3A, 0x6E, 0xA5);

    // ---- 每一行"重置"的目标状态：就是这些元素声明时的那份值 ----

    // 加载那三行走的是与全局重置同一条路径：同步套用，不走异步流水线。
    private void RestoreTranslateRow() => ApplyReset(CreateResetRec0(), Rec0);

    // Rec1/Rec2 的 RenderTransform 得**直接赋 null**，不走构建器：适配器的 Transform 重载收的是集合，
    // 传空集合建出来的是一个空的 TransformGroup，不是 null —— 而声明的静止态就是"没有变换"。
    private void RestoreRotateRow()
    {
        Rec1.RenderTransform = null;
        ApplyReset(CreateResetRec1(), Rec1);
    }

    private void RestoreCombineRow()
    {
        Rec2.RenderTransform = null;
        ApplyReset(CreateResetRec2(), Rec2);
    }

    private void RestoreShiftStart() => ((TranslateTransform)Over0.RenderTransform).X = 0;

    private void RestoreElasticStart() => ((TranslateTransform)Over4.RenderTransform).X = 0;

    private void RestoreColorStart() => Over1.Fill = new SolidColorBrush(OverColorStart);

    private void RestoreSizeStart() => Over2.Width = 80;

    private void RestoreGradientStart() => Over3.Fill = CreateBs1Brush();

    private void ResetOverShoot()
    {
        foreach (var target in new[] { Over0, Over1, Over2, Over3, Over4 })
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

        RestoreShiftStart();
        RestoreElasticStart();
        RestoreColorStart();
        RestoreSizeStart();
        RestoreGradientStart();
    }
}