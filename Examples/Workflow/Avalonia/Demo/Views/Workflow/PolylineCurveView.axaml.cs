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

        // The brush is the view's own and the stops are only created here: AimFlowBrush aims it, gives it its
        // colours and builds the declaration whose endpoints those colours are.
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

    /// <summary>Half the band's width, in gradient-offset units.</summary>
    private const double BandHalfWidth = 0.04;

    // The three phases, as the band's centre at the end of each: it forms as it enters, travels fully lit,
    // and settles back on its way out. What the animation writes is these centres, plus and minus HalfWidth.
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

    /// <summary>The band's colour, and the arrowhead's: the link's colour at full strength.</summary>
    private Color Lit { get; set; }

    /// <summary>The line's resting colour: the lit colour dimmed to a little under two thirds.</summary>
    private Color Dim { get; set; }

    private Transition<PolylineCurveView>? _flow;
    private bool _running;

    /// <summary>
    /// The flow, as the three phases it is made of, declared one after the other and repeated forever.
    /// </summary>
    /// <remarks>
    /// Built per view rather than held in a <c>static readonly</c> field, because two of its endpoints are the
    /// link's own colours, and a declaration that reads a local is shared by every later execution of it — here
    /// that would paint one link's band in another link's colour.
    /// <para>
    /// The paths go into the brush itself: <c>GradientStops[1]</c> is the band and the two stops either side of
    /// it are its shoulders, so a phase is a handful of indexed writes and the phase structure is readable
    /// rather than computed. A straight line rather than an eased curve, because the band should move at a
    /// constant speed — an ease would make each cycle pause at the ends and read as pulses instead of flow.
    /// </para>
    /// </remarks>
    private Transition<PolylineCurveView> BuildFlow() => Transition<PolylineCurveView>.Create()
        // Phase 1 — the band forms as it enters: it travels a third of the link while coming up from the
        // resting colour to the lit one.
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
        // Phase 2 — it travels fully lit and unchanged, which is the phase that reads as flow rather than as a
        // pulse: nothing about it changes except where it is.
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandLeaving - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandLeaving)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandLeaving + BandHalfWidth)
        .Effect(new TransitionEffect()
        {
            Duration = TravelDuration,
            Ease = Eases.Default,
        })
        .Then()
        // Phase 3 — it leaves, settling back to the resting colour over the last third of the travel. That is
        // also what makes the seam invisible when the cycle repeats: the line is uniformly dim at both ends of
        // a cycle, so the value snapping back to its captured start cannot be seen.
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

    /// <summary>
    /// Aims the brush along the link and gives it its two colours. Called whenever the link moves — its anchors
    /// change on every frame of a zoom (the Core anchor getters collapse the nodes toward the origin) and of a
    /// node drag — and when its colour changes, which is also when the declaration is rebuilt, since the two
    /// colours are its endpoints.
    /// </summary>
    /// <remarks>
    /// Nothing here writes the band's position. The cycle owns those stops and writes them every frame from the
    /// endpoints it captured, so re-seating them from a path that runs during a gesture would fight it for a
    /// frame — which reads as a band that stutters while the canvas moves.
    /// </remarks>
    private void AimFlowBrush()
    {
        // Absolute coordinates: the four points are drawn in the control's own space and the link runs
        // diagonally, so a relative gradient would sweep across the bounding box instead of along the line.
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

        // A view recycled onto a link of another colour gets its cycle restarted, from its own colour's
        // starting state rather than the previous link's.
        if (_running)
        {
            StartFlow();
        }
    }

    /// <summary>
    /// The band's colour: the link's own colour at full strength, lifted a little so a link that is already
    /// white still has somewhere brighter to go.
    /// </summary>
    private static Color LitOf(Color color)
    {
        const double lift = 0.45;

        byte Up(byte channel) => (byte)Math.Round(channel + (255 - channel) * lift);

        return Color.FromArgb(255, Up(color.R), Up(color.G), Up(color.B));
    }

    /// <summary>
    /// The line's resting colour: the lit colour dimmed to a little under two thirds, which is what makes a
    /// lit band read as a band.
    /// </summary>
    /// <remarks>
    /// Dimming by alpha is what keeps the hue: the alternative that suggests itself — a "highlight" that is the
    /// line colour pushed <em>towards white</em> — is what this demo had, and it is invisible. Its links are
    /// cyan, and cyan lifted 75% towards white differs from cyan in one channel out of three, on a 2px line,
    /// against a dark canvas. Making the resting line the dim one puts the contrast where the eye can find it at
    /// a glance, and it works the same on the white links the other demos draw.
    /// </remarks>
    private static Color DimOf(Color color) => Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    /// <summary>
    /// Starts the cycle from the sender's end. Started on attach so a pooled view that is handed a different
    /// link animates that link rather than the one it was built for.
    /// </summary>
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

    /// <summary>
    /// Stops the cycle: a pooled view released and reused for another link must not leave the old animation
    /// running on it.
    /// </summary>
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
