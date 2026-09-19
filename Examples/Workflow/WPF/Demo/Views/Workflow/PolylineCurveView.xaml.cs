using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs.
/// Hover to highlight, Delete to remove, and a travelling highlight so the direction of data flow is
/// readable at a glance.
/// </summary>
public partial class PolylineCurveView : UserControl
{
    public PolylineCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        Focusable = true;
        Panel.SetZIndex(this, -100);

        // 画刷归视图所有；这里只建它，指向、配色与链都由 AimFlowBrush 完成
        AimFlowBrush();

        // WPF 的 UserControl 没有可重写的虚拟挂载方法，用 Loaded/Unloaded 这对事件（WPF 适配器也用它挂载画布）
        // 池化复用只隐藏视图并改绑 DataContext，不摘树，两个事件都不触发；锚点属性重绑已让画刷重新指向，周期也从未停过，只有落到另一颜色的链接上时才由 AimFlowBrush 重建链并重启
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        MouseEnter += (_, _) => { IsHighlighted = true; Focus(); };
        MouseLeave += (_, _) => IsHighlighted = false;
        MouseMove += OnHoverMouseMove;
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
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(PolylineCurveView), new PropertyMetadata(Colors.Cyan, OnRenderChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnRenderChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PolylineCurveView)d;
        control.UpdateInteractivity();
        control.InvalidateVisual();

        // 渐变沿链接自身轴向、由链接自身颜色混合：端点与颜色都是画刷的输入，而非绘制代码的
        if (e.Property == StartLeftProperty || e.Property == StartTopProperty
            || e.Property == EndLeftProperty || e.Property == EndTopProperty
            || e.Property == LineColorProperty)
        {
            control.AimFlowBrush();
        }

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

    // 光带半宽（渐变偏移单位）
    private const double BandHalfWidth = 0.04;

    // 三段相位各自结束时光带中心的位置：成形、全亮行进、退去
    private const double BandStart = 0.06;
    private const double BandFormed = 0.34;
    private const double BandLeaving = 0.66;
    private const double BandExit = 0.94;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(550);

    /// <summary>
    /// The brush the link is drawn with, and the object the flow animates: a gradient along the link's own
    /// axis whose middle stop is the band. It is a property of this control rather than something a model
    /// holds, so the animated paths read straight off the view — <c>FlowBrush.GradientStops[1].Offset</c> and
    /// <c>[1].Color</c> — and there is no value in between to map back into geometry.
    /// </summary>
    /// <remarks>
    /// MappingMode is absolute rather than WPF's default RelativeToBoundingBox: the four points the link is
    /// drawn from are the control's own space and the link runs diagonally, so a relative gradient would sweep
    /// across the bounding box instead of along the line. The two points themselves are set in
    /// <see cref="AimFlowBrush"/>, from the link's own endpoints.
    /// </remarks>
    public LinearGradientBrush FlowBrush { get; } = new()
    {
        SpreadMethod = GradientSpreadMethod.Pad,
        MappingMode = BrushMappingMode.Absolute,
    };

    // 光带与箭头颜色：链接本色提到全不透明
    private Color Lit { get; set; }

    // 线体静息色：亮色按 alpha 变暗到约 62%
    private Color Dim { get; set; }

    private Transition<PolylineCurveView>? _flow;
    private bool _running;

    // 每视图构建：两个端点取自该链接自己的颜色（静态声明会把读到的值共享给之后每次执行，也会被处理器一直持有视图）
    // 路径直达画刷：GradientStops[1] 是光带、两侧是肩；匀速所以不用缓动
    // WPF 还须自带重绘：DrawingContext 按引用持有画刷却不订阅它，只写停靠点 OnRender 不会再次进入，显式 InvalidateVisual 才每次写进入一次
    private Transition<PolylineCurveView> BuildFlow() => Transition<PolylineCurveView>.Create()
        // 相位一：一边成形一边进入（走三分之一路程，同时由静息色变亮）
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandFormed - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandFormed)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandFormed + BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Color, Lit)
        // 重绘挂 LateUpdate（Update 在应用帧之前，LateUpdate 之后），且每段都挂；重放每周期照发 Update/LateUpdate，Clone 也复制处理器，所以重绘不丢
        .Effect(e =>
        {
            e.Duration = EnterDuration;
            e.Ease = Eases.Default;
            e.LateUpdate += (_, _) => InvalidateVisual();
        })
        .Then()
        // 相位二：保持全亮只移动——这一段读起来才是流动而非脉冲
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandLeaving - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandLeaving)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandLeaving + BandHalfWidth)
        .Effect(e =>
        {
            e.Duration = TravelDuration;
            e.Ease = Eases.Default;
            e.LateUpdate += (_, _) => InvalidateVisual();
        })
        .Then()
        // 相位三：一边退回静息色一边离开；周期两端都是均匀暗色，循环接缝才看不出来
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandExit - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandExit)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandExit + BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Color, Dim)
        .Effect(e =>
        {
            e.Duration = ExitDuration;
            e.Ease = Eases.Default;
            e.LateUpdate += (_, _) => InvalidateVisual();
        })
        .Repeat(int.MaxValue);

    // 链接移动（缩放与拖拽每帧都改锚点）或变色时调用，变色要重建链：两个端点就是它的颜色
    // 这里不写光带位置：那些停靠点归周期所有，手势期间抢写会让光带抖动
    private void AimFlowBrush()
    {
        // 绝对映射（随画刷一起设）：这两点就是控件空间里链接自己的端点，不是包围盒的比例
        FlowBrush.StartPoint = new Point(StartLeft, StartTop);
        FlowBrush.EndPoint = new Point(EndLeft, EndTop);

        var lit = LitOf(LineColor);
        if (_flow is not null && lit == Lit)
        {
            return;
        }

        Lit = lit;
        Dim = DimOf(lit);
        _flow = BuildFlow();

        var stops = FlowBrush.GradientStops;
        if (stops.Count == 0)
        {
            stops.Add(new GradientStop(Dim, BandStart - BandHalfWidth));
            stops.Add(new GradientStop(Dim, BandStart));
            stops.Add(new GradientStop(Dim, BandStart + BandHalfWidth));
        }
        else
        {
            stops[0].Color = Dim;
            stops[2].Color = Dim;
        }

        // 视图被复用到另一种颜色的链接上时，按自己的颜色重新起周期
        if (_running)
        {
            StartFlow();
        }
    }

    // 亮色：各通道向白抬 45%；本 demo 的白链接已无处可抬，光带只靠 alpha 撑，这也是静息色必须暗的原因
    private static Color LitOf(Color color)
    {
        const double lift = 0.45;

        byte Up(byte channel) => (byte)Math.Round(channel + (255 - channel) * lift);

        return Color.FromArgb(255, Up(color.R), Up(color.G), Up(color.B));
    }

    // 靠 alpha 变暗取反差，色相不变；往白里提在青线（2px 线、暗底）上几乎看不出，白链接更是白提白
    private static Color DimOf(Color color) => Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    // 从发送端起动周期；变得可绘制和载入树时都起动：复用换链接后要动的是新链接，出树又回来的也要重新跑起来
    private void StartFlow()
    {
        if (IsVirtual || !CanRender)
        {
            StopFlow();
            return;
        }

        AimFlowBrush();

        // 声明从目标读起始值，Execute 前画刷要先落到周期起点；循环在每个接缝重放捕获的起点，故这也是之后每周期的起始状态
        var stops = FlowBrush.GradientStops;
        stops[0].Offset = BandStart - BandHalfWidth;
        stops[1].Offset = BandStart;
        stops[2].Offset = BandStart + BandHalfWidth;
        stops[1].Color = Dim;

        _flow!.Execute(this);
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

    #region Render

    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);
        if (!CanRender) return;

        var points = BuildPoints();
        if (points.Count < 2) return;

        var color = IsHighlighted ? Colors.OrangeRed : LineColor;
        var thickness = IsHighlighted ? 3.5 : 2.0;

        // 流动高亮只对稳定连接有意义：虚拟链接是指针下的橡皮筋，高亮的已经是视线所在，两者都用平色；其余用流光写入的渐变
        var flat = new SolidColorBrush(color);
        Brush brush = IsHighlighted || IsVirtual ? flat : FlowBrush;

        // 箭头是终点标记，用光带亮色而非渐变：线静息是暗的，箭头跟着暗就成了链接上永不点亮的那一处
        Brush arrowBrush = IsHighlighted ? flat : new SolidColorBrush(Lit);

        var pen = IsVirtual
            ? new Pen(brush, thickness) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }
            : new Pen(brush, thickness);

        if (IsHighlighted)
        {
            var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B)), thickness + 6);
            for (int i = 0; i < points.Count - 1; i++)
                ctx.DrawLine(glowPen, points[i], points[i + 1]);
        }

        for (int i = 0; i < points.Count - 1; i++)
            ctx.DrawLine(pen, points[i], points[i + 1]);

        if (!IsVirtual)
            DrawArrowhead(ctx, points[^2], points[^1], arrowBrush, thickness);
    }

    private List<Point> BuildPoints()
    {
        var s = new Point(StartLeft, StartTop);
        var e = new Point(EndLeft, EndTop);
        double dx = EndLeft - StartLeft;
        const double phi = 0.6180339887;
        double stub = dx / 2.0 * (1.0 - phi);
        var p1 = new Point(s.X + stub, s.Y);
        var p4 = new Point(e.X - stub, e.Y);
        return [s, p1, p4, e];
    }

    private static void DrawArrowhead(DrawingContext ctx, Point from, Point tip, Brush brush, double thickness)
    {
        var t = new Vector(tip.X - from.X, tip.Y - from.Y);
        if (t.LengthSquared < 0.001) return;
        t.Normalize();
        double al = 12, aw = 8;
        var perp = new Vector(-t.Y, t.X);
        var baseP = new Point(tip.X - t.X * al, tip.Y - t.Y * al);
        var w1 = new Point(baseP.X + perp.X * (aw / 2), baseP.Y + perp.Y * (aw / 2));
        var w2 = new Point(baseP.X - perp.X * (aw / 2), baseP.Y - perp.Y * (aw / 2));
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(tip, true, true);
            c.LineTo(w1, true, false);
            c.LineTo(w2, true, false);
        }
        geo.Freeze();
        ctx.DrawGeometry(brush, null, geo);
    }

    #endregion

    #region Interaction

    private void OnHoverMouseMove(object sender, MouseEventArgs e)
    {
        var pt = e.GetPosition(this);
        bool over = HitTestLine(pt);
        if (over && !IsHighlighted) { IsHighlighted = true; Focus(); }
        else if (!over && IsHighlighted) IsHighlighted = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete && IsHighlighted)
        {
            if (DataContext is IWorkflowLinkViewModel vm)
                vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private bool HitTestLine(Point pt)
    {
        const double hitRadius = 6.0;
        var pts = BuildPoints();
        for (int i = 0; i < pts.Count - 1; i++)
            if (DistSeg(pt, pts[i], pts[i + 1]) <= hitRadius) return true;
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
