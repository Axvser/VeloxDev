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

        // The two halves are wired together here: the flow holds the brush the link is drawn with and, on
        // WPF, the control it has to ask for a re-render (see LinkFlow.Apply for why that is needed).
        _flow.Owner = this;
        _flow.Brush = new LinearGradientBrush
        {
            SpreadMethod = GradientSpreadMethod.Pad,

            // Absolute coordinates rather than WPF's default RelativeToBoundingBox: the four points the link
            // is drawn from are the control's own space and the link runs diagonally, so a relative gradient
            // would sweep across the bounding box instead of along the line. The two points themselves are
            // set in UpdateFlowBrush, from the link's own endpoints.
            MappingMode = BrushMappingMode.Absolute,
        };
        _flow.Brush.GradientStops.Add(new GradientStop(LineColor, 0d));
        _flow.Brush.GradientStops.Add(new GradientStop(LineColor, 0.5d));
        _flow.Brush.GradientStops.Add(new GradientStop(LineColor, 1d));
        UpdateFlowBrush();

        // The attach/detach pair. WPF's UserControl has no virtual OnAttachedToVisualTree to override the way
        // Avalonia's Control does, so this is the event pair instead — the same one the WPF adapter itself
        // uses to hook a surface (VeloxDev.WPF, WorkflowSurfaceBehavior.Attach).
        //
        // What these two cover is the view entering and leaving the tree, not the view pool recycling it:
        // this pool hides a released view with Visibility = Collapsed and hands it a new DataContext rather
        // than detaching it, so a recycled view raises neither event. It does not need to: recycling rebinds
        // the four anchor properties, which re-aims the brush at its new link and puts the cycle back at its
        // start, and the animation was never stopped in the first place — it just goes on walking the band
        // along whichever link the view is now drawing (see UpdateFlowBrush).
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
            control.UpdateFlowBrush();
        }

        // A link becomes drawable only once both endpoints have been measured, and the flow has nothing to
        // travel along before that.
        if (e.Property == CanRenderProperty || e.Property == IsVirtualProperty)
            control.StartFlow();
    }

    private void UpdateInteractivity()
    {
        IsHitTestVisible = !IsVirtual;
        Focusable = !IsVirtual;
    }

    #endregion

    #region Flow effect

    /// <summary>
    /// The object the flow animation writes into. <c>Transition&lt;T&gt;</c> animates a member of a
    /// reference type, so the brush and the two colours the band is mixed from are held here rather than
    /// reached through the control: the animated path is <see cref="Phase"/>, and its setter is what turns
    /// that one number into the band's position and colour.
    /// </summary>
    /// <remarks>
    /// The line is drawn dim and the band is the same colour at full strength, so what travels is a lit
    /// length of the link rather than a different colour on it. The three stops are the band: the middle
    /// one carries the lit colour and the other two sit <see cref="HalfWidth"/> either side of it, which is
    /// what keeps it a band instead of one wide smear along the whole line.
    /// <para>
    /// One thing is held here that is not animated state: <see cref="Owner"/>. On WPF, writing a brush's
    /// stops does not by itself make the control that drew with it repaint, and on a view whose whole job
    /// is to repaint at the frame rate that has to be asked for explicitly — see <see cref="Apply"/>.
    /// </para>
    /// </remarks>
    private sealed class LinkFlow
    {
        /// <summary>Half the band's width, in gradient-offset units.</summary>
        private const double HalfWidth = 0.04;

        // One cycle, as fractions of it. The phases have different lengths because they cover different
        // distances: the band travels a third of the link while forming, a third while fully lit, and a
        // third while leaving.
        private const double EnterEnd = 0.30;
        private const double FadeStart = 0.66;
        private const double BandFrom = 0.06;
        private const double BandFormed = 0.34;
        private const double BandLeaving = 0.66;
        private const double BandTo = 0.94;

        public LinearGradientBrush Brush { get; set; } = null!;

        /// <summary>
        /// The view this flow belongs to, which is what gets asked to re-render when the band moves.
        /// </summary>
        public PolylineCurveView? Owner { get; set; }

        /// <summary>The line's resting colour: <see cref="Lit"/> at the link's own strength dimmed.</summary>
        public Color Dim { get; set; }

        /// <summary>The band's colour, and the arrowhead's: the link's colour at full strength.</summary>
        public Color Lit { get; set; }

        private double _phase;

        /// <summary>
        /// Cycle progress, 0→1: the whole of the animated state. Writing it repaints the band, and the
        /// animation writes it every frame.
        /// </summary>
        public double Phase
        {
            get => _phase;
            set { _phase = value; Apply(); }
        }

        /// <summary>
        /// Places the band and mixes its colour for the current phase — the three phases the cycle is made
        /// of, as one piecewise mapping.
        /// <para>
        /// Phase 1 (0 → <see cref="EnterEnd"/>) the band forms as it enters: it travels a third of the way
        /// while coming up from the line's resting colour to the lit one. Phase 2 (<see cref="EnterEnd"/> →
        /// <see cref="FadeStart"/>) it travels fully lit and unchanged, which is the phase that reads as
        /// flow rather than as a pulse. Phase 3 (<see cref="FadeStart"/> → 1) it leaves: the last third of
        /// the travel, settling back to the resting colour — which is also what makes the seam invisible
        /// when the cycle repeats, since the line is uniformly dim at both ends of a cycle.
        /// </para>
        /// </summary>
        private void Apply()
        {
            double centre;
            double mix;
            if (_phase < EnterEnd)
            {
                var t = _phase / EnterEnd;
                centre = BandFrom + (BandFormed - BandFrom) * t;
                mix = t;
            }
            else if (_phase < FadeStart)
            {
                var t = (_phase - EnterEnd) / (FadeStart - EnterEnd);
                centre = BandFormed + (BandLeaving - BandFormed) * t;
                mix = 1d;
            }
            else
            {
                var t = (_phase - FadeStart) / (1d - FadeStart);
                centre = BandLeaving + (BandTo - BandLeaving) * t;
                mix = 1d - t;
            }

            // Addressed in place rather than rebuilt: the pen the control draws the link with already holds
            // this brush instance, so writing its stops is what changes the link — a brush rebuilt here would
            // be a new object nothing is drawing from, and the last frame would stay on screen as it was.
            var stops = Brush.GradientStops;
            stops[0].Offset = centre - HalfWidth;
            stops[1].Offset = centre;
            stops[2].Offset = centre + HalfWidth;
            stops[1].Color = Blend(Dim, Lit, mix);

            // The one place this port has to do something the reference does not. Avalonia invalidates a
            // control from the brush it drew with, so the writes above are the whole story there; WPF does
            // not — the drawing a DrawingContext captured keeps its reference to the brush, but nothing is
            // subscribed to it, so the link would sit on the frame it was last told to paint and the band
            // would never appear to move. Measured on a control shaped like this one: with the stop writes
            // alone OnRender is not entered again at all, with this call it is entered once per write.
            Owner?.InvalidateVisual();
        }

        private static Color Blend(Color from, Color to, double t) => Color.FromArgb(
            (byte)Math.Round(from.A + (to.A - from.A) * t),
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    private readonly LinkFlow _flow = new();

    /// <summary>
    /// Walks the band across the link once per cycle, forever, so the link reads as carrying data from the
    /// sender's anchor to the receiver's. <see cref="LinkFlow.Phase"/> is the only animated value; its
    /// setter paints the three phases.
    /// <para>
    /// Declared once and executed per view rather than built per call: the endpoint is the same every
    /// cycle, which is the case the animation reference puts in a <c>static readonly</c> field. A straight
    /// line rather than an eased curve, because the band should move at a constant speed — an ease would
    /// make each cycle pause at the ends and read as a series of pulses instead of a flow.
    /// </para>
    /// <para>
    /// The phases are one looping segment and a piecewise mapping rather than three segments joined with
    /// <c>Then()</c>, because nothing in the engine repeats a chain: a segment's <c>LoopTime</c> repeats
    /// that segment, the queue of segments is walked exactly once, and the loop guard reads a pass counter
    /// the whole run shares (<c>Src/Core/VeloxDev.Core/TransitionSystem/TransitionInterpreter.cs:194</c> —
    /// the WPF adapter's interpreter is that core loop plus a <c>DispatcherTimer</c> pacer) — so
    /// <c>LoopTime = int.MaxValue</c> on a first segment never reaches the second, and there is no way to
    /// express "these three, in order, forever" as a chain today.
    /// </para>
    /// </summary>
    private static readonly Transition<LinkFlow> Flow =
        Transition<LinkFlow>.Create()
            .Property(t => t.Phase, 1d)
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromSeconds(1.8),
                LoopTime = int.MaxValue,
                Ease = Eases.Default,
            });

    /// <summary>
    /// Orients the gradient along the link and gives the flow its two colours. <see cref="LinkFlow.Phase"/>
    /// is written back to 0 for the same reason: it is what paints the middle stop, and at phase 0 that is
    /// the link's colour at the band's starting position — the state a cycle begins and ends in.
    /// </summary>
    private void UpdateFlowBrush()
    {
        var brush = _flow.Brush;
        if (brush is null) return;

        // The brush's mapping mode is absolute (set with the brush itself), so these two are the link's own
        // endpoints in the control's space, not fractions of a bounding box.
        brush.StartPoint = new Point(StartLeft, StartTop);
        brush.EndPoint = new Point(EndLeft, EndTop);

        _flow.Lit = LitOf(LineColor);
        _flow.Dim = DimOf(_flow.Lit);

        var stops = brush.GradientStops;
        stops[0].Color = _flow.Dim;
        stops[2].Color = _flow.Dim;
        _flow.Phase = 0d;
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
    /// Starts the flow from the sender's end. Started whenever the view becomes drawable, so a view that is
    /// handed a different link than the one it was built for animates that link, and a view that comes back
    /// into the tree after its cycle was exited has one running on it again.
    /// </summary>
    private void StartFlow()
    {
        if (IsVirtual || !CanRender) return;

        // The transition reads its start value from the target, so the cycle has to be at its beginning
        // before Execute. The loop replays that captured start at every seam, so this is also the value
        // each later cycle begins from.
        _flow.Phase = 0d;
        Flow.Execute(_flow);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => StartFlow();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // A pooled view released and reused for another link must not leave the old animation running on it.
        Transition.Exit(_flow, IncludeMutual: true, IncludeNoMutual: true);
    }

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
        Brush brush = IsHighlighted || IsVirtual ? flat : _flow.Brush;

        // The arrowhead is the destination marker, so it carries the band's colour rather than the gradient:
        // the line rests dim, and an arrowhead dimmed with it would be the one part of the link that never
        // lights up.
        Brush arrowBrush = IsHighlighted ? flat : new SolidColorBrush(_flow.Lit);

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
