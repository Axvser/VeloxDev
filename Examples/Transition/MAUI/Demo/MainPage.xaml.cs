using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;
using VeloxDev.TransitionSystem;

// 同名类型一律显式取 MAUI 的那一侧：System.Drawing 里也有一份 PointF/RectF/SizeF，量纲不同、不能混。
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Demo
{
    /// <summary>
    /// Standalone animation test for the VeloxDev.MAUI PlatformAdapters (TransitionSystem).
    /// </summary>
    /// <remarks>
    /// 版面与其余几个平台的 Transition demo 对齐（见 <c>MainPage.xaml</c>）：顶栏（全局三件 + 五种加载方式
    /// + 载荷读数）压在上面，下面是一张**可滚动的案例列表** —— 一条案例一行，行里是那条案例真正在动的元素、
    /// 一句"这条在验什么"，以及这一行自己的 启动 / 关闭 / 重置。
    /// <para>
    /// 三种行（加载 3 条、过冲 5 条、每条采样器 1 条）长得一样，区别只在各自的令牌、描述与动作里，三条合成一处
    /// 的定义在 <see cref="RebuildCaseList"/>。元素由代码造、由行带进列表：一个元素只能有一个父级，而每一行各自
    /// 是一个父级 —— 位移那两条曲线因此各有一块自己的目标。
    /// </para>
    /// </remarks>
    public partial class MainPage : ContentPage
    {
        /// <summary>进界面时把每一行写成它自己声明的起点，且只做一次。</summary>
        private bool _resetInitialized;

        public MainPage()
        {
            InitializeComponent();

            RebuildCaseList();
            CaseRows.Children.Add(SamplerBench.Build(_caseRows));
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (_resetInitialized) return;
            _resetInitialized = true;

            // "重置"是整块界面的重置，所以它收下三类行各自的重置动作 —— 少一类，这个名字就与它做的事对不上。
            btnReset.Clicked += (s, e) => ResetAllCases();

            // 过冲读数：用定时器轮询目标的真实属性，而不是订阅 effect 的事件——流水线每段都会 Clone 一份
            // effect，订阅在原始 effect 上的处理函数根本不会触发。MAUI 没有 DispatcherPriority，
            // 走的是 NonPriority 路径，这里也是那条无优先级分支的真机覆盖。
            var readoutTimer = Dispatcher.CreateTimer();
            readoutTimer.Interval = TimeSpan.FromMilliseconds(40);
            readoutTimer.Tick += (s, e) => UpdateReadout();
            readoutTimer.Start();

            // 每一行先写成它自己声明的起点。不这么做的话，静息时每行持有的是控件的默认值（null 画刷、默认圆点、
            // 零尺寸），都不是采样器声明的起点，于是"偏离起点"的行数在什么都没跑的时候就是满的 —— 顶栏那个数
            // 会变成一句假命题。
            ResetSamplerRows();
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
                "嵌套属性路径：动画写的是 TranslationX —— 元素自己的位移，而且是被就地改的那一个。",
                Rec0, () => Animation0.Execute(Rec0), RestoreTranslateRow));

            _caseRows.Add(LoadRow("rotate", "旋转",
                "整个旋转被换掉：端点是 180 度，绕元素自己的中心转，逐字段外推而不是逐分量改。",
                Rec1, () => Animation1.Execute(Rec1), RestoreRotateRow));

            _caseRows.Add(LoadRow("combine", "平移 + 缩放",
                "位移、三维翻转与缩放落在同一段上，第二段还会转去动颜色。",
                Rec2, () => Animation2.Execute(Rec2), RestoreCombineRow));

            _caseRows.Add(OvershootRow("shift-back", "位移 Back.Out",
                "共享进度的标量过冲：Back 越过目标 10% 再落回来，数字看得出来、眼睛看不出来。",
                Over0, ShiftStageWidth, () => RunOvershoot(Over0, OverScalarBack, "back", BackDurationMs, 0, RestoreShiftStart), RestoreShiftStart));

            _caseRows.Add(OvershootRow("shift-elastic", "位移 Elastic.Out",
                "上面那一行的另一条曲线：Elastic 峰值更高、回弹次数更多，两行同时跑就是同一次对比。",
                Over4, ShiftStageWidth, () => RunOvershoot(Over4, OverScalarElastic, "elastic", ElasticDurationMs, 4, RestoreElasticStart), RestoreElasticStart));

            _caseRows.Add(OvershootRow("color", "颜色过冲",
                "四通道按颜色的规则走：共用进度撞到上限就整组停住，所以动的是亮度而不是色相。",
                Over1, BodyStageWidth, () => RunOvershoot(Over1, OverColor, "color", BackDurationMs, 1, RestoreColorStart), RestoreColorStart));

            _caseRows.Add(OvershootRow("size", "尺寸过冲",
                "宽度是 double，没有上下限：外推照走，只是这一条只会变大，碰不到负的那一端。",
                Over2, SizeStageWidth, () => RunOvershoot(Over2, OverSize, "size", ElasticDurationMs, 2, RestoreSizeStart), RestoreSizeStart));

            _caseRows.Add(OvershootRow("brush", "渐变过冲",
                "非纯色画刷走交叉淡出：混合系数是个分数，两端各自饱和，不跟着缓动越过端点。",
                Over3, BodyStageWidth, () => RunOvershoot(Over3, OverBrush, "brush", ElasticDurationMs, 3, RestoreGradientStart), RestoreGradientStart));

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
        /// 元素区的宽度。位移那两条要放得下整段行程（端点是 230，方块本身 80，Elastic 的峰值还要再高 37%），
        /// 不按原值留出位置的话，它一跑就整块滑出格子 —— 看上去和"没动"一模一样，正是这个演示要消除的错觉。
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
        /// 把元素放进一块定宽、等高、裁边的台子里 —— 它的行程跑到端点也不会压到旁边的字上。
        /// </summary>
        /// <remarks>
        /// 台子的宽由那一条案例的行程决定（见各条案例的 <c>*StageWidth</c>）：这是"这一段动哪儿"的唯一来源，
        /// 窄一点就会让元素在最该被看见的那一瞬跑出边界 —— 而"跑出去看不见"和"没在跑"在屏幕上分不开。
        /// MAUI 里位移是元素自己的 <c>TranslationX</c>（渲染期的平移，不改布局），所以起点由 <c>Margin</c> 摆，
        /// 台子的裁切由 <c>IsClippedToBounds</c> 给。
        /// </remarks>
        private static Grid PlayStage(View element, double width)
        {
            var stage = SamplerBench.Stage(width);
            stage.HorizontalOptions = LayoutOptions.Start;
            stage.VerticalOptions = LayoutOptions.Center;

            element.HorizontalOptions = LayoutOptions.Start;
            element.VerticalOptions = LayoutOptions.Start;
            element.Margin = new Thickness(SamplerBench.ElementInset, SamplerBench.ElementTop, 0, 0);

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

        // 旋转与缩放绕元素自己的中心：行里的台子是裁边的，绕左上角转 180 度会把方块整个甩到台子外，而"跑出去看不见"
        // 与"没在跑"在屏幕上分不开。MAUI 的锚点默认就是中心（0.5/0.5），这里显式写出来 —— 它是"这一行装得下自己那段
        // 行程"的前提，不该靠默认值。
        private readonly Rectangle Rec0 = new() { Fill = CreateRec0Brush(), WidthRequest = 80, HeightRequest = 60 };

        private readonly Rectangle Rec1 = new()
        {
            Fill = new SolidColorBrush(Colors.Lime), WidthRequest = 80, HeightRequest = 60,
            AnchorX = 0.5, AnchorY = 0.5,
        };

        private readonly Rectangle Rec2 = new()
        {
            Fill = CreateBs1Brush(), WidthRequest = 80, HeightRequest = 60,
            AnchorX = 0.5, AnchorY = 0.5,
        };

        // 位移那两条就地驱动 TranslationX，所以起点在这里声明。
        private readonly Rectangle Over0 = new()
        {
            Fill = new SolidColorBrush(OverColorStart), WidthRequest = 80, HeightRequest = 60,
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
            Fill = new SolidColorBrush(OverColorStart), WidthRequest = 80, HeightRequest = 60,
        };

        private readonly Rectangle Over1 = new()
        {
            Fill = new SolidColorBrush(OverColorStart), WidthRequest = 80, HeightRequest = 60,
        };

        private readonly Rectangle Over2 = new()
        {
            Fill = new SolidColorBrush(new MauiColor(0x7A / 255f, 0x5A / 255f, 0xA5 / 255f, 1f)),
            WidthRequest = SizeStart, HeightRequest = 60,
        };

        private readonly Rectangle Over3 = new() { Fill = CreateBs1Brush(), WidthRequest = 80, HeightRequest = 60 };

        // -----------------------------------------------------------------------------------------------
        // 每一行元素的场地
        //
        // 行高是统一的（SamplerBench.RowHeight），所以每一行都装得下自己那段行程 —— 装不下就等于元素跑没了。
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// 位移那两条的元素区宽度：端点 230 外加上方块自己的 80，还要放下 Elastic 的峰值（230 × 1.37 ≈ 315）。
        /// </summary>
        /// <remarks>
        /// 目标定 230 而不是 300：Elastic 的峰值是目标的 1.37 倍，300 的峰值连着方块一起要 450 以上，台子得给到
        /// 460 才不裁 —— 那种台子会把这一行的元素框撑得比别的行宽一倍。行程收进台子里更划算：验收侧只钉静止态，
        /// 行程是观感，不是契约。
        /// </remarks>
        private const double ShiftStageWidth = 420d;

        private const double ShiftTarget = 230d;

        /// <summary>
        /// 加载那三行的元素区宽度，以及那三条动画的行程。
        /// </summary>
        /// <remarks>
        /// 行程原来按窗口宽度定（240 起，且三块目标直接摊在窗口上）。现在每一块在列表的一行里，台子就是它的边界，
        /// 跑出去会被裁掉 —— 而"跑出去看不见"与"没在跑"在屏幕上是一样的，正是这个演示要消除的错觉。
        /// 所以行程缩到台子里放得下，而这里的约束是第三条（平移 + 三维翻转 + 1.3 倍缩放）：它的方块在最极端
        /// 那一瞬伸到台内约 375 的位置（80 的方块绕自身中心放大 1.3 倍占 104，而 200 的行程在 MAUI 的缩放与
        /// 透视复合之后比 200 更远），所以台子按位移那两条一样给 420 —— 整个方块始终在台子里，而不是被边界
        /// 切掉一截。另外两条（只平移 200 的那条、绕中心翻转的那条）都比它窄。
        /// </remarks>
        private const double LoadStageWidth = 420d;

        private const double LoadTravel = 200d;

        /// <summary>
        /// 尺寸那一条的元素区宽度：宽度从 80 长到 220，Elastic 峰值处（80 + 140 × 1.375 ≈ 272.5）也还留在台子里。
        /// </summary>
        private const double SizeStageWidth = 300d;

        /// <summary>颜色与渐变那两条不改变尺寸，元素区就是方块本身加一点边。</summary>
        private const double BodyStageWidth = 96d;

        // -----------------------------------------------------------------------------------------------
        // 过冲
        //
        // 上面每一条用到的缓动返回值都落在 [0,1] 内，所以没有一条能越过目标再回来。Back 峰值 1.10、
        // Elastic 峰值 1.37。过冲能不能真的看到，取决于目标属性落在哪个采样器上：
        //   - 数值（TranslationX、WidthRequest）自由外推，是真正的过冲；
        //   - 颜色的 R/G/B 共用一个进度、在第一个触到上限的通道处整组停住；
        //   - 非纯色画刷走交叉淡出，系数是个分数，只在两端饱和。
        // 读数是让过冲可见的东西：数字越过目标再回来，肉眼无法把它和一条更慢的缓动区分开。
        // -----------------------------------------------------------------------------------------------

        private const double WidthTarget = 220d;

        // 过冲条在声明的初值：尺寸类的起点与颜色的起始色。起始值只有这一份来源，逐场景重置和整条重置都读它。
        private const double SizeStart = 80d;

        private static readonly MauiColor OverColorStart = new(0x3A / 255f, 0x6E / 255f, 0xA5 / 255f, 1f);

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
                .Property(r => r.Fill, new SolidColorBrush(new MauiColor(0x80 / 255f, 0x80 / 255f, 0xD0 / 255f, 1f)))
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

        /// <summary>
        /// 跑一条过冲案例：先把目标放回起点，再起动画。
        /// </summary>
        /// <remarks>
        /// 先放回起点是必须的：<c>Prepare</c> 读的是**目标此刻的值**当起点，不放回去就变成"从终点动到终点"，
        /// 看上去什么都没发生。只重置本行自己的元素：重置整条过冲会取消别的行上正在跑的那一次，
        /// 而"两行同时跑就是同一次对比"正是这几行存在的意思。
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
        private void RestoreTranslateRow() => ApplyReset(CreateRec0Reset(), Rec0);

        private void RestoreRotateRow() => ApplyReset(CreateRec1Reset(), Rec1);

        private void RestoreCombineRow() => ApplyReset(CreateRec2Reset(), Rec2);

        private void RestoreShiftStart() => Over0.TranslationX = 0;

        private void RestoreElasticStart() => Over4.TranslationX = 0;

        private void RestoreColorStart() => Over1.Fill = new SolidColorBrush(OverColorStart);

        private void RestoreSizeStart() => Over2.WidthRequest = SizeStart;

        private void RestoreGradientStart() => Over3.Fill = CreateBs1Brush();

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
        private Rectangle[] ScenarioTargets() => [Rec0, Rec1, Rec2, Over0, Over1, Over2, Over3, Over4];

        /// <summary>过冲那五块目标。</summary>
        private Rectangle[] OvershootTargets() => [Over0, Over1, Over2, Over3, Over4];

        // -----------------------------------------------------------------------------------------------
        // 采样器：演出与批量
        // -----------------------------------------------------------------------------------------------

        /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
        private long _probeSequence;

        // 只留一支演出用的定时器：连点两个把手时，后一次要能叫停前一次，否则两条采样器会同时往各自的格子里写。
        private IDispatcherTimer? _benchTimer;

        // 上一次演出写的那一格：换一格时要把它那条真动画停掉，不然它会在没人看的时候继续往旧格子上写。
        private SamplerSubject? _liveSubject;

        // 每一条采样器**各有一份**采样累积：逐行路径只用被点的那一份，批量路径十几份同时喂。
        // 异常也记在各自那一份里，所以十几条并发时谁的错是认得出的。
        private readonly Dictionary<string, SamplerProbe.LiveWatch> _watches = new(StringComparer.Ordinal);

        // 批量运行时每一行的闭式解帧（点那一刻算一次，不随动画走）与那支采样定时器。
        private readonly Dictionary<string, string> _batchFrames = new(StringComparer.Ordinal);
        private IDispatcherTimer? _batchTimer;

        /// <summary>
        /// 这一行的"启动"：验五个固定缓动时间，再起一条真动画把这一条跑一遍。
        /// </summary>
        /// <remarks>
        /// 采样器写在**这一格的在屏控件**上、载荷也从它读回，所以下面两件事是同一件事的两种读法。
        /// 这一条同时是套件点的那个把手令牌走的路径，也是行里"启动"按钮走的路径 —— 两者不可能各说各话。
        /// <para>
        /// 扫描落在点击处理函数里、也就是 UI 线程上 —— 它要构造画刷、阴影、变换这类有线程亲和性的对象；
        /// 也正因为不在 Tick 里，那条「Tick 里的异常在 MAUI 上没人接」的陷阱不必碰。
        /// </para>
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
            var property = SamplerProbe.Property(sampler);
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
        /// 在这一路里由**最后一条被发起的采样器行**写下，要每一行的结果，读 over.batch。
        /// </remarks>
        private void StartAllCases(object sender, EventArgs e)
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
            // 采样器那一类走的是 BulkStart —— 行里那个"启动"会掐掉上一次被观察的行，批量这一路谁都不能动别人，
            // 否则十几条会变成"后一条掐掉前一条"，只剩最后一条在跑。
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
        /// <para>
        /// Tick 里的每一步都不许抛：MAUI 不接 Tick 里的异常，它直接冒泡成未处理异常（dotnet/maui #12245）。
        /// <see cref="SamplerProbe.Read"/> 与 <see cref="SamplerProbe.LiveWatch.Observe"/> 都按不抛写。
        /// </para>
        /// </remarks>
        private void StartBatchWatch(long sequence)
        {
            var clock = Stopwatch.StartNew();
            var timer = Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
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
            var payload = new System.Text.StringBuilder(
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
        /// <para>
        /// Tick 里的每一步都不许抛：MAUI 不接 Tick 里的异常，它直接冒泡成未处理异常（dotnet/maui #12245）。
        /// <see cref="SamplerProbe.Read"/> 与 <see cref="SamplerProbe.LiveWatch.Observe"/> 都按不抛写，
        /// 起动画时的异常则在下面就地收进**这一行自己的**观察里，而不是让一次点击把 demo 打挂。
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
                // 这里同步抛出的都是"这条路径根本起不来"（例如声明的路径不可采样）。照实报给验收。
                watch.Fail($"{exception.GetType().Name}: {exception.Message}");
            }

            // 点击那一刻先落一份 seq，验收侧靠它把"新的"与"上一次剩下的"分开。
            OverLive.Text = SamplerProbe.LiveWatch.Pending(sampler, sequence);

            var clock = Stopwatch.StartNew();
            var timer = Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
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
        // 顶栏那三件：整块界面一起启动 / 停止 / 重置
        // -----------------------------------------------------------------------------------------------

        /// <summary>顶栏"停止全部"：整块界面一起停下，否则这个按钮的名字就是假的。原地冻结，不回起点。</summary>
        private void ExitAll(object sender, EventArgs e)
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

            ResetCase(Rec0, RestoreTranslateRow);
            ResetCase(Rec1, RestoreRotateRow);
            ResetCase(Rec2, RestoreCombineRow);
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

        // 读数只取目标的真实属性，不缓存也不伪造。载荷与读数共用这一次采样，两者不可能互相矛盾。
        // 每一步都不许抛：IDispatcherTimer.Tick 里的异常在 MAUI 上不会被框架接住，会直接冒泡成未处理异常
        // （dotnet/maui #12245）。MAUI 没有 DispatcherPriority，这条读数同时是 NonPriority 采样路径的真机覆盖。
        private void UpdateReadout()
        {
            var color = Over1.Fill is SolidColorBrush solid ? solid.Color : Colors.Transparent;
            var stops = Over3.Fill is LinearGradientBrush gradient ? gradient.GradientStops : null;
            var offset = stops is { Count: > 0 } ? stops[0].Offset : float.NaN;

            Readout.Text =
                $"位移 Back   当前 {Over0.TranslationX,7:F1}"
                + $"   |   Elastic   当前 {Over4.TranslationX,7:F1}"
                + $"   |   宽度 目标 {WidthTarget,5:F1}   当前 {Over2.WidthRequest,7:F1}\n"
                + $"颜色 R/G/B 当前 {255 * color.Red,4:F0}/{255 * color.Green,4:F0}/{255 * color.Blue,4:F0}"
                + $"   |   渐变 stop0 目标 {GradientStopTarget,4:F2}  当前 {offset,5:F3}";
            OverState.Text = BuildState();
        }

        private string BuildState()
        {
            _sequence++;

            // 只有标量目标记录峰值。峰值存在的意义是"刀刃型峰"——采样落在尖峰两侧就会低估它；颜色的过冲是一段
            // 形状而不是一个尖峰，报当前值就够，测试轮询取最大即可。
            var x = Over0.TranslationX;
            var elasticX = Over4.TranslationX;
            var width = Over2.WidthRequest;
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
                 // rows/away/moving 排在 nomutual **之前**：后者是加载模式那半必须读到的最后一个字段，
                 // 所以它排在最后，标签也得为这一份更长的载荷留出折行的位置。
                 + $"rows={row.Rows};away={row.Away};moving={row.Moving};"
                 + $"nomutual={NoMutualCount()};";
        }

        /// <summary>
        /// 加载模式那一排三块目标的状态：动画真正写的那些属性，读出来报给测试。
        /// </summary>
        /// <remarks>
        /// 与过冲那几行同一支定时器、同一次采样，所以两者不可能不一致。报的是"动的是什么"而不是"应该动到哪" ——
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

        /// <summary>上一拍每一行的值，用来判断"这一拍还在不在变"。按键是采样器名。</summary>
        private readonly Dictionary<string, double[]> _rowPrevious = new(StringComparer.Ordinal);

        /// <summary>
        /// 行数 / 偏离声明起点的行数 / 相对上一拍仍在变的行数。
        /// </summary>
        /// <remarks>
        /// 顶栏那三个按钮唯一的可观测量，与加载模式那边的 <c>nomutual</c> 同一个思路：它不解释动画该到哪，
        /// 只说明有没有在动、有没有回到起点。只数采样器行 —— 别的行的"起点"由各自的台子宣布，不在这里说话。
        /// <para>
        /// <b>两个数得分开，缺一个就有假命题。</b> 只看"偏离起点"，静息时也是满的 —— 每行初始持有的是控件的
        /// 默认值（<c>null</c> 画刷、灰底色、默认圆角），不是采样器声明的起点，于是"全部启动后 &gt; 0"在什么都
        /// 没跑时也成立。所以进界面时先把每一行写成它声明的起点（见 <c>OnAppearing</c>），并另记一个"这一拍还在
        /// 变"：静息 0 / 启动后满 / 停止后 0 / 重置后 0 且回到起点。
        /// </para>
        /// <para>
        /// <b>两个数都是"这一拍"的快照，不是"这一批跑起来了"的判据。</b> 一行刚起动画的头几帧里，它的分量可能与
        /// 声明的起点**逐位相同**：颜色那一条尤其如此（<c>ColorSampler</c> 的通道是 <c>byte</c>，截断之后连续几帧
        /// 都还停在起点的字节上）。所以要问"这一批起来了没有"，得**等** <c>away</c> 追上该有的行数，
        /// 不能拿某一拍去断言（见验收侧的 ScanToolbar）。
        /// </para>
        /// <para>
        /// 每一步都不许抛：这是在 IDispatcherTimer.Tick 里跑的。
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

        // -----------------------------------------------------------------------------------------------
        // 加载模式那五条路径：直接起 / 后台起 / 并发起 / 后台并发起 / 连点
        // -----------------------------------------------------------------------------------------------

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
    }

    public partial class MainPage
    {
        // -----------------------------------------------------------------------------------------------
        // 加载：那三条动画
        //
        // 行程按**这一行自己的台子**定，不是按窗口宽度定的：三块目标现在各在列表的一行里，台子就是它的边界，
        // 跑出行外会被裁掉 —— 而"跑出去看不见"与"没在跑"在屏幕上分不开。验收侧只钉静止态
        // （CreateRec0/1/2Reset），行程是观感，不是契约。
        // -----------------------------------------------------------------------------------------------

        // Simple animation: translate + demonstrates a nested property path, directly modifying
        // TranslationX, together with gradient changes.
        private static readonly Transition<Rectangle> Animation0 =
            Transition<Rectangle>.Create()
                .Property(r => r.TranslationX, LoadTravel)
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

        // The Bs1 gradient (Yellow → Violet, 0,0 → 1,1) is rebuilt in code because a resource is not a value a
        // transition path can point at. Keep this the one place it is defined: Rec2's reset animates Fill back to
        // this brush, and the two gradient stops of the demo are it too.
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

        // Delayed animation - rotation about the element's own centre (see the AnchorX/AnchorY on the
        // rectangles): the row's stage is clipped, so rotating about the top-left corner would fling the
        // square clean out of it.
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
                // First segment: translate + rotate + scale
                .Property(r => r.RotationX, 180)
                .Property(r => r.RotationY, 180)
                .Property(r => r.TranslationX, LoadTravel)
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
    }
}
