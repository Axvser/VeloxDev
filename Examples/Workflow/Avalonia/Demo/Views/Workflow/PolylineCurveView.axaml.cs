using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Collections.Generic;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo;

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
/// The type keeps the name <c>PolylineCurveView</c> because the surface template binds it by that name; it has
/// not drawn a polyline since the geometry was replaced.
/// </para>
/// <para>
/// Supports click-to-select (highlighted) and <c>Delete</c> to remove.
/// </para>
/// </summary>
public partial class PolylineCurveView : Control
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

        RefreshGeometry();

        CurveSelectionManager.SelectionChanged += owner =>
        {
            if (owner != this && IsSelected)
            {
                IsSelected = false;
            }
        };
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
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<PolylineCurveView, bool>(nameof(IsSelected), false);

    /// <summary>How far along the link the comet's head has travelled, as a fraction of its length.</summary>
    /// <remarks>
    /// Animated rather than computed, and registered with <see cref="AffectsRender{T}"/> so each frame the
    /// transition writes is also a frame this view repaints. The value carries no geometry of its own — the
    /// arc-length table turns it into a point — which is what lets the light follow a curve.
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
    public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public double BandHead { get => GetValue(BandHeadProperty); set => SetValue(BandHeadProperty, value); }
    public double BandIntensity { get => GetValue(BandIntensityProperty); set => SetValue(BandIntensityProperty, value); }

    static PolylineCurveView()
    {
        AffectsRender<PolylineCurveView>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty, IsSelectedProperty,
            BandHeadProperty, BandIntensityProperty);
    }

    #endregion

    #region Flow effect

    // 每视图构建：周期只写两个标量，几何、配色与弧长表都不参与
    // 匀速（Eases.Default 就是恒等）—— 流水不该有缓动，头部的速度一变化就不像在流了
    private Transition<PolylineCurveView> BuildFlow() => Transition<PolylineCurveView>.Create()
        // 相位一：从发送端出发，一边走一边亮起
        .Property(v => v.BandHead, BandFormed)
        .Property(v => v.BandIntensity, 1d)
        .Effect(new TransitionEffect()
        {
            Duration = EnterDuration,
            Ease = Eases.Default,
        })
        .Then()
        // 相位二：全亮行进——这一段读起来才是流动而非脉冲
        .Property(v => v.BandHead, BandLeaving)
        .Effect(new TransitionEffect()
        {
            Duration = TravelDuration,
            Ease = Eases.Default,
        })
        .Then()
        // 相位三：到达并熄灭。两端都是「没有光」的状态，循环接缝才看不出来
        .Property(v => v.BandHead, 1d)
        .Property(v => v.BandIntensity, 0d)
        .Effect(new TransitionEffect()
        {
            Duration = ExitDuration,
            Ease = Eases.Default,
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartFlow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // A pooled view released and reused for another link must not leave the old animation running on it.
        StopFlow();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StartLeftProperty || change.Property == StartTopProperty
            || change.Property == EndLeftProperty || change.Property == EndTopProperty)
        {
            RefreshGeometry();
        }

        // A link becomes drawable only once both endpoints have been measured, and the flow has nothing to
        // travel along before that — while a virtual one is the rubber band under the pointer, which has no
        // settled connection to describe.
        if (change.Property == CanRenderProperty || change.Property == IsVirtualProperty)
        {
            if (IsVirtual || !CanRender)
            {
                StopFlow();
            }
            else
            {
                StartFlow();
            }
        }
    }

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
            ctx.BeginFigure(PointAtLength(from), false);

            for (int i = 0; i < _samples.Length; i++)
            {
                double l = _cumulative[i];
                if (l <= from || l >= to) continue;
                ctx.LineTo(_samples[i]);
            }

            ctx.LineTo(PointAtLength(to));
        }

        return geo;
    }

    #endregion

    #region Render

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!CanRender) return;
        if (_samples.Length < 2 || _length <= 0) return;

        var color = IsSelected ? Colors.OrangeRed : LineColor;
        var thickness = IsSelected ? LineThickness + 1.5 : LineThickness;
        var body = BuildSegment(0, _length);

        // 管壁：两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。圆头圆角，
        // 两端才不像被截断的横截面
        context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(color, 0.10), thickness + 9) { LineCap = PenLineCap.Round }, body);
        context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(color, 0.16), thickness + 4) { LineCap = PenLineCap.Round }, body);

        // 虚拟连线是指针下的橡皮筋：虚线、不流动
        if (IsVirtual)
        {
            var dashed = new Pen(new ImmutableSolidColorBrush(color, 0.75), thickness)
            {
                DashStyle = new DashStyle([4.0, 2.0], 0),
            };
            context.DrawGeometry(null, dashed, body);
            return;
        }

        // 线体本身是静息的：光不在时它只是一根暗线，有了对比彗星才亮得出来
        var bodyAlpha = IsSelected ? 0.85 : 0.55;
        context.DrawGeometry(null,
            new Pen(new ImmutableSolidColorBrush(color, bodyAlpha), thickness) { LineCap = PenLineCap.Round }, body);

        if (BandIntensity > 0.001)
        {
            DrawComet(context, color, thickness);
        }
    }

    // 彗星：沿弧长切出 [头-尾, 头] 这一段，分若干小段画，每段给一个递减的透明度与变化的颜色。
    // 不用渐变刷是因为它的轴是两端之间的直线，在曲线上会把光打偏（见类注释）。
    private void DrawComet(DrawingContext context, Color color, double thickness)
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
                    ? new Pen(new ImmutableSolidColorBrush(c, a * 0.22), thickness + 9)
                    : new Pen(new ImmutableSolidColorBrush(c, a), thickness * (0.45 + 0.95 * f0))
                    {
                        // 圆头：头部因此是一个逐渐收拢的圆端，而不是截断的一刀
                        LineCap = PenLineCap.Round,
                    };

                context.DrawGeometry(null, pen, BuildSegment(l0, l1));
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

    #region Interaction

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        IsSelected = true;
        CurveSelectionManager.Select(this);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CurveSelectionManager.Deselect(this);
        IsSelected = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete && IsSelected)
        {
            if (DataContext is IWorkflowLinkViewModel vm)
                vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private bool HitTestLine(Point pt)
    {
        const double hitRadius = 6.0;

        for (int i = 1; i < _samples.Length; i++)
        {
            if (DistanceToSegment(pt, _samples[i - 1], _samples[i]) <= hitRadius)
                return true;
        }

        return false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        bool over = HitTestLine(pt);
        if (over && !IsSelected)
        {
            IsSelected = true;
            CurveSelectionManager.Select(this);
            Focus();
        }
        else if (!over && IsSelected)
        {
            CurveSelectionManager.Deselect(this);
            IsSelected = false;
        }
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = (ab.X * ab.X) + (ab.Y * ab.Y);
        if (len2 < 0.0001) return new Vector(p.X - a.X, p.Y - a.Y).Length;
        double t = (((p.X - a.X) * ab.X) + ((p.Y - a.Y) * ab.Y)) / len2;
        t = Math.Clamp(t, 0.0, 1.0);
        var proj = new Point(a.X + (t * ab.X), a.Y + (t * ab.Y));
        return new Vector(p.X - proj.X, p.Y - proj.Y).Length;
    }

    #endregion
}
