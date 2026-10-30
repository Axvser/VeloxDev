using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using Demo.Workflow;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>
/// One connection between two ports, drawn as a single cubic curve that leaves each end horizontally.
/// <para>
/// The light travelling along it is a comet — a bright head, a tail that fades behind it, and a halo that
/// follows the head — and it is cut out of the curve <b>by arc length</b> rather than by a gradient brush.
/// That is why the curve is sampled: a <see cref="LinearGradientBrush"/>'s axis is the straight line between
/// the two ends, so on a curve it lights the string rather than the rope.
/// </para>
/// <para>
/// The flattened curve comes from <see cref="LinkCurve"/> and is published to the link's own helper, so the
/// surface hit-tests the exact shape this view painted (see <see cref="LinkHitTestEx"/>). Hit-testing and routing
/// stay in Core: hover and press are resolved once, there. The highlight is this demo's own reading — it
/// subscribes to this link's own <see cref="IInputEvents"/> pointer events and decides how lit it looks.
/// </para>
/// </summary>
public partial class PolylineCurveView : Control
{
    // 拖尾占全长的比例。这是彗星唯一的观感旋钮：调大＝更长的尾、更像流光；调小＝更像一个亮点在跑。
    private const double TailFraction = 0.30;

    // 拖尾分几段画。每段一个透明度，衰减因此是连续的而不需要渐变刷。
    // 每段只是一条弦：它占全长的 1.875%，弦与弧的差不到半个像素（与 WPF/Jalium 同一假设）。
    private const int TailSegments = 16;

    // 控制点的最小水平拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段。
    private const double PullMinimum = 40;

    // 三段相位各自结束时头部走过的比例：出发、行进、到达
    private const double BandFormed = 0.30;
    private const double BandLeaving = 0.78;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(450);

    // Core 的扁平化曲线：采样、弧长、包围盒都归它，本视图只读。端点变化时重建并发布给命中契约。
    private LinkCurve? _curve;

    // 静息线体的几何与曲线同生共死：曲线不变它就不变，动画帧上不必重建。
    private StreamGeometry? _body;

    // 彗星每帧只算每段的两个端点，直接用预建的画笔各画一条弦：不碰点集，也不让几何逐帧失效。
    // 两遍绘制共用同一份端点，所以每帧的点只取一次。
    private readonly Point[] _tailFrom = new Point[TailSegments];
    private readonly Point[] _tailTo = new Point[TailSegments];
    private readonly double[] _tailOpacity = new double[TailSegments];
    private readonly bool[] _tailActive = new bool[TailSegments];

    // 配色/粗细只在属性变化时重建；动画帧只改透明度，所以这些 Pen 可以一直复用。
    private readonly Pen[] _cometBloomPens = new Pen[TailSegments];
    private readonly Pen[] _cometBodyPens = new Pen[TailSegments];
    private Color _styleColor;
    private double _styleThickness;
    private bool _styleHighlighted;
    private bool _styleReady;
    private Pen? _glowOuter;
    private Pen? _glowInner;
    private Pen? _bodyPen;
    private Pen? _dashedPen;

    // 本视图订的那棵树共用的时钟；每次 tick 把它的两个标量抄进本视图的属性。
    private WorkflowFlowClock? _clock;
    private readonly Action _clockTick;
    private bool _attached;

    public PolylineCurveView()
    {
        // 缓存字段：每帧交给时钟的是同一个委托，不随 tick 新建。
        _clockTick = OnClockTick;
        InitializeComponent();
        IsHitTestVisible = true;
        Focusable = true;

        // 悬停即取焦点（Delete 需要），而 Avalonia 的 ScrollViewer 默认会把「获得焦点的元素」滚进视口
        // （BringIntoViewOnFocusChange 默认 true），本视图又是整块画布大小 ⇒ 鼠标一碰到线画布就跳一段。
        // 这条请求只对本视图有意义，所以在发源地吃掉；节点卡里输入框的自动滚进视口不受影响。
        AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);

        RefreshGeometry();
    }

    #region Styled properties

    public static readonly StyledProperty<double> StartLeftProperty =
        AvaloniaProperty.Register<PolylineCurveView, double>(nameof(StartLeft));
    public static readonly StyledProperty<double> StartTopProperty =
        AvaloniaProperty.Register<PolylineCurveView, double>(nameof(StartTop));
    public static readonly StyledProperty<double> EndLeftProperty =
        AvaloniaProperty.Register<PolylineCurveView, double>(nameof(EndLeft));
    public static readonly StyledProperty<double> EndTopProperty =
        AvaloniaProperty.Register<PolylineCurveView, double>(nameof(EndTop));
    public static readonly StyledProperty<bool> CanRenderProperty =
        AvaloniaProperty.Register<PolylineCurveView, bool>(nameof(CanRender), true);
    public static readonly StyledProperty<bool> IsVirtualProperty =
        AvaloniaProperty.Register<PolylineCurveView, bool>(nameof(IsVirtual), false);
    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<PolylineCurveView, Color>(nameof(LineColor), Color.Parse("#CC38BDF8"));
    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<PolylineCurveView, double>(nameof(LineThickness), 2.0);
    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<PolylineCurveView, bool>(nameof(IsHighlighted), false);
    public static readonly StyledProperty<Color> HighlightColorProperty =
        AvaloniaProperty.Register<PolylineCurveView, Color>(nameof(HighlightColor), Color.Parse("#FFFFFFFF"));

    /// <summary>How far along the link the comet's head has travelled, as a fraction of its length.</summary>
    /// <remarks>
    /// Animated rather than computed, and registered with <see cref="AffectsRender{T}"/> so each frame the
    /// shared clock writes is also a frame this view repaints. The value carries no geometry of its own — the
    /// published arc-length table turns it into a point — which is what lets the light follow a curve.
    /// </remarks>
    public static readonly StyledProperty<double> BandHeadProperty =
        AvaloniaProperty.Register<PolylineCurveView, double>(nameof(BandHead));

    /// <summary>How lit the comet is: 0 while it is absent, 1 while it travels.</summary>
    public static readonly StyledProperty<double> BandIntensityProperty =
        AvaloniaProperty.Register<PolylineCurveView, double>(nameof(BandIntensity));

    public double StartLeft { get => GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Color LineColor { get => GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public double LineThickness { get => GetValue(LineThicknessProperty); set => SetValue(LineThicknessProperty, value); }
    public bool IsHighlighted { get => GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }
    public Color HighlightColor { get => GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }
    public double BandHead { get => GetValue(BandHeadProperty); set => SetValue(BandHeadProperty, value); }
    public double BandIntensity { get => GetValue(BandIntensityProperty); set => SetValue(BandIntensityProperty, value); }

    static PolylineCurveView()
    {
        AffectsRender<PolylineCurveView>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty, IsHighlightedProperty, HighlightColorProperty,
            BandHeadProperty, BandIntensityProperty);

        // 平台驱动只装一次：共享时钟负责周期与订户计数，本类只提供 Avalonia 的 Transition 构造方式。
        WorkflowFlowClock.RegisterDriver(static clock => new FlowDriver(clock));
    }

    #endregion

    #region Flow effect

    // 订本树共用的时钟：只有真正在屏、可画、非虚拟、且挂在某棵树上的链接才加入。
    // 池化改绑会先退旧树再订新树，摘树则退订；订阅数归零时时钟自己停表（一棵树始终只有一条周期）。
    private void RefreshFlowSubscription()
    {
        var clock = _attached && !IsVirtual && CanRender
            ? WorkflowFlowClock.ForLink(DataContext as IWorkflowLinkViewModel)
            : null;

        if (ReferenceEquals(clock, _clock)) return;

        if (_clock is not null)
        {
            _clock.Unsubscribe(_clockTick);

            // 旧一帧的光不该留在改绑后的新链接上；下一帧由新时钟重新写入。
            BandHead = 0d;
            BandIntensity = 0d;
        }

        _clock = clock;
        _clock?.Subscribe(_clockTick);
    }

    // 每帧只抄两个标量；两个属性都注册了 AffectsRender，写值就是这一次重画。
    private void OnClockTick()
    {
        var clock = _clock;
        if (clock is null) return;

        BandHead = clock.BandHead;
        BandIntensity = clock.BandIntensity;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        PublishCurve();
        ResubscribeInput();
        RefreshFlowSubscription();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // A pooled view released and reused for another link must not leave the old animation running on it.
        _attached = false;
        RefreshFlowSubscription();
        UnsubscribeInput();
    }

    // VeloxDev customization: 悬停高亮是本 demo 的。订**这条线自己的** Helper 就够了 —— 路由会告诉它指针
    // 什么时候进来、什么时候离开，这里不必再去比 target 是谁。视图比树活得短，改绑与摘树都要退订。
    private IWorkflowLinkViewModel? _inputLink;

    private void ResubscribeInput()
    {
        var link = DataContext as IWorkflowLinkViewModel;
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StartLeftProperty || change.Property == StartTopProperty
            || change.Property == EndLeftProperty || change.Property == EndTopProperty)
        {
            RefreshGeometry();
        }

        if (change.Property == DataContextProperty)
        {
            // 池化改绑：旧链接不能留着一条指向本视图的曲线，否则它的命中会答在一个已经画着别人的控件上。
            if (change.OldValue is IWorkflowLinkViewModel old && !ReferenceEquals(old, change.NewValue))
            {
                old.PublishCurve(null);
            }

            PublishCurve();
            ResubscribeInput();
            RefreshFlowSubscription();
        }

        // A link becomes drawable only once both endpoints have been measured, and the flow has nothing to
        // travel along before that — while a virtual one is the rubber band under the pointer, which has no
        // settled connection to describe.
        if (change.Property == CanRenderProperty || change.Property == IsVirtualProperty)
        {
            RefreshFlowSubscription();
        }

        // UsePolyline 在两个视图之间切换显示；接手显示的那个要把曲线（与 sender）重新挂到自己身上。
        if (change.Property == IsVisibleProperty)
        {
            PublishCurve();
        }
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

    #endregion

    #region Geometry

    // 曲线由 Core 构建一次：与绘制读的是同一次构建，所以命中与画出来的永远是一条。
    private void RefreshGeometry()
    {
        var curve = LinkCurve.BuildLinkCubic(DataContext as IWorkflowLinkViewModel, StartLeft, StartTop, EndLeft, EndTop, PullMinimum);
        _curve = curve;
        _body = curve.Length > 0 ? BuildSegment(0, curve.Length) : null;

        PublishCurve();
    }

    // 把画出来的形状交给链接的 Helper，界面据此判命中（见 ILinkHitTestable）。
    // 同一链接由两个视图轮流显示（UsePolyline）：没有在显示的那个不能发布 —— 命中契约只存一个
    // Visual，菜单等事件的 sender 必须落在真正被看到的那个控件上，否则菜单会挂在隐藏控件上。
    private void PublishCurve()
    {
        if (!IsVisible || DataContext is not IWorkflowLinkViewModel link)
        {
            return;
        }

        link.PublishCurve(_curve, this);
    }

    // 取 [from, to] 这一段弧长上的折线几何。两端各自插值到精确位置，中间用现成采样点
    private StreamGeometry BuildSegment(double from, double to)
    {
        var curve = _curve!;
        to = Math.Min(to, curve.Length);
        from = Math.Clamp(from, 0, curve.Length);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(PointAtLength(from), false);

            for (int i = 0; i < curve.Count; i++)
            {
                double l = curve.LengthAt(i);
                if (l <= from || l >= to) continue;
                ctx.LineTo(new Point(curve.XAt(i), curve.YAt(i)));
            }

            ctx.LineTo(PointAtLength(to));
        }

        return geo;
    }

    // 弧长 → 点（Core 二分 + 段内插值，精确到亚像素，不受采样密度限制）
    private Point PointAtLength(double len)
    {
        var (x, y) = _curve!.PointAtLength(len);
        return new Point(x, y);
    }

    #endregion

    #region Render

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!CanRender || _curve is not { Length: > 0 } || _body is null) return;

        var color = IsHighlighted ? HighlightColor : LineColor;
        var thickness = IsHighlighted ? LineThickness + 1.5 : LineThickness;
        EnsureStyle(color, thickness, IsHighlighted);

        // 管壁：两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。圆头圆角，
        // 两端才不像被截断的横截面
        context.DrawGeometry(null, _glowOuter!, _body);
        context.DrawGeometry(null, _glowInner!, _body);

        // 虚拟连线是指针下的橡皮筋：虚线、不流动
        if (IsVirtual)
        {
            context.DrawGeometry(null, _dashedPen!, _body);
            return;
        }

        // 线体本身是静息的：光不在时它只是一根暗线，有了对比彗星才亮得出来
        context.DrawGeometry(null, _bodyPen!, _body);

        if (BandIntensity > 0.001)
        {
            DrawComet(context);
        }
    }

    // 画笔只在颜色、粗细或高亮变化时重建 —— 动画帧（BandHead/BandIntensity）不会走到这里。
    private void EnsureStyle(Color color, double thickness, bool highlighted)
    {
        if (_styleReady && _styleColor == color && _styleThickness == thickness && _styleHighlighted == highlighted)
        {
            return;
        }

        _styleReady = true;
        _styleColor = color;
        _styleThickness = thickness;
        _styleHighlighted = highlighted;

        _glowOuter = new Pen(new ImmutableSolidColorBrush(color, 0.10), thickness + 9) { LineCap = PenLineCap.Round };
        _glowInner = new Pen(new ImmutableSolidColorBrush(color, 0.16), thickness + 4) { LineCap = PenLineCap.Round };
        _bodyPen = new Pen(new ImmutableSolidColorBrush(color, highlighted ? 0.85 : 0.55), thickness) { LineCap = PenLineCap.Round };
        _dashedPen = new Pen(new ImmutableSolidColorBrush(color, 0.75), thickness)
        {
            DashStyle = new DashStyle([4.0, 2.0], 0),
        };

        // 彗星每段的白热颜色与宽度只取决于颜色和粗细；随帧变化的透明度留给 PushOpacity 去乘。
        for (int k = 0; k < TailSegments; k++)
        {
            double f = k / (double)TailSegments;
            var c = Mix(color, Colors.White, f);

            // 光晕笔不设端帽（默认平头），与原来的光晕描边一致；圆头只在下面的本体笔上
            _cometBloomPens[k] = new Pen(new ImmutableSolidColorBrush(c), thickness + 9);
            _cometBodyPens[k] = new Pen(new ImmutableSolidColorBrush(c), thickness * (0.45 + (0.95 * f)))
            {
                // 圆头：头部因此是一个逐渐收拢的圆端，而不是截断的一刀
                LineCap = PenLineCap.Round,
            };
        }
    }

    // 彗星：沿弧长切出 [头-尾, 头] 这一段，分若干小段画，每段给一个递减的透明度与变化的颜色。
    // 不用渐变刷是因为它的轴是两端之间的直线，在曲线上会把光打偏（见类注释）。
    // 每段直接画一条弦，而不是改写复用几何的点集：弦只占全长的 1.875%，弧与弦的差不到半个像素，
    // 而点集的逐帧通知、路径失效与平台几何的建立都不再发生。
    private void DrawComet(DrawingContext context)
    {
        var curve = _curve!;
        double head = Math.Clamp(BandHead, 0, 1) * curve.Length;
        double tail = TailFraction * curve.Length;

        // 先算好每段的两个端点与透明度；两遍绘制共用这两份点，只有画笔与透明度不同。
        for (int k = 0; k < TailSegments; k++)
        {
            double f0 = k / (double)TailSegments;      // 0 = 尾梢，1 = 头
            double f1 = (k + 1) / (double)TailSegments;

            double l0 = head - (tail * (1 - f0));
            double l1 = head - (tail * (1 - f1));
            _tailActive[k] = false;
            if (l1 <= 0 || l0 >= curve.Length) continue;

            // 平方衰减：让透明集中在尾段，读起来才像拖尾而不是一条均匀的带
            double a = BandIntensity * f0 * f0;
            if (a <= 0.004) continue;

            // PointAtLength 自己会把弧长夹到 [0, Length]，跨过起点的那一段因此不会取到曲线外
            _tailFrom[k] = PointAtLength(l0);
            _tailTo[k] = PointAtLength(l1);
            _tailOpacity[k] = a;
            _tailActive[k] = true;
        }

        // 两遍：先光晕（更宽更淡）再本体，两遍都跟着头走，所以动感在光晕上也读得出来
        for (int pass = 0; pass < 2; pass++)
        {
            bool bloom = pass == 0;
            var pens = bloom ? _cometBloomPens : _cometBodyPens;

            for (int k = 0; k < TailSegments; k++)
            {
                if (!_tailActive[k]) continue;

                // 画笔的颜色已含白热程度，这里只把随帧变化的透明度乘上去。
                // 本体的笔是圆头：最后一段的终点就是亮头，圆帽由 DrawLine 按笔的端帽补上。
                using (context.PushOpacity(bloom ? _tailOpacity[k] * 0.22 : _tailOpacity[k]))
                {
                    context.DrawLine(pens[k], _tailFrom[k], _tailTo[k]);
                }
            }
        }
    }

    // 两色之间线性混合（含 alpha），用于尾梢到头部的那一段渐变
    private static Color Mix(Color from, Color to, double t)
    {
        byte L(byte a, byte b) => (byte)Math.Round(a + ((b - a) * t));

        return Color.FromArgb(L(from.A, to.A), L(from.R, to.R), L(from.G, to.G), L(from.B, to.B));
    }

    #endregion
}
