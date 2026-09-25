using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// One connection between two ports, drawn as a single cubic curve that leaves each end horizontally.
/// <para>
/// The light travelling along it is a comet — a bright head, a tail that fades behind it, and a halo that
/// follows the head — and it is cut out of the curve <b>by arc length</b> rather than by a gradient brush.
/// That is the whole reason this view keeps its own sample table: a <see cref="LinearGradientBrush"/>'s axis
/// is the straight line between the two ends, so on a curve it lights the string rather than the rope. The
/// brightness stops tracking the bend, the light appears to speed up and slow down as it goes round, and the
/// kink where the old stub met its diagonal reads as a kink in the light itself.
/// </para>
/// <para>
/// This file is the WPF reading of Avalonia's <c>Views/Workflow/PolylineCurveView.axaml.cs</c>; the geometry,
/// the table and the comet are the same construction. Two things that file warns about are moot here, and one
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
/// table is rebuilt whenever an endpoint moves — which, while a node is dragged or the surface is zoomed, is
/// every frame. 128 samples of a cubic is a few hundred flops, and the alternative (sampling at draw time,
/// twice per tail segment) is what the table exists to avoid.
/// </item>
/// </list>
/// <para>
/// The type keeps the name <c>PolylineCurveView</c> because the surface template binds it by that name; it has
/// not drawn a polyline since the geometry was replaced.
/// </para>
/// <para>
/// Supports click-to-select (highlighted), <c>Delete</c> and a right-click menu to remove.
/// </para>
/// </summary>
public partial class PolylineCurveView : UserControl
{
    // 弧长表的分辨率。128 段在缩放上限（Scale 10）下也看不出折线感，而每帧重建它只是几百次算术。
    private const int SampleCount = 128;

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

    // 弧长表：_cumulative[i] 是 _samples[0..i] 的累计长度，_length 是全长。
    // 三者只在端点变化时重建 —— 每帧渲染要按弧长取点，现算不划算。
    private Point[] _samples = [];
    private double[] _cumulative = [];
    private double _length;

    private Transition<PolylineCurveView>? _flow;
    private bool _running;

    public PolylineCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        Focusable = true;
        Panel.SetZIndex(this, -100);

        RefreshGeometry();

        // WPF 的 UserControl 没有可重写的虚拟挂载方法，用 Loaded/Unloaded 这对事件（WPF 适配器也用它挂载画布）
        // 池化复用只隐藏视图并改绑 DataContext，不摘树，两个事件都不触发；锚点属性重绑已让几何重建，周期也从未停过
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        MouseEnter += (_, _) => { IsHighlighted = true; Focus(); };
        MouseLeave += (_, _) => IsHighlighted = false;
        MouseMove += OnHoverMouseMove;

        // 右键：菜单由 WPF 自己在右键抬起时开，这里只做两件事 —— 不在线上的那次取消掉，以及把菜单要删的
        // 那条线选中。视图的命中面是画出来的描边（最外那圈辉光），而「不在线上」那次仍要自己取消 ——
        // 命中面一旦被改粗（例如给视图加上背景），空白处也会触发
        ContextMenu = BuildMenu();
        ContextMenuOpening += OnContextMenuOpening;

        // 悬停取焦点会连带触发 WPF 的默认行为：拿到焦点的元素请求「把自己滚进视口」，ScrollViewer 照办 ——
        // 鼠标一碰到线画布就跳一段，跳多远看当时的偏移。焦点本身要留着（Delete 键靠它），所以只吃掉这条请求。
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
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }
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

        control.UpdateInteractivity();
        control.InvalidateVisual();

        // 两端测量完才可绘制，在那之前流光无物可循；虚拟链接是指针下的橡皮筋，没有稳定连接可描述
        if (e.Property == CanRenderProperty || e.Property == IsVirtualProperty)
        {
            if (control.IsVirtual || !control.CanRender)
            {
                control.StopFlow();
            }
            else
            {
                control.StartFlow();
            }
        }
    }

    private void UpdateInteractivity()
    {
        IsHitTestVisible = !IsVirtual;
        Focusable = !IsVirtual;
    }

    #endregion

    #region Flow effect

    // 每视图构建：周期只写两个标量，几何、配色与弧长表都不参与
    // 匀速（Eases.Default 就是恒等）—— 流水不该有缓动，头部的速度一变化就不像在流了
    private Transition<PolylineCurveView> BuildFlow() => Transition<PolylineCurveView>.Create()
        // 相位一：从发送端出发，一边走一边亮起
        .Property(v => v.BandHead, BandFormed)
        .Property(v => v.BandIntensity, 1d)
        .Effect(e =>
        {
            e.Duration = EnterDuration;
            e.Ease = Eases.Default;
        })
        .Then()
        // 相位二：全亮行进——这一段读起来才是流动而非脉冲
        .Property(v => v.BandHead, BandLeaving)
        .Effect(e =>
        {
            e.Duration = TravelDuration;
            e.Ease = Eases.Default;
        })
        .Then()
        // 相位三：到达并熄灭。两端都是「没有光」的状态，循环接缝才看不出来
        .Property(v => v.BandHead, 1d)
        .Property(v => v.BandIntensity, 0d)
        .Effect(e =>
        {
            e.Duration = ExitDuration;
            e.Ease = Eases.Default;
        })
        .Repeat(int.MaxValue);

    // 从发送端起动周期；视图复用后会换链接，所以在挂载时起动
    private void StartFlow()
    {
        if (IsVirtual || !CanRender)
        {
            StopFlow();
            return;
        }

        RefreshGeometry();
        _flow ??= BuildFlow();

        // 声明从目标读起值，所以执行前必须把两个标量摆到周期起点；循环在每个接缝重放这一份起始状态
        BandHead = 0;
        BandIntensity = 0;

        _flow.Execute(this);
        _running = true;
    }

    // 停周期：视图被释放复用时不能留着旧动画在跑
    private void StopFlow()
    {
        if (!_running)
        {
            return;
        }

        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _running = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => StartFlow();

    private void OnUnloaded(object sender, RoutedEventArgs e) => StopFlow();

    #endregion

    #region Geometry

    // 弧长表。端点变化时重建，渲染时只读。
    private void RefreshGeometry()
    {
        var samples = new Point[SampleCount + 1];
        for (int i = 0; i <= SampleCount; i++)
        {
            samples[i] = BezierAt(i / (double)SampleCount);
        }

        var cumulative = new double[SampleCount + 1];
        for (int i = 1; i <= SampleCount; i++)
        {
            var d = samples[i] - samples[i - 1];
            cumulative[i] = cumulative[i - 1] + Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
        }

        _samples = samples;
        _cumulative = cumulative;
        _length = cumulative[SampleCount];
    }

    // 两个控制点各自水平拉开：连线因此从两端水平出线、中间平滑过渡，没有折角
    private (Point C1, Point C2) Controls()
    {
        double dx = EndLeft - StartLeft;

        // 最小拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段，失去「从端口水平出来」的形状
        double pull = Math.Max(40, Math.Abs(dx) * 0.5);

        return (new Point(StartLeft + pull, StartTop), new Point(EndLeft - pull, EndTop));
    }

    private Point BezierAt(double t)
    {
        var (c1, c2) = Controls();
        double u = 1 - t;
        double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;

        return new Point(
            (a * StartLeft) + (b * c1.X) + (c * c2.X) + (d * EndLeft),
            (a * StartTop) + (b * c1.Y) + (c * c2.Y) + (d * EndTop));
    }

    // 弧长 → 点。二分找所在采样段再线性插值，所以取点是精确到亚像素的，不受采样密度限制
    private Point PointAtLength(double len)
    {
        if (_length <= 0) return new Point(StartLeft, StartTop);

        len = Math.Clamp(len, 0, _length);

        int lo = 0, hi = _cumulative.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (_cumulative[mid] <= len) lo = mid;
            else hi = mid;
        }

        double span = _cumulative[hi] - _cumulative[lo];
        double t = span <= 0 ? 0 : (len - _cumulative[lo]) / span;

        return new Point(
            _samples[lo].X + ((_samples[hi].X - _samples[lo].X) * t),
            _samples[lo].Y + ((_samples[hi].Y - _samples[lo].Y) * t));
    }

    // 取 [from, to] 这一段弧长上的折线几何。两端各自插值到精确位置，中间用现成采样点
    private StreamGeometry BuildSegment(double from, double to)
    {
        to = Math.Min(to, _length);
        from = Math.Clamp(from, 0, _length);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(PointAtLength(from), false, false);

            for (int i = 0; i < _samples.Length; i++)
            {
                double l = _cumulative[i];
                if (l <= from || l >= to) continue;
                ctx.LineTo(_samples[i], true, false);
            }

            ctx.LineTo(PointAtLength(to), true, false);
        }

        // 冻结：几何每帧被重画（彗星的每一小段都是它），不再变的就不该再花校验与通知的钱
        geo.Freeze();
        return geo;
    }

    #endregion

    #region Render

    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);
        if (!CanRender) return;
        if (_samples.Length < 2 || _length <= 0) return;

        var color = IsHighlighted ? Colors.OrangeRed : LineColor;
        var thickness = IsHighlighted ? LineWidthHighlighted : LineWidth;
        var body = BuildSegment(0, _length);

        // 管壁：两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。圆头圆角，
        // 两端才不像被截断的横截面（WPF 没有 Avalonia 那种一个管两端的 LineCap，两个端点各写一次）
        ctx.DrawGeometry(null, GlowPen(color, 0.10, thickness + 9), body);
        ctx.DrawGeometry(null, GlowPen(color, 0.16, thickness + 4), body);

        // 虚拟连线是指针下的橡皮筋：虚线、不流动
        if (IsVirtual)
        {
            var dashed = new Pen(new SolidColorBrush(AtAlpha(color, 0.75)), thickness)
            {
                DashStyle = new DashStyle([4.0, 2.0], 0),
                DashCap = PenLineCap.Round,
            };
            ctx.DrawGeometry(null, dashed, body);
            return;
        }

        // 线体本身是静息的：光不在时它只是一根暗线，有了对比彗星才亮得出来
        var bodyPen = new Pen(new SolidColorBrush(AtAlpha(color, IsHighlighted ? 0.85 : 0.55)), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        ctx.DrawGeometry(null, bodyPen, body);

        if (BandIntensity > 0.001)
        {
            DrawComet(ctx, color, thickness);
        }
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

    // 彗星：沿弧长切出 [头-尾, 头] 这一段，分若干小段画，每段给一个递减的透明度与变化的颜色。
    // 不用渐变刷是因为它的轴是两端之间的直线，在曲线上会把光打偏（见类注释）。
    private void DrawComet(DrawingContext ctx, Color color, double thickness)
    {
        double head = Math.Clamp(BandHead, 0, 1) * _length;
        double tail = TailFraction * _length;

        // 两遍：先光晕（更宽更淡）再本体，两遍都跟着头走，所以动感在光晕上也读得出来
        for (int pass = 0; pass < 2; pass++)
        {
            bool bloom = pass == 0;

            for (int k = 0; k < TailSegments; k++)
            {
                double f0 = k / (double)TailSegments;      // 0 = 尾梢，1 = 头
                double f1 = (k + 1) / (double)TailSegments;

                double l0 = head - (tail * (1 - f0));
                double l1 = head - (tail * (1 - f1));
                if (l1 <= 0 || l0 >= _length) continue;

                // 平方衰减：让透明集中在尾段，读起来才像拖尾而不是一条均匀的带
                double a = BandIntensity * f0 * f0;
                if (a <= 0.004) continue;

                // 尾梢是本体的颜色，越靠近头越白 —— 白热只发生在头部
                var c = Mix(color, Colors.White, f0);

                var pen = bloom
                    ? new Pen(new SolidColorBrush(AtAlpha(c, a * 0.22)), thickness + 9)
                    : new Pen(new SolidColorBrush(AtAlpha(c, a)), thickness * (0.45 + (0.95 * f0)))
                    {
                        // 圆头：头部因此是一个逐渐收拢的圆端，而不是截断的一刀
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round,
                    };

                ctx.DrawGeometry(null, pen, BuildSegment(l0, l1));
            }
        }
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

    #region Interaction

    private void OnHoverMouseMove(object sender, MouseEventArgs e)
    {
        var pt = e.GetPosition(this);
        bool over = HitTestLine(pt);
        if (over && !IsHighlighted) { IsHighlighted = true; Focus(); }
        else if (!over && IsHighlighted) IsHighlighted = false;
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // 不在线上：这次右键不是这条线的，菜单不开（画布上空白处右键因此什么也不弹）
        if (!HitTestLine(Mouse.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        // 未选中先选中 —— 菜单里的删除作用于当前这条线
        IsHighlighted = true;
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete && IsHighlighted)
        {
            DeleteLink();
            e.Handled = true;
        }
    }

    // 菜单只有一项，且不绑命令：WPF 的 ContextMenu 是独立视觉树，DataContext 不会自己跟过来；
    // 视图又会被池化改绑给另一条链接，所以菜单项在点击那一刻才去读视图自己的 DataContext
    private ContextMenu BuildMenu()
    {
        var item = new MenuItem { Header = "删除连线" };
        item.Click += (_, _) => DeleteLink();

        return new ContextMenu { Items = { item } };
    }

    private void DeleteLink()
    {
        if (DataContext is IWorkflowLinkViewModel vm)
            vm.DeleteCommand.Execute(null);
    }

    private bool HitTestLine(Point pt)
    {
        const double hitRadius = 6.0;

        // 命中沿同一张弧长表走：曲线换了之后，按老的四点折线判命中会在弯的地方对不上手指
        for (int i = 1; i < _samples.Length; i++)
        {
            if (DistSeg(pt, _samples[i - 1], _samples[i]) <= hitRadius) return true;
        }

        return false;
    }

    private static double DistSeg(Point p, Point a, Point b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double len2 = abx * abx + aby * aby;
        if (len2 < 0.0001) return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0, 1);
        return (p - new Point(a.X + t * abx, a.Y + t * aby)).Length;
    }

    #endregion
}
