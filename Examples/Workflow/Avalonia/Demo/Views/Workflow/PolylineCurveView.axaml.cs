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
/// Orthogonal (polyline) connection: H-stub → vertical jog → H-stub → tip.
/// Supports click-to-select (highlighted) and Delete to remove, and carries a travelling highlight so
/// the direction of data flow is readable at a glance.
/// </summary>
public partial class PolylineCurveView : Control
{
    public PolylineCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        Focusable = true;

        // 画刷归视图所有；这里只建它，指向、配色与链都由 AimFlowBrush 完成
        AimFlowBrush();

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
        AvaloniaProperty.Register<PolylineCurveView, Color>(nameof(LineColor), Colors.Cyan);
    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<PolylineCurveView, double>(nameof(LineThickness), 2.0);
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<PolylineCurveView, bool>(nameof(IsSelected), false);

    public double StartLeft { get => GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Color LineColor { get => GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public double LineThickness { get => GetValue(LineThicknessProperty); set => SetValue(LineThicknessProperty, value); }
    public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }

    static PolylineCurveView()
    {
        AffectsRender<PolylineCurveView>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty, IsSelectedProperty);
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
    public LinearGradientBrush FlowBrush { get; } = new()
    {
        SpreadMethod = GradientSpreadMethod.Pad,
    };

    // 光带与箭头颜色：链接本色提到全不透明
    private Color Lit { get; set; }

    // 线体静息色：亮色按 alpha 变暗到约 62%
    private Color Dim { get; set; }

    private Transition<PolylineCurveView>? _flow;
    private bool _running;

    // 每视图构建：两个端点取自该链接自己的颜色，静态声明会把读到的那份值共享给之后每次执行
    // 路径直达画刷：GradientStops[1] 是光带、两侧是肩；匀速所以不用缓动
    private Transition<PolylineCurveView> BuildFlow() => Transition<PolylineCurveView>.Create()
        // 相位一：一边成形一边进入（走三分之一路程，同时由静息色变亮）
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandFormed - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandFormed)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandFormed + BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Color, Lit)
        .Effect(new TransitionEffect()
        {
            Duration = EnterDuration,
            Ease = Eases.Default,
        })
        .Then()
        // 相位二：保持全亮只移动——这一段读起来才是流动而非脉冲
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandLeaving - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandLeaving)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandLeaving + BandHalfWidth)
        .Effect(new TransitionEffect()
        {
            Duration = TravelDuration,
            Ease = Eases.Default,
        })
        .Then()
        // 相位三：一边退回静息色一边离开；周期两端都是均匀暗色，循环接缝才看不出来
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandExit - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandExit)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandExit + BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Color, Dim)
        .Effect(new TransitionEffect()
        {
            Duration = ExitDuration,
            Ease = Eases.Default,
        })
        .Repeat(int.MaxValue);

    // 链接移动（缩放与拖拽每帧都改锚点）或变色时调用，变色要重建链：两个端点就是它的颜色
    // 这里不写光带位置：那些停靠点归周期所有，手势期间抢写会让光带抖动
    private void AimFlowBrush()
    {
        // 绝对坐标：四个点在控件自身坐标系里且连线是斜的，相对渐变会扫过包围盒而不是沿线
        FlowBrush.StartPoint = new RelativePoint(StartLeft, StartTop, RelativeUnit.Absolute);
        FlowBrush.EndPoint = new RelativePoint(EndLeft, EndTop, RelativeUnit.Absolute);

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

    // 亮色：各通道向白抬 45%（白链接也留出更亮处）
    private static Color LitOf(Color color)
    {
        const double lift = 0.45;

        byte Up(byte channel) => (byte)Math.Round(channel + (255 - channel) * lift);

        return Color.FromArgb(255, Up(color.R), Up(color.G), Up(color.B));
    }

    // 靠 alpha 变暗取反差，色相不变；往白里提在青线（本 demo）和白线上都几乎看不出（实测过）
    private static Color DimOf(Color color) => Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    // 从发送端起动周期；视图复用后会换链接，所以在挂载时起动
    private void StartFlow()
    {
        if (IsVirtual || !CanRender)
        {
            StopFlow();
            return;
        }

        AimFlowBrush();

        // The transition reads its start values from the target, so the brush has to be at the cycle's start
        // before Execute — and the loop replays that captured start at every seam, so this is also the state
        // each later cycle begins from.
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
            || change.Property == EndLeftProperty || change.Property == EndTopProperty
            || change.Property == LineColorProperty)
        {
            AimFlowBrush();
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

    #region Render

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!CanRender) return;

        var points = BuildPoints();
        if (points.Count < 2) return;

        var color = IsSelected ? Colors.OrangeRed : LineColor;
        var thickness = IsSelected ? LineThickness + 1.5 : LineThickness;

        // The travelling highlight is only meaningful on a settled connection. A virtual link is the rubber
        // band under the pointer and a selected one is already highlighted, so both keep a flat pen.
        ImmutableSolidColorBrush GetSolid() => new(color);
        IBrush brush = IsSelected || IsVirtual ? GetSolid() : FlowBrush;

        // The arrowhead is the destination marker, so it carries the band's colour rather than the
        // gradient: the line rests dim, and an arrowhead dimmed with it would be the one part of the link
        // that never lights up.
        IBrush arrowBrush = IsSelected ? GetSolid() : new ImmutableSolidColorBrush(Lit);

        Pen pen;
        if (IsVirtual)
            pen = new Pen(brush, thickness) { DashStyle = new DashStyle([4.0, 2.0], 0) };
        else
            pen = new Pen(brush, thickness);

        // Draw segments
        for (int i = 0; i < points.Count - 1; i++)
            context.DrawLine(pen, points[i], points[i + 1]);

        // Selection glow — translucent wider stroke behind
        if (IsSelected)
        {
            var glowPen = new Pen(new ImmutableSolidColorBrush(color, 0.25), thickness + 6);
            for (int i = 0; i < points.Count - 1; i++)
                context.DrawLine(glowPen, points[i], points[i + 1]);
        }

        if (!IsVirtual)
            DrawArrowhead(context, points[^2], points[^1], arrowBrush, thickness);
    }

    private List<Point> BuildPoints()
    {
        var s = new Point(StartLeft, StartTop);
        var e = new Point(EndLeft, EndTop);

        // Golden ratio short stub on each side (1-φ ≈ 0.382 of half-dx)
        double dx = EndLeft - StartLeft;
        const double phi = 0.6180339887;
        double stub = dx / 2.0 * (1.0 - phi); // ≈ dx × 0.191

        var p1 = new Point(s.X + stub, s.Y); // end of start stub
        var p4 = new Point(e.X - stub, e.Y); // start of end stub

        // p1 → p4 is a single diagonal/vertical connector
        return [s, p1, p4, e];
    }

    private static void DrawArrowhead(DrawingContext ctx, Point from, Point tip, IBrush brush, double thickness)
    {
        var tangent = new Vector(tip.X - from.X, tip.Y - from.Y);
        if (tangent.Length < 0.001) return;
        tangent = tangent.Normalize();

        double arrowLength = 12;
        double arrowWidth = 8;
        var perp = new Vector(-tangent.Y, tangent.X);
        var basePt = new Point(tip.X - tangent.X * arrowLength, tip.Y - tangent.Y * arrowLength);
        var wing1 = new Point(basePt.X + perp.X * (arrowWidth / 2), basePt.Y + perp.Y * (arrowWidth / 2));
        var wing2 = new Point(basePt.X - perp.X * (arrowWidth / 2), basePt.Y - perp.Y * (arrowWidth / 2));

        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(tip, true);
            c.LineTo(wing1);
            c.LineTo(wing2);
        }
        ctx.DrawGeometry(brush, null, geo);
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
        var points = BuildPoints();
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (DistanceToSegment(pt, points[i], points[i + 1]) <= hitRadius)
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
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 0.0001) return new Vector(p.X - a.X, p.Y - a.Y).Length;
        double t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2;
        t = Math.Clamp(t, 0.0, 1.0);
        var proj = new Point(a.X + t * ab.X, a.Y + t * ab.Y);
        return new Vector(p.X - proj.X, p.Y - proj.Y).Length;
    }

    #endregion
}
