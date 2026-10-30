using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Demo.Workflow;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// One connection between two ports, drawn as a single cubic curve that leaves each end horizontally.
/// <para>
/// The light travelling along it is a comet — a bright head, a tail that fades behind it, and a halo that
/// follows the head — and it is cut out of the curve <b>by arc length</b> rather than by a gradient brush.
/// That is the whole reason this view keeps a sample table: a <see cref="LinearGradientBrush"/>'s axis is
/// the straight line between the two ends, so on a curve it lights the string rather than the rope. The
/// table itself now comes from Core's <see cref="LinkCurve"/> — the view draws with it <i>and</i> publishes
/// it, so the curve that is painted and the curve that is hit are one object rather than two derivations.
/// </para>
/// <para>
/// This file is the WPF reading of Avalonia's <c>Views/Workflow/PolylineCurveView.axaml.cs</c>; the geometry
/// and the comet are the same construction. Two things that file warns about are moot here, and one
/// is not:
/// </para>
/// <list type="bullet">
/// <item>
/// It stopped using a gradient brush, so <c>BrushMappingMode.Absolute</c> and the
/// <c>TransitionEffect.LateUpdate</c> repaint hook are both gone — those were only ever needed because a
/// <see cref="DrawingContext"/> keeps a reference to a brush without subscribing to it, so writing a stop
/// offset never re-entered <c>OnRender</c>. What the comet animates now is two styled properties on this
/// control, and WPF's own change notification is the hook: each one's <see cref="PropertyMetadata"/> callback
/// calls <see cref="UIElement.InvalidateVisual"/>. That is the WPF spelling of Avalonia's <c>AffectsRender</c>,
/// and it is the whole of it.
/// </item>
/// <item>
/// The old polyline's golden-ratio stubs and its arrowhead are gone with it: the curve is the direction cue now
/// (and the comet is the flow), so an arrowhead at the end was a third thing saying the same.
/// </item>
/// <item>
/// What is <i>not</i> moot is that this control draws into its own space with the canvas' coordinates, so the
/// curve is rebuilt whenever an endpoint moves — which, while a node is dragged or the surface is zoomed, is
/// every frame. 128 samples of a cubic is a few hundred flops, and the alternative (sampling at draw time,
/// twice per tail segment) is what the table exists to avoid.
/// </item>
/// </list>
/// <para>
/// The type keeps the name <c>PolylineCurveView</c> because the surface template binds it by that name; it has
/// not drawn a polyline since the geometry was replaced.
/// </para>
/// <para>
/// It no longer handles pointer input: the surface resolves the hit against the tree's published curves
/// (<see cref="LinkHitTestEx"/>) and the shared input route delivers it. The hover highlight is
/// <b>this demo's</b> — it subscribes to this link's own <see cref="IInputEvents"/> pointer events and writes
/// <see cref="IsHighlighted"/> when the hovered link is its own — and it takes keyboard focus while hovered so
/// <c>Delete</c> bubbles up to the surface that forwards it.
/// </para>
/// </summary>
public partial class PolylineCurveView : UserControl
{
    // 控制点的最小水平拉出量（canvas-local 单位）：两个端口靠得很近时，0.5·dx 会让曲线退化成
    // 一条直线段，失去「从端口水平出来」的形状。
    private const double PullMinimum = 40;

    // 拖尾占全长的比例。这是彗星唯一的观感旋钮：调大＝更长的尾、更像流光；调小＝更像一个亮点在跑。
    private const double TailFraction = 0.30;

    // 拖尾分几段画。每段一个透明度，衰减因此是连续的而不需要渐变刷。
    private const int TailSegments = 16;

    // 三段相位各自结束时头部走过的比例：出发、行进、到达
    private const double BandFormed = 0.30;
    private const double BandLeaving = 0.78;

    // 线宽与高亮时的线宽（Avalonia 那边是 LineThickness 与其 +1.5；本 demo 没有别处设它，
    // 所以就不必为一个从未被写过的依赖属性留位置）
    private const double LineWidth = 2.0;
    private const double LineWidthHighlighted = 3.5;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(450);

    // 平台驱动只装一次：共享时钟负责周期与订户计数，本类只提供 WPF 的 Transition 构造方式。
    static PolylineCurveView()
    {
        WorkflowFlowClock.RegisterDriver(static clock => new FlowDriver(clock));
    }

    // 本视图的弧长表：绘制、取点、发布给 Core 做命中，三处共用这一份，不再各推一份几何。
    private LinkCurve? _curve;

    // 当前把曲线发布给了哪条链接。视图会被池化改绑，换绑时要把旧链接的曲线撤掉，
    // 否则一条已经不在场上的线仍然可被点中。
    private IWorkflowLinkViewModel? _publishedLink;

    // 本视图订的那棵树共用的时钟；每次 tick 把它的两个标量抄进本视图的属性。
    private WorkflowFlowClock? _clock;
    private readonly Action _clockTick;
    private bool _loaded;

    // 虚拟连线是唯一实现 IConnectionPreviewLink 的链接：它自己有阶段（发起/有效候选/无效候选）。
    // 订模型而不是让表面推，池化改绑与取消都不会留下旧颜色。
    private IConnectionPreviewLink? _previewLink;
    private ConnectionPreviewState _preview;

    // 阶段色是本 demo 的外观，不是 Core 的：有效候选绿、无效候选红，其余用模板给的线色。
    private static readonly Color PreviewValidColor = (Color)ColorConverter.ConvertFromString("#FF34D399");
    private static readonly Color PreviewInvalidColor = (Color)ColorConverter.ConvertFromString("#FFF87171");

    public PolylineCurveView()
    {
        // 缓存字段：每帧交给时钟的是同一个委托，不随 tick 新建。
        _clockTick = OnClockTick;
        InitializeComponent();
        // 指针输入不在视图里：命中归 Core，指针与按键由表面行为转发；悬停高亮由本 demo 订这条线自己的输入
        // 事件自己画（见 ResubscribeInput），悬停焦点由适配器交给画线的那台控件（见 WorkflowSurfaceBehavior.FocusHoveredLink）。
        IsHitTestVisible = false;
        Focusable = true;
        Panel.SetZIndex(this, -100);

        RefreshGeometry();

        // WPF 的 UserControl 没有可重写的虚拟挂载方法，用 Loaded/Unloaded 这对事件（WPF 适配器也用它挂载画布）
        // 池化复用只隐藏视图并改绑 DataContext，不摘树，两个事件都不触发；锚点属性重绑已让几何重建，周期也从未停过
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // 池化改绑走 DataContext，不走卸载：几何要重发。
        DataContextChanged += OnDataContextChanged;

        // 适配器把悬停焦点交给本视图后会触发 WPF 的默认行为：拿到焦点的元素请求「把自己滚进视口」，
        // ScrollViewer 照办 —— 鼠标一碰到线画布就跳一段。焦点本身要留着（Delete 键靠它冒泡到表面），所以只吃掉这条请求。
        AddHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((_, e) => e.Handled = true));
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(true, OnRenderChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnRenderChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(PolylineCurveView), new PropertyMetadata((Color)ColorConverter.ConvertFromString("#CC38BDF8"), OnRenderChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnRenderChanged));
    public static readonly DependencyProperty HighlightColorProperty =
        DependencyProperty.Register(nameof(HighlightColor), typeof(Color), typeof(PolylineCurveView), new PropertyMetadata((Color)ColorConverter.ConvertFromString("#FFFFFFFF"), OnRenderChanged));

    /// <summary>How far along the link the comet's head has travelled, as a fraction of its length.</summary>
    /// <remarks>
    /// Animated rather than computed, and its change callback is what repaints this view. The value carries no
    /// geometry of its own — the arc-length table turns it into a point — which is what lets the light follow a
    /// curve.
    /// </remarks>
    public static readonly DependencyProperty BandHeadProperty =
        DependencyProperty.Register(nameof(BandHead), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>How lit the comet is: 0 while it is absent, 1 while it travels.</summary>
    public static readonly DependencyProperty BandIntensityProperty =
        DependencyProperty.Register(nameof(BandIntensity), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    /// <inheritdoc />
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    /// <summary>The soft light this link turns into while it is the hovered one.</summary>
    public Color HighlightColor { get => (Color)GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }
    public double BandHead { get => (double)GetValue(BandHeadProperty); set => SetValue(BandHeadProperty, value); }
    public double BandIntensity { get => (double)GetValue(BandIntensityProperty); set => SetValue(BandIntensityProperty, value); }

    /// <summary>
    /// The WPF stand-in for Avalonia's <c>AffectsRender</c>: rebuild the table when the endpoints move, repaint
    /// on any of them. Without the repaint the comet would be written into two properties every frame and never
    /// drawn — WPF keeps <c>OnRender</c>'s output as retained drawing, so writing a value does not re-enter it.
    /// </summary>
    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PolylineCurveView)d;

        if (e.Property == StartLeftProperty || e.Property == StartTopProperty
            || e.Property == EndLeftProperty || e.Property == EndTopProperty)
        {
            control.RefreshGeometry();
        }

        control.UpdateFocusable();
        control.InvalidateVisual();

        // 两端测量完才可绘制，在那之前流光无物可循；虚拟链接是指针下的橡皮筋，没有稳定连接可描述
        if (e.Property == CanRenderProperty || e.Property == IsVirtualProperty)
        {
            control.RefreshFlowSubscription();
        }
    }

    // 键盘焦点留着：Delete 键从这条视图冒泡到表面行为。视图自己不再处理指针，命中的可见性交给工具。
    private void UpdateFocusable()
    {
        Focusable = !IsVirtual;
    }

    #endregion

    #region Pooling

    // 改绑：几何重发（曲线发布到新链接），本视图订的输入事件也跟着换一条线。悬停焦点由适配器交给画线的那台控件。
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        RefreshGeometry();
        ResubscribeInput();
        ResubscribePreview();
        RefreshFlowSubscription();
    }

    // 虚拟连线的阶段：订模型自己的 Preview。IsVirtual 也由它决定 —— 橡皮筋就该是虚线、不可聚焦，
    // 而它在 WPF 的通用链接模板里没有标记可绑。
    private void ResubscribePreview()
    {
        var link = DataContext as IConnectionPreviewLink;
        if (ReferenceEquals(link, _previewLink)) return;

        UnsubscribePreview();
        _previewLink = link;
        _preview = link?.Preview ?? ConnectionPreviewState.None;
        if (link is INotifyPropertyChanged notifier) notifier.PropertyChanged += OnPreviewChanged;

        IsVirtual = link is not null;
        InvalidateVisual();
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
        InvalidateVisual();
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

    #endregion

    #region Flow effect

    // 订本树共用的时钟：只有真正在屏、可画、非虚拟、且挂在某棵树上的链接才加入。
    // 池化改绑会先退旧树再订新树，摘树则退订；订阅数归零时时钟自己停表（一棵树始终只有一条周期）。
    private void RefreshFlowSubscription()
    {
        var clock = _loaded && !IsVirtual && CanRender
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

    // 每帧只抄两个标量：属性回调负责重画，几何、配色与弧长表都不参与。
    private void OnClockTick()
    {
        var clock = _clock;
        if (clock is null) return;

        BandHead = clock.BandHead;
        BandIntensity = clock.BandIntensity;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        ResubscribeInput();
        ResubscribePreview();
        RefreshFlowSubscription();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        RefreshFlowSubscription();
        UnsubscribeInput();
        UnsubscribePreview();
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
            var effect = new TransitionEffect
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

    // 弧长表由 Core 的 LinkCurve 拥有：端点变化时重建，同一份同时用于绘制、取点与命中。
    private void RefreshGeometry()
    {
        var curve = LinkCurve.BuildLinkCubic(DataContext as IWorkflowLinkViewModel, StartLeft, StartTop, EndLeft, EndTop, PullMinimum);
        _curve = curve;

        // 线体只在曲线变化时重建；帧回调（BandHead/BandIntensity）不再碰它
        _bodyGeometry = curve.Length > 0 ? BuildSegment(0, curve.Length) : null;

        PublishCurve();
    }

    // 把曲线交给链接的 Helper，界面据此在 canvas-local 空间里判命中。
    private void PublishCurve()
    {
        var link = DataContext as IWorkflowLinkViewModel;
        if (!ReferenceEquals(_publishedLink, link))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = link;
        }

        if (link is not null && _curve is not null)
        {
            link.PublishCurve(_curve, this);
        }
    }

    // 弧长 → 点。Core 的曲线在段内线性插值，取点因此不受采样密度限制。
    private Point PointAtLength(double len)
    {
        var (x, y) = _curve!.PointAtLength(len);
        return new Point(x, y);
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
            ctx.BeginFigure(PointAtLength(from), false, false);

            for (int i = 0; i < curve.Count; i++)
            {
                double l = curve.LengthAt(i);
                if (l <= from || l >= to) continue;
                ctx.LineTo(new Point(curve.XAt(i), curve.YAt(i)), true, false);
            }

            ctx.LineTo(PointAtLength(to), true, false);
        }

        // 线体只在端点变化时重建一次，建好后不再变，冻结掉校验与通知的开销
        geo.Freeze();
        return geo;
    }

    #endregion

    #region Render

    // 线体几何：随曲线重建一次，帧回调只取用
    private StreamGeometry? _bodyGeometry;

    // 静息笔（两层光晕、线体、虚拟虚线）：只在配色/线宽/高亮/虚拟态变化时重建
    private Pen _glowOuterPen = null!;
    private Pen _glowInnerPen = null!;
    private Pen _bodyPen = null!;
    private Pen _dashedPen = null!;
    private Color _linePensColor;
    private double _linePensThickness;
    private bool _linePensVirtual;
    private bool _linePensHighlighted;
    private bool _linePensReady;

    // 彗星每段的笔：混合色与线宽只随段号与配色/线宽变化，逐帧变的透明度交给 PushOpacity
    private readonly Pen[] _bloomPens = new Pen[TailSegments];
    private readonly Pen[] _cometPens = new Pen[TailSegments];
    private Color _cometPensColor;
    private double _cometPensThickness;
    private bool _cometPensReady;

    // 彗星每段一个可复用的几何槽：PathGeometry 能原地改点，冻结的 StreamGeometry 不能
    private readonly TailSlot[] _tailSlots = CreateTailSlots();
    private readonly bool[] _tailLive = new bool[TailSegments];

    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);
        if (!CanRender) return;
        if (_curve is null || _curve.Length <= 0) return;

        var color = _preview switch
        {
            ConnectionPreviewState.ValidTarget => PreviewValidColor,
            ConnectionPreviewState.InvalidTarget => PreviewInvalidColor,
            _ => IsHighlighted ? HighlightColor : LineColor,
        };
        var thickness = IsHighlighted ? LineWidthHighlighted : LineWidth;
        var body = _bodyGeometry;
        if (body is null) return;

        EnsureLinePens(color, thickness);

        // 管壁：两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。圆头圆角，
        // 两端才不像被截断的横截面（WPF 没有 Avalonia 那种一个管两端的 LineCap，两个端点各写一次）
        ctx.DrawGeometry(null, _glowOuterPen, body);
        ctx.DrawGeometry(null, _glowInnerPen, body);

        // 虚拟连线是指针下的橡皮筋：虚线、不流动
        if (IsVirtual)
        {
            ctx.DrawGeometry(null, _dashedPen, body);
            return;
        }

        // 线体本身是静息的：光不在时它只是一根暗线，有了对比彗星才亮得出来
        ctx.DrawGeometry(null, _bodyPen, body);

        if (BandIntensity > 0.001)
        {
            DrawComet(ctx, color, thickness);
        }
    }

    // 同一份状态每帧都会走到这里：颜色、线宽、高亮与虚拟态都没变就直接复用
    private void EnsureLinePens(Color color, double thickness)
    {
        if (_linePensReady
            && color == _linePensColor
            && thickness == _linePensThickness
            && IsVirtual == _linePensVirtual
            && IsHighlighted == _linePensHighlighted)
        {
            return;
        }

        _glowOuterPen = GlowPen(color, 0.10, thickness + 9);
        _glowInnerPen = GlowPen(color, 0.16, thickness + 4);

        var dashedBrush = new SolidColorBrush(AtAlpha(color, 0.75));
        dashedBrush.Freeze();
        _dashedPen = new Pen(dashedBrush, thickness)
        {
            DashStyle = new DashStyle([4.0, 2.0], 0),
            DashCap = PenLineCap.Round,
        };
        _dashedPen.Freeze();

        var bodyBrush = new SolidColorBrush(AtAlpha(color, IsHighlighted ? 0.85 : 0.55));
        bodyBrush.Freeze();
        _bodyPen = new Pen(bodyBrush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        _bodyPen.Freeze();

        _linePensColor = color;
        _linePensThickness = thickness;
        _linePensVirtual = IsVirtual;
        _linePensHighlighted = IsHighlighted;
        _linePensReady = true;
    }

    private static Pen GlowPen(Color color, double alpha, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(AtAlpha(color, alpha)), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    // 每段的混合色与线宽是静态的；透明度才是每帧的量，所以笔不随 BandIntensity 重建
    private void EnsureCometPens(Color color, double thickness)
    {
        if (_cometPensReady && color == _cometPensColor && thickness == _cometPensThickness)
        {
            return;
        }

        for (int k = 0; k < TailSegments; k++)
        {
            double f0 = k / (double)TailSegments;
            var mixed = AtAlpha(Mix(color, Colors.White, f0), 1.0);

            var brush = new SolidColorBrush(mixed);
            brush.Freeze();

            // 光晕笔保持默认平头：原来的临时笔没设 cap，圆头只在两层静息光晕上
            var bloom = new Pen(brush, thickness + 9);
            bloom.Freeze();

            var comet = new Pen(brush, thickness * (0.45 + (0.95 * f0)))
            {
                // 圆头：头部因此是一个逐渐收拢的圆端，而不是截断的一刀
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            comet.Freeze();

            _bloomPens[k] = bloom;
            _cometPens[k] = comet;
        }

        _cometPensColor = color;
        _cometPensThickness = thickness;
        _cometPensReady = true;
    }

    // 彗星：沿弧长切出 [头-尾, 头] 这一段，分若干小段画，每段给一个递减的透明度与变化的颜色。
    // 不用渐变刷是因为它的轴是两端之间的直线，在曲线上会把光打偏（见类注释）。
    private void DrawComet(DrawingContext ctx, Color color, double thickness)
    {
        double head = Math.Clamp(BandHead, 0, 1) * _curve!.Length;
        double tail = TailFraction * _curve.Length;

        EnsureCometPens(color, thickness);

        // 两遍共用同一批分段几何：每段只改点，不再每帧新建几何
        for (int k = 0; k < TailSegments; k++)
        {
            double f0 = k / (double)TailSegments;
            double f1 = (k + 1) / (double)TailSegments;

            double l0 = head - (tail * (1 - f0));
            double l1 = head - (tail * (1 - f1));
            if (l1 <= 0 || l0 >= _curve.Length)
            {
                _tailLive[k] = false;
                continue;
            }

            // 平方衰减：让透明集中在尾段，读起来才像拖尾而不是一条均匀的带
            double a = BandIntensity * f0 * f0;
            if (a <= 0.004)
            {
                _tailLive[k] = false;
                continue;
            }

            UpdateTailGeometry(k, l0, l1);
            _tailLive[k] = true;
        }

        // 两遍：先光晕（更宽更淡）再本体，两遍都跟着头走，所以动感在光晕上也读得出来
        for (int pass = 0; pass < 2; pass++)
        {
            bool bloom = pass == 0;
            var pens = bloom ? _bloomPens : _cometPens;

            for (int k = 0; k < TailSegments; k++)
            {
                if (!_tailLive[k]) continue;

                double f0 = k / (double)TailSegments;
                double a = BandIntensity * f0 * f0;

                // 笔是静态的：逐帧变化的只有这一段的不透明度
                ctx.PushOpacity(Math.Clamp(bloom ? a * 0.22 : a, 0, 1));
                ctx.DrawGeometry(null, pens[k], _tailSlots[k].Geometry);
                ctx.Pop();
            }
        }
    }

    // 把 [from, to] 的采样点写进第 k 个几何槽，结构与 BuildSegment 相同，只是原地改写
    private void UpdateTailGeometry(int k, double from, double to)
    {
        var curve = _curve!;
        to = Math.Min(to, curve.Length);
        from = Math.Clamp(from, 0, curve.Length);

        var slot = _tailSlots[k];
        slot.Points.Clear();

        for (int i = 0; i < curve.Count; i++)
        {
            double l = curve.LengthAt(i);
            if (l <= from || l >= to) continue;
            slot.Points.Add(new Point(curve.XAt(i), curve.YAt(i)));
        }

        slot.Points.Add(PointAtLength(to));
        slot.Figure.StartPoint = PointAtLength(from);
    }

    private static TailSlot[] CreateTailSlots()
    {
        var slots = new TailSlot[TailSegments];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = new TailSlot();
        }

        return slots;
    }

    // 一段彗星的几何：一个图元 + 一个点集合，重复绘制时只改点
    private sealed class TailSlot
    {
        public TailSlot()
        {
            var segment = new PolyLineSegment();
            Figure = new PathFigure { IsClosed = false, IsFilled = false };
            Figure.Segments.Add(segment);
            Points = segment.Points;

            Geometry = new PathGeometry();
            Geometry.Figures.Add(Figure);
        }

        public PathGeometry Geometry { get; }
        public PathFigure Figure { get; }
        public PointCollection Points { get; }
    }

    // 两色之间线性混合（含 alpha），用于尾梢到头部的那一段渐变
    private static Color Mix(Color from, Color to, double t)
    {
        byte L(byte a, byte b) => (byte)Math.Round(a + ((b - a) * t));

        return Color.FromArgb(L(from.A, to.A), L(from.R, to.R), L(from.G, to.G), L(from.B, to.B));
    }

    // 取渲染用色：色相不变，透明度独立给
    private static Color AtAlpha(Color color, double alpha)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    #endregion
}
