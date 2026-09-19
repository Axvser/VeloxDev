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
    /// reference type, so the brush and the two colours the band is mixed from are held here rather than
    /// reached through the control: the animated path is <see cref="Phase"/>, and its setter is what turns
    /// that one number into the band's position and colour.
    /// </summary>
    /// <remarks>
    /// The line is drawn dim and the band is the same colour at full strength, so what travels is a lit
    /// length of the link rather than a different colour on it. The three stops are the band: the middle
    /// one carries the lit colour and the other two sit <see cref="HalfWidth"/> either side of it, which is
    /// what keeps it a band instead of one wide smear along the whole line.
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

            // Addressed in place rather than rebuilt: the brush is the property the control renders from, so
            // writing its stops is what makes the framework repaint the link.
            var stops = Brush.GradientStops;
            stops[0].Offset = centre - HalfWidth;
            stops[1].Offset = centre;
            stops[2].Offset = centre + HalfWidth;
            stops[1].Color = Blend(Dim, Lit, mix);
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
    /// the whole run shares — so <c>LoopTime = int.MaxValue</c> on a first segment never reaches the
    /// second, and there is no way to express "these three, in order, forever" as a chain today.
    /// </para>
    /// </summary>
    private static readonly Transition<LinkFlow> Flow =
        Transition<LinkFlow>.Create()
            .Property(t => t.Phase, 1d)
            .Effect(new TransitionEffect()
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

        // Absolute coordinates: the four points are drawn in the control's own space and the link runs
        // diagonally, so a relative gradient would sweep across the bounding box instead of along the line.
        brush.StartPoint = new RelativePoint(StartLeft, StartTop, RelativeUnit.Absolute);
        brush.EndPoint = new RelativePoint(EndLeft, EndTop, RelativeUnit.Absolute);

        _flow.Lit = LitOf(LineColor);
        _flow.Dim = DimOf(_flow.Lit);

        var stops = brush.GradientStops;
        stops[0].Color = _flow.Dim;
        stops[2].Color = _flow.Dim;
        _flow.Phase = 0d;
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
    /// Dimming by alpha is what keeps the hue: the alternative that suggests itself — a "highlight" that is
    /// the line colour pushed <em>towards white</em> — is what this demo had, and it is invisible. Its links
    /// are cyan, and cyan lifted 75% towards white differs from cyan in one channel out of three, on a 2px
    /// line, against a dark canvas. Making the resting line the dim one puts the contrast where the eye can
    /// find it at a glance, and it works the same on the white links the other demos draw.
    /// </remarks>
    private static Color DimOf(Color color) => Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    /// <summary>
    /// Starts the flow from the sender's end. Started on attach so a pooled view that is handed a
    /// different link animates that link rather than the one it was built for.
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

        // The arrowhead is the destination marker, so it carries the band's colour rather than the
        // gradient: the line rests dim, and an arrowhead dimmed with it would be the one part of the link
        // that never lights up.
        IBrush arrowBrush = IsSelected ? GetSolid() : new ImmutableSolidColorBrush(_flow.Lit);

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
