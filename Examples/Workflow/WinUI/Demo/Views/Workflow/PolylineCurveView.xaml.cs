using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.ComponentModel;
using System.Numerics;
using Demo.Workflow;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views;

/// <summary>
/// One connection between two ports, drawn as a single cubic curve that leaves each end horizontally.
/// <para>
/// The light travelling along it is a comet — a bright head, a tail that fades behind it, and a halo that
/// follows the head — and it is cut out of the curve <b>by arc length</b> rather than by a gradient brush.
/// A <see cref="LinearGradientBrush"/>'s axis is the straight line between the two ends, so on a curve it
/// lights the string rather than the rope: the brightness stops tracking the bend, and the light appears to
/// speed up and slow down as it goes round. The arc-length table is the Core
/// <see cref="LinkCurve"/> this view publishes, so drawing and hit-testing share one geometry.
/// </para>
/// <para>
/// <b>What "no OnRender" costs, and how it is paid here.</b> The Avalonia original draws each arc-length
/// slice with <c>DrawingContext.DrawGeometry</c> in one pass. WinUI is retained mode: there is no per-frame
/// draw hook, and each stroked slice needs its own brush, so a slice has to <i>be</i> an element. This view
/// therefore owns a fixed pool of <see cref="Path"/> elements — one per tail slice, one per halo slice, plus
/// three for the resting line — and rewrites only their three points, their colour and their thickness each
/// frame. Nothing is created or destroyed while the animation runs, which is what keeps a per-frame path
/// rewrite affordable. The alternative, a Win2D <c>CanvasControl</c>, would draw this in a single pass and
/// match Avalonia's output more cheaply, but Win2D is not a dependency of this demo (see Demo.csproj) and
/// adding a graphics stack to port a decoration is not a trade this repo makes.
/// </para>
/// <para>
/// It draws nothing at all until both endpoints carry measured coordinates: an unmeasured slot's anchor is
/// <see cref="double.NaN"/>, and a retained <see cref="Path"/> paints whatever its geometry holds, so a NaN point
/// is a line that never appears rather than a line drawn in the wrong place.
/// </para>
/// <para>
/// The type keeps the name <c>PolylineCurveView</c> because the surface template binds it by that name; it has
/// not drawn a polyline since the geometry was replaced.
/// </para>
/// <para>
/// The view is a painting surface only: it publishes the <see cref="LinkCurve"/> it draws
/// (<see cref="LinkHitTestEx.PublishCurve"/>) in canvas-local coordinates, and the shared hit test
/// (<see cref="LinkHitTestEx"/>) decides what the pointer is on. Nothing here is hit-testable — the view is
/// canvas sized, so a hit-testable one would swallow every canvas gesture.
/// </para>
/// <para>
/// The hover highlight is this demo's: the view subscribes to this link's own
/// <see cref="IInputEvents"/> pointer events and writes <see cref="IsHighlighted"/> when the hovered link is
/// its own. Deleting and the right-click menu belong to the demo's surface too, off the same input route.
/// </para>
/// </summary>
public sealed partial class PolylineCurveView : UserControl
{
    // 控制点的最小水平拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段，失去「从端口水平出来」的形状。
    // 与 Core 的 LinkCurve.BuildCubic 取同一个下限；发布给命中的曲线也用这一份。
    private const double PullMinimum = 40d;

    // 拖尾占全长的比例。这是彗星唯一的观感旋钮：调大＝更长的尾、更像流光；调小＝更像一个亮点在跑。
    private const double TailFraction = 0.30;

    // 拖尾分几段画。每段一个不透明度，衰减因此是连续的而不需要渐变刷。
    private const int TailSegments = 16;

    // 光晕分几段画。它比本体少一半：光晕最亮处也只有 0.22，八级台阶在这里看不出来，
    // 而保留模式下每一段都是一个真元素 —— 这一档是平台成本换来的，不是观感上的让步。
    private const int BloomSegments = 8;

    // 光晕的两档宽度与不透明度（相对本体厚度）
    private const double BloomWidth = 9;
    private const double BloomAlpha = 0.22;

    // 视图自己的盒子按曲线的包围盒来定，而不是按画布尺寸：曲线可以伸出画布（节点的锚点/尺寸不必落在
    // OriginSize 内），画布大小的盒子会把它裁在画布边缘；合成层的 ShapeVisual 又是按自己的 Size 裁的。
    // 这个 padding 覆盖光晕半宽、线宽与圆头。
    private const double BoxPadding = BloomWidth / 2 + 8;

    // 三段相位各自结束时头部走过的比例：出发、行进、到达
    private const double BandFormed = 0.30;
    private const double BandLeaving = 0.78;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(450);

    // 平台驱动只装一次：共享时钟负责周期与订户计数，本类只提供 WinUI 的 Transition 构造方式。
    static PolylineCurveView()
    {
        WorkflowFlowClock.RegisterDriver(static clock => new FlowDriver(clock));
    }

    private static readonly DoubleCollection VirtualStrokeDashArray = [4, 2];

    // 虚拟连线是唯一实现 IConnectionPreviewLink 的链接：发起 / 有效候选 / 无效候选是它的阶段。
    private IConnectionPreviewLink? _previewLink;
    private ConnectionPreviewState _preview;

    // 阶段色是本 demo 的外观：有效绿、无效红，其余沿用模板给的线色。
    private static readonly Windows.UI.Color PreviewValidColor = Windows.UI.Color.FromArgb(0xFF, 0x34, 0xD3, 0x99);
    private static readonly Windows.UI.Color PreviewInvalidColor = Windows.UI.Color.FromArgb(0xFF, 0xF8, 0x71, 0x71);

    // 一条「按弧长切出来的描边」在合成层里的全部家当：一个 sprite shape、一份可动端点的线段几何、一支可改色的画刷。
    // 三者都只建一次，之后每帧只写两个端点与一个颜色。
    // 用合成对象而不是 XAML 的 Path，是因为合成对象不参与 XAML 布局：Path 一改几何就向上失效整个画布的测量
    // （实测：空闲不动时每秒仍要重排 60 次），而这里一次都不会有。
    private sealed class CometStrip
    {
        public required CompositionSpriteShape Shape { get; init; }
        public required CompositionLineGeometry Geometry { get; init; }
        public required CompositionColorBrush Brush { get; init; }
    }

    /// <summary>
    /// One layer of the resting line: a <see cref="Path"/> and the cubic it draws.
    /// </summary>
    /// <remarks>
    /// There are three of these — halo, glow, body — and each owns its <b>own</b> geometry even though all three
    /// draw the very same curve. Sharing one geometry between them does not work in WinUI: assigning a
    /// <see cref="Geometry"/> to a <see cref="Path"/> parents it, and handing the same instance to a second path
    /// throws <c>E_INVALIDARG</c> ("Value does not fall within the expected range") — observed at runtime, not at
    /// build time. Avalonia has no such rule: there the three layers are three <c>DrawGeometry</c> calls against one
    /// <c>StreamGeometry</c>, because a drawing context is a picture rather than a retained tree it takes ownership
    /// of. Hence the same "one curve, three strokes" design costs three coupled geometries here (see
    /// <see cref="RefreshGeometry"/>, which writes all three).
    /// </remarks>
    private sealed class RestingStroke
    {
        public required Path Path { get; init; }
        public required PathGeometry Geometry { get; init; }
        public required PathFigure Figure { get; init; }
        public required BezierSegment Segment { get; init; }
        public required SolidColorBrush Brush { get; init; }
    }

    private readonly Grid _container;
    private readonly RestingStroke _halo;
    private readonly RestingStroke _glow;
    private readonly RestingStroke _line;

    // 三层一起改的那一份：端点一变，三条曲线要同步写一次
    private readonly RestingStroke[] _resting;

    // 彗星整层挂在合成树上，所以这里多一层什么都不画的 Grid 当载体：它排在静息线之后，
    // 合成子视觉因此盖在静息线之上（子视觉与 XAML 内容的先后不好赌，用一个末位的空元素把顺序钉死）
    private readonly Grid _cometLayer = new() { Clip = null, IsHitTestVisible = false };
    private ShapeVisual? _cometVisual;
    private CometStrip[]? _tail;
    private CometStrip[]? _bloom;

    // 弧长表与命中几何都归 Core 的 LinkCurve：本视图只留一份引用，不再自建采样/累计长度。
    // 端点变化时重建，绘制与发布共用同一条曲线 —— 画出来的与能命中的因此不会分叉。
    private LinkCurve? _curve;

    // 本视图当前描述的链接（池化复用会把 DataContext 改到另一条 / 置空），撤曲线时要认它。
    private IWorkflowLinkViewModel? _boundLink;

    // 包围盒原点：几何写成「原始 canvas-local 坐标 − 原点」，元素再平移回原点，画出来的位置仍是原始坐标；
    // 形状因此都落在本视图的盒子里，合成层不会在盒子边缘把它们裁掉。
    // 用 RenderTransform 而不是 Canvas.Left/Top：虚拟连线的载体是 Grid，不是 Canvas，Canvas 定位对它无效。
    private double _boxMinX;
    private double _boxMinY;
    private readonly TranslateTransform _boxTransform = new();

    private bool _isLoaded;

    // 本视图订的那棵树共用的时钟；每次 tick 把它的两个标量抄进本视图的字段并重画一次。
    private WorkflowFlowClock? _clock;

    // 每帧一次重画的入口。存成字段只建一次：就地写 lambda 会捕获 this，那就是每帧一次分配。
    private readonly Action _clockTick;

    public PolylineCurveView()
    {
        _clockTick = OnClockTick;
        InitializeComponent();
        Canvas.SetZIndex(this, -100);

        // 曲线的几何是画布局部坐标，深缩放时负的 top/left 会落到这个元素自己的框外。WinUI 默认把元素内容
        // 裁到框内，除非整条链上的 Clip 都是 null（同目录 NodeView 用的也是这个办法）；Avalonia 那边是
        // 自绘的，从来没有裁剪这回事。
        _container = new Grid { Clip = null };
        RenderTransform = _boxTransform;

        // 三层都不可命中：这是整块画布大小的视图，可命中就会把画布手势整个吃掉。
        // 命中判定不落在元素上，由共享的 LinkHitTestEx 对发布出去的曲线做几何判定。
        _halo = CreateRestingStroke(_container, LineThickness + 9);
        _glow = CreateRestingStroke(_container, LineThickness + 4);
        _line = CreateRestingStroke(_container, LineThickness);
        _resting = [_halo, _glow, _line];

        // 彗星那 24 段不进 XAML 树，只留这一层当载体；真正建在 Loaded 里（合成器要等元素有了视觉才有）
        _container.Children.Add(_cometLayer);

        Content = _container;
        SizeChanged += (_, _) => SyncCometSize();

        // 先按依赖属性的默认值画一遍：端点要等绑定推下来，那时 OnChanged 会再跑
        Refresh();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        // 池化视图会被改绑给另一条链接，而回收/复用只置 Visibility 与 DataContext —— Loaded/Unloaded 都不触发，
        // 所以「换了链接」这件事只能从这里知道
        DataContextChanged += OnDataContextChanged;
        UpdateInteractivity();
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(true, OnChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Windows.UI.Color), typeof(PolylineCurveView), new PropertyMetadata(Windows.UI.Color.FromArgb(0xCC, 0x38, 0xBD, 0xF8), OnChanged));
    public static readonly DependencyProperty LineThicknessProperty =
        DependencyProperty.Register(nameof(LineThickness), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(2.0, OnChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty HighlightColorProperty =
        DependencyProperty.Register(nameof(HighlightColor), typeof(Windows.UI.Color), typeof(PolylineCurveView), new PropertyMetadata(Windows.UI.Color.FromArgb(0xCC, 0xE8, 0xFF, 0xFF), OnChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Windows.UI.Color LineColor { get => (Windows.UI.Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public double LineThickness { get => (double)GetValue(LineThicknessProperty); set => SetValue(LineThicknessProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }
    public Windows.UI.Color HighlightColor { get => (Windows.UI.Color)GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PolylineCurveView)d;
        control.UpdateInteractivity();

        if (e.Property == StartLeftProperty || e.Property == StartTopProperty
            || e.Property == EndLeftProperty || e.Property == EndTopProperty)
        {
            // 端点一动就把整幅重算一遍 —— 同步做，Avalonia 那边也是同步的：
            // 推迟一帧会让彗星追不上正在被拖的端点。重算也包含显隐：两端从「没量出来」变成真实坐标时，
            // 正是这一步让线当场出现，而不是等下一个属性碰巧变化
            control.Refresh();
            return;
        }

        if (e.Property == LineColorProperty || e.Property == LineThicknessProperty || e.Property == IsHighlightedProperty || e.Property == HighlightColorProperty)
        {
            control.UpdateRestingLine();
            control.RedrawComet();
            return;
        }

        // 两端测量完才可绘制，在那之前流光无物可循；虚拟链接是指针下的橡皮筋，没有稳定连接可描述
        if (e.Property == CanRenderProperty || e.Property == IsVirtualProperty)
        {
            control.Refresh();
        }
    }

    // 命中判定归共享的 LinkHitTestEx（几何式，走发布出去的曲线），视图因此整块不可命中 ——
    // 它是画布大小的，一旦可命中就会把画布手势全部吃掉。
    private void UpdateInteractivity()
    {
        IsHitTestVisible = false;
        IsTabStop = false;
    }

    #endregion

    #region Flow effect

    /// <summary>How far along the link the comet's head has travelled, as a fraction of its length.</summary>
    /// <remarks>
    /// Animated rather than computed, and the armature the whole comet hangs off: the value carries no geometry of
    /// its own — the arc-length table turns it into points — which is what lets the light follow a curve. A plain
    /// property rather than a dependency property, deliberately: the shared clock's tick copies it into the field
    /// and then redraws once, so the setter is not a second repaint entry. The clock itself is the transition's
    /// target; this view only samples it.
    /// </remarks>
    public double BandHead
    {
        get => _bandHead;
        set => _bandHead = value;
    }

    /// <summary>How lit the comet is: 0 while it is absent, 1 while it travels.</summary>
    public double BandIntensity
    {
        get => _bandIntensity;
        set => _bandIntensity = value;
    }

    private double _bandHead;
    private double _bandIntensity;

    // 订本树共用的时钟：只有真正在屏、可画、非虚拟、且挂在某棵树上的链接才加入。
    // 池化改绑会先退旧树再订新树，摘树则退订；订阅数归零时时钟自己停表（一棵树始终只有一条周期）。
    private void RefreshFlowSubscription()
    {
        var link = DataContext as IWorkflowLinkViewModel;
        var clock = _isLoaded && !IsVirtual && RenderReady
            ? WorkflowFlowClock.ForLink(link)
            : null;

        if (ReferenceEquals(clock, _clock)) return;

        if (_clock is not null)
        {
            _clock.Unsubscribe(_clockTick);

            // 旧一帧的光不该留在改绑后的新链接上；下一帧由新时钟重新写入。
            _bandHead = 0d;
            _bandIntensity = 0d;
            RedrawComet();
        }

        _clock = clock;
        _clock?.Subscribe(_clockTick);
    }

    // 每帧只抄两个标量并重画一次：setter 不自己重画，两个值都落地后合成一次结果。
    private void OnClockTick()
    {
        var clock = _clock;
        if (clock is null) return;

        _bandHead = clock.BandHead;
        _bandIntensity = clock.BandIntensity;
        RedrawComet();
    }

    // 每棵树只建一次：共享时钟把两个标量写到自身，LateUpdate 再把这一帧扇出给订户。
    // 30fps 是唯一故意的差别 —— 每条链接不再各起一条 60fps 的采样循环。
    private sealed class FlowDriver : IWorkflowFlowDriver
    {
        private readonly WorkflowFlowClock _clock;
        private readonly Transition<WorkflowFlowClock> _flow;

        public FlowDriver(WorkflowFlowClock clock)
        {
            _clock = clock;
            _flow = Transition<WorkflowFlowClock>.Create()
                // 相位一：从发送端出发，一边走一边亮起
                .Property(c => c.BandHead, BandFormed)
                .Property(c => c.BandIntensity, 1d)
                .Effect(PhaseEffect(clock, EnterDuration))
                .Then()
                // 相位二：全亮行进——这一段读起来才是流动而非脉冲
                .Property(c => c.BandHead, BandLeaving)
                .Effect(PhaseEffect(clock, TravelDuration))
                .Then()
                // 相位三：到达并熄灭。两端都是「没有光」的状态，循环接缝才看不出来
                .Property(c => c.BandHead, 1d)
                .Property(c => c.BandIntensity, 0d)
                .Effect(PhaseEffect(clock, ExitDuration))
                .Repeat(int.MaxValue);
        }

        public void Start() => _flow.Execute(_clock);

        // 停的是这棵树共享的那条周期，不是某个视图自己的。
        public void Stop() => Transition.Exit(_clock, IncludeMutual: true, IncludeNoMutual: true);

        // 每段匀速（Eases.Default 就是恒等）；LateUpdate 挂在时钟缓存的那个委托上。
        private static TransitionEffect PhaseEffect(WorkflowFlowClock clock, TimeSpan duration)
        {
            var effect = new TransitionEffect()
            {
                Duration = duration,
                Ease = Eases.Default,
                FPS = 30,
            };
            effect.LateUpdate += clock.LateUpdateHandler;
            return effect;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        EnsureComet();
        Refresh();
        ResubscribeInput();
    }

    // 彗星的 24 段在合成层里建一次。端点变化只改已有的几何对象，不重建任何东西 —— 这也是它不再进布局的原因
    private void EnsureComet()
    {
        if (_cometVisual is not null)
        {
            SyncCometSize();
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(_cometLayer);
        var compositor = visual.Compositor;

        _cometVisual = compositor.CreateShapeVisual();

        _tail = new CometStrip[TailSegments];
        for (var i = 0; i < TailSegments; i++)
        {
            _tail[i] = CreateCometStrip(compositor, (float)LineThickness, out var shape);
            _cometVisual.Shapes.Add(shape);
        }

        _bloom = new CometStrip[BloomSegments];
        for (var i = 0; i < BloomSegments; i++)
        {
            _bloom[i] = CreateCometStrip(compositor, (float)BloomWidth, out var shape);
            _cometVisual.Shapes.Add(shape);
        }

        ElementCompositionPreview.SetElementChildVisual(_cometLayer, _cometVisual);
        SyncCometSize();
    }

    // 线段几何在承载元素自己的坐标系里，视觉尺寸必须跟着视图走，否则会被裁掉
    private void SyncCometSize()
    {
        if (_cometVisual is null)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;
        if (width > 0 && height > 0)
        {
            _cometVisual.Size = new Vector2((float)width, (float)height);
        }
    }

    private static CometStrip CreateCometStrip(Compositor compositor, float thickness, out CompositionSpriteShape shape)
    {
        var geometry = compositor.CreateLineGeometry();
        var brush = compositor.CreateColorBrush(Colors.Transparent);
        shape = compositor.CreateSpriteShape(geometry);
        shape.StrokeBrush = brush;
        shape.StrokeThickness = thickness;
        // 圆头：头部因此是一个逐渐收拢的圆端，而不是截断的一刀；相邻两段也自然接得上
        shape.StrokeStartCap = CompositionStrokeCap.Round;
        shape.StrokeEndCap = CompositionStrokeCap.Round;

        return new CometStrip { Shape = shape, Geometry = geometry, Brush = brush };
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        RefreshFlowSubscription();
    }

    // 改绑（池化视图换链接）与回收（DataContext 先被置空）都只在这里露出：两者都不触发 Loaded/Unloaded。
    // 绑定会把新的端点推下来，推不动的那几种情况（值恰好相同）Refresh 也照样按当前值重画一遍，
    // 所以「几何/显隐/周期」三件事在改绑后必定与当前这条链接一致
    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        var link = args.NewValue as IWorkflowLinkViewModel;
        if (!ReferenceEquals(_boundLink, link))
        {
            // 改绑/回收：旧链接的曲线必须撤掉，否则一条已经不画这条线的线还会被命中
            _boundLink?.PublishCurve(null);
            _boundLink = link;
            // 池化复用后旧链接的高亮会留在本视图上，先熄灭；换订新那棵树之后，
            // 指针再动时输入路由会把 Entered 交给新那条线，由它自己重新点亮
            IsHighlighted = false;
            ResubscribeInput();
            ResubscribePreview(link);
        }

        if (link is not null)
        {
            Refresh();
            return;
        }

        // 被回收了：没有链接可描述。Unloaded 对池化视图永远不来，周期只能在这里停，
        // 否则它会一直往一个没人看的视图里写 27 条 Path，直到这个视图被复用
        Hide();
    }

    // 虚拟连线的阶段：订模型自己的 Preview。改绑/回收都要换订阅，否则旧橡皮筋的阶段色会留在本视图上。
    private void ResubscribePreview(IWorkflowLinkViewModel? link)
    {
        var preview = link as IConnectionPreviewLink;
        if (ReferenceEquals(preview, _previewLink)) return;

        UnsubscribePreview();
        _previewLink = preview;
        _preview = preview?.Preview ?? ConnectionPreviewState.None;
        if (preview is INotifyPropertyChanged notifier) notifier.PropertyChanged += OnPreviewChanged;

        UpdateRestingLine();
    }

    private void UnsubscribePreview()
    {
        if (_previewLink is INotifyPropertyChanged notifier) notifier.PropertyChanged -= OnPreviewChanged;
        _previewLink = null;
        _preview = ConnectionPreviewState.None;
    }

    private void OnPreviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(IConnectionPreviewLink.Preview)) return;

        _preview = _previewLink?.Preview ?? ConnectionPreviewState.None;
        UpdateRestingLine();
    }

    // VeloxDev customization: 悬停高亮是本 demo 的。订**这条线自己的** Helper 就够了 —— 路由会告诉它指针
    // 什么时候进来、什么时候离开，这里不必再去比 target 是谁。视图比树活得短，改绑与摘树都要退订。
    private IWorkflowLinkViewModel? _inputLink;

    private void ResubscribeInput()
    {
        var link = _boundLink;
        if (ReferenceEquals(link, _inputLink)) return;

        UnsubscribeInput();
        if (link?.GetHelper() is not IInputEvents events) return;

        _inputLink = link;
        events.Input.PointerEntered += OnPointerEntered;
        events.Input.PointerExited += OnPointerExited;
    }

    private void UnsubscribeInput()
    {
        if (_inputLink?.GetHelper() is not IInputEvents events) return;

        events.Input.PointerEntered -= OnPointerEntered;
        events.Input.PointerExited -= OnPointerExited;
        _inputLink = null;
    }

    private void OnPointerEntered(object? sender, PointerEnteredEventArgs e) => IsHighlighted = true;

    private void OnPointerExited(object? sender, PointerExitedEventArgs e) => IsHighlighted = false;

    #endregion

    #region Geometry

    // 曲线与弧长表都归 Core：本视图只建一条 LinkCurve，绘制取点与发布命中共用它。
    // 控制点拉出规则由 BuildCubic 给出（max(40, |dx|/2)，两端水平出线），与静息线画的同一条。
    private void RefreshGeometry()
    {
        var link = DataContext as IWorkflowLinkViewModel;
        var points = LinkCurve.LinkCurvePoints(link, StartLeft, StartTop, EndLeft, EndTop, PullMinimum);
        _curve = LinkCurve.BuildLinkCubic(link, StartLeft, StartTop, EndLeft, EndTop, PullMinimum);

        // 盒子 = 曲线的包围盒（含控制点），与画布尺寸无关：
        // 1) 曲线可以伸出画布（节点/锚点不必落在 OriginSize 内），按画布尺寸定的盒子会把线裁在画布边；
        // 2) 合成层的 ShapeVisual 按自己的 Size 裁切，盒子里必须真的装得下整条线；
        // 3) 元素放在包围盒原点、几何减去原点，画出来的 canvas-local 坐标不变，与节点/命中曲线仍是同一套坐标系。
        var minX = Math.Min(Math.Min(points[0].X, points[1].X), Math.Min(points[2].X, points[3].X)) - BoxPadding;
        var minY = Math.Min(Math.Min(points[0].Y, points[1].Y), Math.Min(points[2].Y, points[3].Y)) - BoxPadding;
        var maxX = Math.Max(Math.Max(points[0].X, points[1].X), Math.Max(points[2].X, points[3].X)) + BoxPadding;
        var maxY = Math.Max(Math.Max(points[0].Y, points[1].Y), Math.Max(points[2].Y, points[3].Y)) + BoxPadding;
        var boxWidth = maxX - minX;
        var boxHeight = maxY - minY;

        if (boxWidth > 0 && boxHeight > 0)
        {
            _boxMinX = minX;
            _boxMinY = minY;
            _boxTransform.X = minX;
            _boxTransform.Y = minY;
            Width = boxWidth;
            Height = boxHeight;

            // 合成层不参与 XAML 布局，Size 要直接写；等 ActualWidth 会晚一帧。
            if (_cometVisual is not null)
            {
                _cometVisual.Size = new Vector2((float)boxWidth, (float)boxHeight);
            }
        }

        // 静息线就是那条三次贝塞尔本身，直接用 BezierSegment 画：一个段就是精确曲线，
        // 不必拿采样点去拼（那是给「按弧长切」用的表）。三层各写一份 —— 几何不能共用，理由见 RestingStroke
        foreach (var stroke in _resting)
        {
            stroke.Figure.StartPoint = new Point(points[0].X - _boxMinX, points[0].Y - _boxMinY);
            stroke.Segment.Point1 = new Point(points[1].X - _boxMinX, points[1].Y - _boxMinY);
            stroke.Segment.Point2 = new Point(points[2].X - _boxMinX, points[2].Y - _boxMinY);
            stroke.Segment.Point3 = new Point(points[3].X - _boxMinX, points[3].Y - _boxMinY);
        }
    }

    // 把画出来的那条曲线交给 Core 做命中。必须是未烘焙的 canvas-local 坐标（本视图的 DP 本来就是），
    // 发布的是引用而非副本，所以每帧重发是安全的；画不出东西时发 null，避免旧曲线继续作答。
    private void PublishCurve()
        => _boundLink?.PublishCurve(RenderReady ? _curve : null, this);

    #endregion

    #region Render

    // 有没有线可画：宿主没把它藏起来，且两端都是量出来的坐标。
    // 未测量的 slot 锚点是 NaN，而 ViewManager 是把一个链接和它的两端分在相邻批次里物化、槽位测量又是
    // 异步落地的，所以视图完全可能先绑上、端点后测量 —— 这条判定就是挡在绘制外面的那道门。
    // 必须显式判 NaN 而不能指望绘制路径里的比较：NaN 参与的比较全是 false，`Length <= 0` 这类守卫根本拦不住它，
    // 而保留模式里写进几何的就是画出来的东西，一旦写入 NaN 点，这条线就什么都不会画。
    private bool RenderReady => CanRender
        && !double.IsNaN(StartLeft) && !double.IsNaN(StartTop)
        && !double.IsNaN(EndLeft) && !double.IsNaN(EndTop);

    // 按当前的依赖属性值把「这一帧要画的东西」整体重算一遍：弧长表、三层静息线、显隐、彗星，以及周期。
    // 保留模式没有下一帧自动重算，所以每个入口（属性变更 / 首次挂载 / 改绑）都必须走这里：
    // 漏掉一个，视图就会停在旧链接的几何上、或者该出现时不出现，直到别的属性碰巧变一次。
    private void Refresh()
    {
        if (!RenderReady)
        {
            Hide();
            return;
        }

        RefreshGeometry();
        UpdateRestingLine();
        UpdateVisibility(true);
        RedrawComet();
        RefreshFlowSubscription();
        PublishCurve();
    }

    // 无可绘制：收起静息线与彗星，并退订共享时钟 —— 退订是有代价的，不退的话这条线会一直跟着树重画
    private void Hide()
    {
        UpdateVisibility(false);
        RefreshFlowSubscription();
        PublishCurve();
    }

    // 三层静息线（halo/glow/body）的显隐。收起时彗星一并收起：它依附的那条线都不在
    private void UpdateVisibility(bool visible)
    {
        var state = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var stroke in _resting)
        {
            stroke.Path.Visibility = state;
        }

        if (!visible)
        {
            HideComet();
        }
    }

    // 静息线：两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。
    // 圆头圆角，两端才不像被截断的横截面
    private void UpdateRestingLine()
    {
        var color = _preview switch
        {
            ConnectionPreviewState.ValidTarget => PreviewValidColor,
            ConnectionPreviewState.InvalidTarget => PreviewInvalidColor,
            _ => IsHighlighted ? HighlightColor : LineColor,
        };
        var thickness = CurrentThickness;

        _halo.Brush.Color = WithAlpha(color, 0.10);
        _halo.Path.StrokeThickness = thickness + 9;
        _glow.Brush.Color = WithAlpha(color, 0.16);
        _glow.Path.StrokeThickness = thickness + 4;

        // 线体本身是静息的：光不在时它只是一根暗线，有了对比彗星才亮得出来
        _line.Brush.Color = WithAlpha(color, IsHighlighted ? 0.85 : 0.55);
        _line.Path.StrokeThickness = thickness;
        _line.Path.StrokeDashArray = IsVirtual ? VirtualStrokeDashArray : null;

        UpdateStripThickness();
    }

    // 彗星各段的粗细只由「整体粗细」与「该段在尾巴上的位置」决定，两者都不是逐帧量：位置是每段固定的，
    // 整体粗细只在悬停/换色时变。所以它写在这里而不是每帧的重画里 —— StrokeThickness 是布局属性，
    // 每帧写 24 次就是每帧让整块画布重排一次（实测：空闲状态下 60 次/秒）。
    // 缓存一道是因为 Refresh() 在拖端点时每帧都跑，不能让这条路径变成新的逐帧写。
    private double _stripThickness = double.NaN;

    private void UpdateStripThickness()
    {
        var thickness = CurrentThickness;
        if (thickness == _stripThickness)
        {
            return;
        }

        _stripThickness = thickness;

        if (_tail is null || _bloom is null)
        {
            return;
        }

        for (var k = 0; k < TailSegments; k++)
        {
            _tail[k].Shape.StrokeThickness = (float)(thickness * (0.45 + (0.95 * (k / (double)TailSegments))));
        }

        var bloomThickness = (float)(thickness + BloomWidth);
        for (var k = 0; k < BloomSegments; k++)
        {
            _bloom[k].Shape.StrokeThickness = bloomThickness;
        }
    }

    private double CurrentThickness => IsHighlighted ? LineThickness + 1.5 : LineThickness;

    // 彗星：沿弧长切出 [头-尾, 头] 这一段，分若干小段画，每段给一个递减的不透明度与变化的颜色。
    // 不用渐变刷是因为它的轴是两端之间的直线，在曲线上会把光打偏（见类注释）。
    private void RedrawComet()
    {
        if (!RenderReady || _curve is not { Length: > 0 } curve || IsVirtual
            || _bandIntensity <= 0.001 || _tail is null || _bloom is null)
        {
            HideComet();
            return;
        }

        var color = IsHighlighted ? HighlightColor : LineColor;

        var length = curve.Length;
        var head = Math.Clamp(_bandHead, 0, 1) * length;
        var tail = TailFraction * length;

        // 本体：尾梢是本体的颜色，越靠近头越白 —— 白热只发生在头部
        for (var k = 0; k < TailSegments; k++)
        {
            var f0 = k / (double)TailSegments;      // 0 = 尾梢，1 = 头
            var f1 = (k + 1) / (double)TailSegments;

            var l0 = head - (tail * (1 - f0));
            var l1 = head - (tail * (1 - f1));

            var strip = _tail[k];

            // 平方衰减：让透明集中在尾段，读起来才像拖尾而不是一条均匀的带
            var a = _bandIntensity * f0 * f0;
            // 出窗的段收成透明而不是收起：合成层里没有 Visibility，也不该有 —— 它是个布局概念
            if (l1 <= 0 || l0 >= length || a <= 0.004)
            {
                strip.Brush.Color = Colors.Transparent;
                continue;
            }

            SetStrip(strip, l0, l1);
            strip.Brush.Color = WithAlpha(Mix(color, Colors.White, f0), a);
        }

        // 光晕：更宽更淡的一层，跟着头走，所以动感在光晕上也读得出来。
        // 它自己的不透明度用的是本段中点而不是起点，八段平均分布才不会整条偏亮
        for (var k = 0; k < BloomSegments; k++)
        {
            var f0 = k / (double)BloomSegments;
            var f1 = (k + 1) / (double)BloomSegments;

            var l0 = head - (tail * (1 - f0));
            var l1 = head - (tail * (1 - f1));

            var strip = _bloom[k];

            var mid = (f0 + f1) * 0.5;
            var a = _bandIntensity * mid * mid * BloomAlpha;
            if (l1 <= 0 || l0 >= length || a <= 0.004)
            {
                strip.Brush.Color = Colors.Transparent;
                continue;
            }

            SetStrip(strip, l0, l1);
            strip.Brush.Color = WithAlpha(Mix(color, Colors.White, mid), a);
        }
    }

    private void HideComet()
    {
        if (_tail is null || _bloom is null)
        {
            return;
        }

        foreach (var strip in _tail)
        {
            strip.Brush.Color = Colors.Transparent;
        }

        foreach (var strip in _bloom)
        {
            strip.Brush.Color = Colors.Transparent;
        }
    }

    // 一段弧长上的描边。两点足够：每段只有几像素长，而两端都是曲线按弧长精确取出来的点。
    private void SetStrip(CometStrip strip, double from, double to)
    {
        var curve = _curve!;
        var a = Math.Clamp(from, 0, curve.Length);
        var b = Math.Clamp(to, 0, curve.Length);

        strip.Geometry.Start = ToVector(curve.PointAtLength(a));
        strip.Geometry.End = ToVector(curve.PointAtLength(b));
    }

    // 与静息线同一套：合成线段也减去包围盒原点，尺寸盒才装得下整条线。
    private Vector2 ToVector((double X, double Y) point)
        => new((float)(point.X - _boxMinX), (float)(point.Y - _boxMinY));

    // 静息线的一层：自己的几何 + 自己的路径，颜色与厚度都归调用方（UpdateRestingLine）写。
    private static RestingStroke CreateRestingStroke(Grid host, double thickness)
    {
        var segment = new BezierSegment();
        var figure = new PathFigure { IsClosed = false };
        figure.Segments.Add(segment);

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var brush = new SolidColorBrush(Colors.Transparent);
        var path = new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            // 圆头：两端因此不像被截断的横截面
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            Clip = null,
        };

        host.Children.Add(path);

        return new RestingStroke
        {
            Path = path,
            Geometry = geometry,
            Figure = figure,
            Segment = segment,
            Brush = brush,
        };
    }

    // 两色之间线性混合（含 alpha），用于尾梢到头部的那一段渐变
    private static Windows.UI.Color Mix(Windows.UI.Color from, Windows.UI.Color to, double t)
    {
        byte L(byte a, byte b) => (byte)Math.Round(a + ((b - a) * t));

        return Windows.UI.Color.FromArgb(L(from.A, to.A), L(from.R, to.R), L(from.G, to.G), L(from.B, to.B));
    }

    // 保留了色相、只换透明度的同色。Avalonia 那边是 new ImmutableSolidColorBrush(color, alpha)，
    // 这里是一支复用画刷改 Color —— 每帧不新建对象。
    private static Windows.UI.Color WithAlpha(Windows.UI.Color color, double alpha)
        => Windows.UI.Color.FromArgb(
            (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255),
            color.R,
            color.G,
            color.B);

    #endregion
}
