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

        _flow.Brush = new LinearGradientBrush
        {
            SpreadMethod = GradientSpreadMethod.Pad,
        };
        _flow.Brush.GradientStops.Add(new GradientStop(LineColor, 0d));
        _flow.Brush.GradientStops.Add(new GradientStop(LineColor, 0.5d));
        _flow.Brush.GradientStops.Add(new GradientStop(LineColor, 1d));
        UpdateFlowBrush();

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

    /// <summary>
    /// The object the flow animation writes into. <c>Transition&lt;T&gt;</c> animates a member of a
    /// reference type, so the brush is held here rather than reached through the control: the animated
    /// path is <c>Brush.GradientStops[1].Offset</c>, and that index is what makes the middle stop — and
    /// only the middle stop — move.
    /// </summary>
    private sealed class LinkFlow
    {
        public LinearGradientBrush Brush { get; set; } = null!;
    }

    private readonly LinkFlow _flow = new();

    /// <summary>
    /// Walks the middle gradient stop from 0 to 1, forever, so a brighter band travels from the sender's
    /// anchor to the receiver's and the link reads as carrying data one way.
    /// <para>
    /// Declared once and executed per view rather than built per call: the endpoint is the same every
    /// cycle, which is the case the animation reference puts in a <c>static readonly</c> field. A straight
    /// line rather than an eased curve, because the band should move at a constant speed — an ease would
    /// make each cycle pause at the ends and read as a series of pulses instead of a flow.
    /// </para>
    /// </summary>
    private static readonly Transition<LinkFlow> Flow =
        Transition<LinkFlow>.Create()
            .Property(t => t.Brush.GradientStops[1].Offset, 1d)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(1.6),
                LoopTime = int.MaxValue,
                Ease = Eases.Default,
            });

    /// <summary>
    /// Orients the gradient along the link and keeps its end stops on the link colour. The middle stop is
    /// left to the animation — writing it here would fight the running transition every frame.
    /// </summary>
    private void UpdateFlowBrush()
    {
        var brush = _flow.Brush;
        if (brush is null) return;

        // Absolute coordinates: the four points are drawn in the control's own space and the link runs
        // diagonally, so a relative gradient would sweep across the bounding box instead of along the line.
        brush.StartPoint = new RelativePoint(StartLeft, StartTop, RelativeUnit.Absolute);
        brush.EndPoint = new RelativePoint(EndLeft, EndTop, RelativeUnit.Absolute);

        var stops = brush.GradientStops;
        stops[0].Color = LineColor;
        stops[2].Color = LineColor;
        stops[1].Color = Lifted(LineColor);
    }

    /// <summary>The travelling band's colour: the link colour pushed towards white, so it reads as the same line.</summary>
    private static Color Lifted(Color color) => Color.FromArgb(
        color.A,
        (byte)(color.R + (255 - color.R) * 0.75),
        (byte)(color.G + (255 - color.G) * 0.75),
        (byte)(color.B + (255 - color.B) * 0.75));

    /// <summary>
    /// Starts the flow from the sender's end. Started on attach so a pooled view that is handed a
    /// different link animates that link rather than the one it was built for.
    /// </summary>
    private void StartFlow()
    {
        if (IsVirtual || !CanRender) return;

        // The transition reads its start value from the target, so the band has to be at 0 before Execute.
        _flow.Brush.GradientStops[1].Offset = 0d;
        Flow.Execute(_flow);
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
        Transition.Exit(_flow, IncludeMutual: true, IncludeNoMutual: true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StartLeftProperty || change.Property == StartTopProperty
            || change.Property == EndLeftProperty || change.Property == EndTopProperty
            || change.Property == LineColorProperty)
        {
            UpdateFlowBrush();
        }

        // A link becomes drawable only once both endpoints have been measured, and the flow has nothing to
        // travel along before that.
        if (change.Property == CanRenderProperty || change.Property == IsVirtualProperty)
            StartFlow();
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
        IBrush brush = IsSelected || IsVirtual ? GetSolid() : _flow.Brush!;

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
            DrawArrowhead(context, points[^2], points[^1], brush, thickness);
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
