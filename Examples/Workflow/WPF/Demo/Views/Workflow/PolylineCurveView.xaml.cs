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

        // The brush is the view's own and the stops are only created here: AimFlowBrush aims it, gives it its
        // colours and builds the declaration whose endpoints those colours are.
        AimFlowBrush();

        // The attach/detach pair. WPF's UserControl has no virtual OnAttachedToVisualTree to override the way
        // Avalonia's Control does, so this is the event pair instead — the same one the WPF adapter itself
        // uses to hook a surface (VeloxDev.WPF, WorkflowSurfaceBehavior.Attach).
        //
        // What these two cover is the view entering and leaving the tree, not the view pool recycling it:
        // this pool hides a released view with Visibility = Collapsed and hands it a new DataContext rather
        // than detaching it, so a recycled view raises neither event. It does not need to: recycling rebinds
        // the four anchor properties, which re-aims the brush at the link it is now drawing, and the animation
        // was never stopped in the first place — it goes on walking the band along whichever link the view
        // holds. The one case that does need something is a recycled view landing on a link of another colour:
        // AimFlowBrush rebuilds the declaration there and restarts the cycle, so the band is never animated in
        // the colour of the link this view used to hold.
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

        // The gradient runs along the link's own axis and is mixed from the link's own colour, so an endpoint
        // and the colour are both inputs to the brush rather than to the drawing code.
        if (e.Property == StartLeftProperty || e.Property == StartTopProperty
            || e.Property == EndLeftProperty || e.Property == EndTopProperty
            || e.Property == LineColorProperty)
        {
            control.AimFlowBrush();
        }

        // A link becomes drawable only once both endpoints have been measured, and the flow has nothing to
        // travel along before that — while a virtual one is the rubber band under the pointer, which has no
        // settled connection to describe.
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
    /// <para>
    /// Each effect also carries the one thing this port has to add that the reference does not: the repaint.
    /// Avalonia invalidates a control from the brush it drew with, so there the stop writes are the whole story;
    /// WPF does not — the drawing a DrawingContext captured keeps its reference to the brush, but nothing is
    /// subscribed to it, so the link would sit on the frame it was last told to paint and the band would never
    /// appear to move. Measured on a control shaped like this one: with the stop writes alone OnRender is not
    /// entered again at all, with an explicit invalidate it is entered once per write. It hangs off
    /// <c>LateUpdate</c> rather than <c>Update</c> because the frame's writes have to have landed before the
    /// repaint is asked for — <c>Update</c> fires before the frame is applied, <c>LateUpdate</c> after — and it
    /// hangs off every segment, so the phase that is running is the one that asks.
    /// </para>
    /// <para>
    /// The handler reads the view's own <c>InvalidateVisual</c>, which is sound here precisely because the
    /// declaration is per view: a <c>static readonly</c> declaration would hold this view through the handler
    /// for as long as the declaration lived.
    /// </para>
    /// </remarks>
    private Transition<PolylineCurveView> BuildFlow() => Transition<PolylineCurveView>.Create()
        // Phase 1 — the band forms as it enters: it travels a third of the link while coming up from the
        // resting colour to the lit one.
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandFormed - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandFormed)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandFormed + BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Color, Lit)
        .Effect(e =>
        {
            e.Duration = EnterDuration;
            e.Ease = Eases.Default;
            e.LateUpdate += (_, _) => InvalidateVisual();
        })
        .Then()
        // Phase 2 — it travels fully lit and unchanged, which is the phase that reads as flow rather than as a
        // pulse: nothing about it changes except where it is.
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
        // Phase 3 — it leaves, settling back to the resting colour over the last third of the travel. That is
        // also what makes the seam invisible when the cycle repeats: the line is uniformly dim at both ends of
        // a cycle, so the value snapping back to its captured start cannot be seen.
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
        // The brush's mapping mode is absolute (set with the brush itself), so these two are the link's own
        // endpoints in the control's space, not fractions of a bounding box.
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

        // A view recycled onto a link of another colour gets its cycle restarted, from its own colour's
        // starting state rather than the previous link's.
        if (_running)
        {
            StartFlow();
        }
    }

    /// <summary>
    /// The band's colour: the link's own colour at full strength, lifted a little towards white so the band
    /// is the brighter end of the pair on a coloured link as well as the more opaque one.
    /// </summary>
    /// <remarks>
    /// On this demo's white links the lift has nothing left to lift — every channel is already at the top —
    /// so there the band is carried by alpha alone, which is the other reason the resting colour has to be
    /// the dim one (see <see cref="DimOf"/>). On the cyan links the other demos draw, both halves of the
    /// difference show.
    /// </remarks>
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
    /// Dimming by alpha is what keeps the hue: the alternative that suggests itself — a "highlight" that is
    /// the line colour pushed <em>towards white</em> — is what the Avalonia demo used to draw, and it is
    /// invisible there. Its links are cyan, and cyan lifted 75% towards white differs from cyan in one
    /// channel out of three, on a 2px line, against a dark canvas. Against white links it is worse still:
    /// white pushed towards white is white. Making the resting line the dim one puts the contrast where the
    /// eye can find it at a glance, and it is the half of the difference that survives on any hue.
    /// </remarks>
    private static Color DimOf(Color color) => Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    /// <summary>
    /// Starts the cycle from the sender's end. Started when the view becomes drawable and when the view is
    /// loaded into the tree, so a view that is handed a different link than the one it was built for animates
    /// that link, and a view that comes back into the tree after its cycle was exited has one running on it
    /// again.
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

        // The travelling highlight is only meaningful on a settled connection. A virtual link is the rubber
        // band under the pointer and a highlighted one is already the thing the eye is on, so both keep a
        // flat pen; every other link is drawn with the gradient the flow animation writes into.
        var flat = new SolidColorBrush(color);
        Brush brush = IsHighlighted || IsVirtual ? flat : FlowBrush;

        // The arrowhead is the destination marker, so it carries the band's colour rather than the gradient:
        // the line rests dim, and an arrowhead dimmed with it would be the one part of the link that never
        // lights up.
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
