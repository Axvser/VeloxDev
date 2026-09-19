using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs.
/// Hover to highlight, Delete to remove, and carries a travelling highlight so the direction of data flow is
/// readable at a glance.
/// </summary>
public sealed partial class PolylineCurveView : UserControl
{
    private static readonly DoubleCollection VirtualStrokeDashArray = [4, 2];

    private readonly Path _path;
    private readonly Path _arrowPath;
    private readonly SolidColorBrush _strokeBrush = new(Colors.Cyan);
    // The arrowhead's brush, kept apart from the line's because it is not the same colour: it takes the flow's
    // lit colour rather than the gradient the line strokes with (see UpdatePath). Recoloured in place by
    // UpdateFlowBrush; the seed value is only what it wears until the link's own colour is known.
    private readonly SolidColorBrush _arrowBrush = new(Colors.Cyan);
    private readonly PathGeometry _pathGeometry = new();
    private readonly PathFigure _pathFigure = new() { IsClosed = false };
    private readonly PathGeometry _arrowGeometry = new();
    private readonly PathFigure _arrowFigure = new() { IsClosed = true };
    private readonly LineSegment _arrowLeftSegment = new();
    private readonly LineSegment _arrowRightSegment = new();
    private readonly Point[] _points = new Point[4];
    private bool _updatePending;
    private bool _isLoaded;

    public PolylineCurveView()
    {
        InitializeComponent();
        Canvas.SetZIndex(this, -100);

        // The polyline/arrow geometry is in raw collapsed (canvas-local) coordinates, so at deep zoom
        // its negative top/left half extends beyond this element's bounds. WinUI clips element content
        // to its bounds unless Clip is nulled (the root/grid pattern the sibling NodeView uses); WPF
        // links are OnRender-drawn and never clipped. Null the whole chain so the retained Paths draw
        // their negative-coordinate geometry the same way WPF does.
        var container = new Grid { Clip = null };
        _path = new Path { Stroke = _strokeBrush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false, Clip = null };
        _arrowPath = new Path { Fill = _arrowBrush, IsHitTestVisible = false, Clip = null };
        container.Children.Add(_path);
        container.Children.Add(_arrowPath);
        this.Content = container;

        // The band's brush: three stops, the middle one carrying the travelling highlight (see LinkFlow.Apply).
        // Built here and handed to the flow rather than declared inside the LinkFlow type so that the type stays
        // a plain holder of the members the animation writes. Seeded from the link's colour and then immediately
        // put in the resting state by UpdateFlowBrush — the same order the reference platform uses.
        _flow.Brush = new LinearGradientBrush
        {
            SpreadMethod = GradientSpreadMethod.Pad,
            GradientStops =
            [
                new GradientStop { Color = LineColor, Offset = 0d },
                new GradientStop { Color = LineColor, Offset = 0.5d },
                new GradientStop { Color = LineColor, Offset = 1d },
            ],
        };
        UpdateFlowBrush();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PointerEntered += (_, _) => { IsHighlighted = true; Focus(FocusState.Pointer); };
        PointerExited += (_, _) => IsHighlighted = false;
        PointerMoved += OnHoverPointerMoved;
        UpdateInteractivity();
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(true, OnChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Windows.UI.Color), typeof(PolylineCurveView), new PropertyMetadata(Colors.Cyan, OnChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Windows.UI.Color LineColor { get => (Windows.UI.Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PolylineCurveView)d;
        control.UpdateInteractivity();
        control.ScheduleUpdate();

        // Only the four endpoints and the colour the band is mixed from re-orient the gradient, and
        // re-orienting also puts the cycle back at its beginning (see UpdateFlowBrush). Every other change
        // here — a hover, a link turning virtual — asks for a repaint and nothing more, because a band that
        // snapped back to the sender's anchor whenever the pointer crossed the link would read as a stutter
        // rather than as flow.
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
        IsTabStop = !IsVirtual;
    }

    #endregion

    #region Flow effect

    /// <summary>
    /// The object the flow animation writes into. <c>Transition&lt;T&gt;</c> animates a member of a reference
    /// type, so the brush and the two colours the band is mixed from are held here rather than reached through
    /// the control: the animated path is <see cref="Phase"/>, and its setter is what turns that one number into
    /// the band's position and colour.
    /// </summary>
    /// <remarks>
    /// The line is drawn dim and the band is the same colour at full strength, so what travels is a lit length
    /// of the link rather than a different colour on it. The three stops are the band: the middle one carries
    /// the lit colour and the other two sit <see cref="HalfWidth"/> either side of it, which is what keeps it a
    /// band instead of one wide smear along the whole line.
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
        public Windows.UI.Color Dim { get; set; }

        /// <summary>The band's colour, and the arrowhead's: the link's colour at full strength.</summary>
        public Windows.UI.Color Lit { get; set; }

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
        /// Places the band and mixes its colour for the current phase — the three phases the cycle is made of,
        /// as one piecewise mapping.
        /// <para>
        /// Phase 1 (0 → <see cref="EnterEnd"/>) the band forms as it enters: it travels a third of the way while
        /// coming up from the line's resting colour to the lit one. Phase 2 (<see cref="EnterEnd"/> →
        /// <see cref="FadeStart"/>) it travels fully lit and unchanged, which is the phase that reads as flow
        /// rather than as a pulse. Phase 3 (<see cref="FadeStart"/> → 1) it leaves: the last third of the
        /// travel, settling back to the resting colour — which is also what makes the seam invisible when the
        /// cycle repeats, since the line is uniformly dim at both ends of a cycle.
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

            // Addressed in place rather than rebuilt: the brush is what the Path strokes with, so writing its
            // stops is what makes the framework repaint the link, while a rebuilt brush would be an object the
            // Path has never been handed and nothing on screen would change.
            var stops = Brush.GradientStops;
            stops[0].Offset = centre - HalfWidth;
            stops[1].Offset = centre;
            stops[2].Offset = centre + HalfWidth;
            stops[1].Color = Blend(Dim, Lit, mix);
        }

        private static Windows.UI.Color Blend(Windows.UI.Color from, Windows.UI.Color to, double t)
            => Windows.UI.Color.FromArgb(
                (byte)Math.Round(from.A + (to.A - from.A) * t),
                (byte)Math.Round(from.R + (to.R - from.R) * t),
                (byte)Math.Round(from.G + (to.G - from.G) * t),
                (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    private readonly LinkFlow _flow = new();

    /// <summary>
    /// Walks the band across the link once per cycle, forever, so the link reads as carrying data from the
    /// sender's anchor to the receiver's. <see cref="LinkFlow.Phase"/> is the only animated value; its setter
    /// paints the three phases.
    /// <para>
    /// A straight line rather than an eased curve, because the band should move at a constant speed — an ease
    /// would make each cycle pause at the ends and read as a series of pulses instead of a flow.
    /// </para>
    /// <para>
    /// The phases are one looping segment and a piecewise mapping rather than three segments joined with
    /// <c>Then()</c>, because nothing in the engine repeats a chain: a segment's <c>LoopTime</c> repeats that
    /// segment, the queue of segments is walked exactly once, and the loop guard reads a pass counter the whole
    /// run shares (<c>Src/Core/VeloxDev.Core/TransitionSystem/TransitionInterpreter.cs:194</c>) — so
    /// <c>LoopTime = int.MaxValue</c> on a first segment never reaches the second, and there is no way to
    /// express "these three, in order, forever" as a chain today.
    /// </para>
    /// <para>
    /// Held per view rather than in the <c>static readonly</c> field the other platforms declare their
    /// animations in: on WinUI a <c>Transition&lt;T&gt;</c> field is built on whichever thread first touches the
    /// declaring type, and that is a documented hazard in this repository's WinUI demos (see
    /// <c>Examples/Transition/WinUI/Demo/MainWindow.xaml.cs</c>). An instance field is built in the view's own
    /// constructor, which the framework only ever runs on the UI thread, so the hazard cannot arise — at the
    /// cost of one declaration per view instead of one per process.
    /// </para>
    /// </summary>
    private readonly Transition<LinkFlow> _flowAnimation =
        Transition<LinkFlow>.Create()
            .Property(t => t.Phase, 1d)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(1.8),
                LoopTime = int.MaxValue,
                Ease = Eases.Default,
            });

    /// <summary>
    /// Orients the gradient along the link and gives the flow its two colours. <see cref="LinkFlow.Phase"/> is
    /// written back to 0 for the same reason: it is what paints the middle stop, and at phase 0 that is the
    /// link's colour at the band's starting position — the state a cycle begins and ends in.
    /// </summary>
    /// <remarks>
    /// WinUI's <see cref="LinearGradientBrush"/> has no <c>MappingMode</c>: its axis is always expressed in the
    /// own 0..1 space of the geometry the shape paints. This Path's geometry is the whole polyline, in the
    /// control's own coordinates, so each endpoint has to be converted into a position inside the geometry's
    /// bounds — a gradient declared as (0,0)→(1,1) would sweep along the bounding box's diagonal rather than
    /// along the link, and on a link whose endpoints run up and to the left it would run backwards as well.
    /// </remarks>
    private void UpdateFlowBrush()
    {
        var brush = _flow.Brush;
        if (brush is null) return;

        // Read from the control's own endpoints rather than from the geometry: this runs on the property change
        // that moved an endpoint, which is before the deferred UpdatePath has rebuilt anything.
        BuildPoints();

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var point in _points)
        {
            if (point.X < minX) minX = point.X;
            if (point.X > maxX) maxX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.Y > maxY) maxY = point.Y;
        }

        // A link can be exactly axis-aligned, which leaves one of the two extents at zero — and dividing by it
        // would give the endpoint a NaN. A divisor of 1 keeps that axis' two endpoints equal, which is the flat
        // gradient an axis-aligned link wants: the band then travels along the axis that does have extent.
        var width = maxX > minX ? maxX - minX : 1d;
        var height = maxY > minY ? maxY - minY : 1d;

        brush.StartPoint = new Point((StartLeft - minX) / width, (StartTop - minY) / height);
        brush.EndPoint = new Point((EndLeft - minX) / width, (EndTop - minY) / height);

        _flow.Lit = LitOf(LineColor);
        _flow.Dim = DimOf(_flow.Lit);
        _arrowBrush.Color = _flow.Lit;

        var stops = brush.GradientStops;
        stops[0].Color = _flow.Dim;
        stops[2].Color = _flow.Dim;
        _flow.Phase = 0d;
    }

    /// <summary>
    /// The band's colour: the link's own colour at full strength, lifted a little so a link that is already
    /// white still has somewhere brighter to go.
    /// </summary>
    private static Windows.UI.Color LitOf(Windows.UI.Color color)
    {
        const double lift = 0.45;

        byte Up(byte channel) => (byte)Math.Round(channel + (255 - channel) * lift);

        return Windows.UI.Color.FromArgb(255, Up(color.R), Up(color.G), Up(color.B));
    }

    /// <summary>
    /// The line's resting colour: the lit colour dimmed to a little under two thirds, which is what makes a lit
    /// band read as a band.
    /// </summary>
    /// <remarks>
    /// Dimming by alpha is what keeps the hue: the alternative that suggests itself — a "highlight" that is the
    /// line colour pushed <em>towards white</em> — is what this demo had, and it is invisible. Its links are
    /// white, and white lifted 75% towards white does not differ from white at all, on a 2px line, against a
    /// dark canvas. Making the resting line the dim one puts the contrast where the eye can find it at a
    /// glance, and it works the same on the cyan links the other demos draw.
    /// </remarks>
    private static Windows.UI.Color DimOf(Windows.UI.Color color) => Windows.UI.Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    /// <summary>
    /// Starts the flow from the sender's end. Started on load so a pooled view that is handed a different link
    /// animates that link rather than the one it was built for.
    /// </summary>
    private void StartFlow()
    {
        if (IsVirtual || !CanRender) return;

        // Loaded is the only place a view is known to be in the tree, and WinUI can raise a property change
        // while it is not — a pooled view being prepared for its next link, for instance. A flow started there
        // would animate a link nobody is looking at with no unload left to stop it, so it is skipped; Loaded
        // starts one as soon as the view is really on screen.
        if (!_isLoaded) return;

        // The transition reads its start value from the target, so the cycle has to be at its beginning before
        // Execute. The loop replays that captured start at every seam, so this is also the value each later
        // cycle begins from.
        _flow.Phase = 0d;
        _flowAnimation.Execute(_flow);
    }

    #endregion

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        EnsureGeometry();
        ScheduleUpdate();

        // Attaching is where the flow belongs: the view pool hands a released view a different link, so the
        // band has to be started against the link this view is attached for, not the one it was built for.
        StartFlow();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _updatePending = false;

        // Detaching is where it stops, for the same reason: a pooled view released and reused for another link
        // must not leave the old animation running on it, driving the brush of whatever it is reused for.
        Transition.Exit(_flow, IncludeMutual: true, IncludeNoMutual: true);
    }

    private void EnsureGeometry()
    {
        while (_pathFigure.Segments.Count < _points.Length - 1)
        {
            _pathFigure.Segments.Add(new LineSegment());
        }

        if (_pathGeometry.Figures.Count == 0)
        {
            _pathGeometry.Figures.Add(_pathFigure);
        }

        if (_arrowFigure.Segments.Count == 0)
        {
            _arrowFigure.Segments.Add(_arrowLeftSegment);
            _arrowFigure.Segments.Add(_arrowRightSegment);
            _arrowGeometry.Figures.Add(_arrowFigure);
        }
    }

    private void ScheduleUpdate()
    {
        if (!_isLoaded)
        {
            return;
        }

        if (_updatePending)
        {
            return;
        }

        _updatePending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            _updatePending = false;
            if (_isLoaded)
            {
                UpdatePath();
            }
        });
    }

    private void UpdatePath()
    {
        EnsureGeometry();

        if (!CanRender)
        {
            _path.Data = null;
            _arrowPath.Data = null;
            return;
        }

        BuildPoints();
        var color = IsHighlighted ? Microsoft.UI.Colors.OrangeRed : LineColor;
        var thickness = IsHighlighted ? 3.5 : 2.0;
        _strokeBrush.Color = color;
        _path.StrokeThickness = thickness;

        // The travelling highlight is only meaningful on a settled connection: a virtual link is the rubber band
        // under the pointer and a highlighted one is already lit, so both keep a flat pen. A settled link strokes
        // with the gradient, whose two outer stops are the resting colour — so what a settled link rests in is
        // the link's own colour dimmed, which is what gives the lit band something to read against.
        _path.Stroke = IsHighlighted || IsVirtual ? _strokeBrush : _flow.Brush;

        // The arrowhead is the destination marker, so it carries the band's colour rather than the gradient: the
        // line rests dim, and an arrowhead dimmed with it would be the one part of the link that never lights up.
        _arrowPath.Fill = IsHighlighted ? _strokeBrush : _arrowBrush;

        if (IsVirtual)
            _path.StrokeDashArray = VirtualStrokeDashArray;
        else
            _path.StrokeDashArray = null;

        _pathFigure.StartPoint = _points[0];
        for (int i = 1; i < _points.Length; i++)
        {
            ((LineSegment)_pathFigure.Segments[i - 1]).Point = _points[i];
        }

        _path.Data = _pathGeometry;

        // Arrowhead
        if (!IsVirtual)
        {
            var from = _points[^2];
            var tip = _points[^1];
            double tx = tip.X - from.X, ty = tip.Y - from.Y;
            double len = Math.Sqrt(tx * tx + ty * ty);
            if (len <= 0.001)
            {
                _arrowPath.Data = null;
                return;
            }

            tx /= len;
            ty /= len;
            double nx = -ty, ny = tx;
            double al = 12, aw = 8;
            var baseP = new Point(tip.X - tx * al, tip.Y - ty * al);
            _arrowFigure.StartPoint = tip;
            _arrowLeftSegment.Point = new Point(baseP.X + nx * (aw / 2), baseP.Y + ny * (aw / 2));
            _arrowRightSegment.Point = new Point(baseP.X - nx * (aw / 2), baseP.Y - ny * (aw / 2));
            _arrowPath.Data = _arrowGeometry;
        }
        else
        {
            _arrowPath.Data = null;
        }
    }

    private void BuildPoints()
    {
        double dx = EndLeft - StartLeft;
        const double phi = 0.6180339887;
        double stub = dx / 2.0 * (1.0 - phi);
        _points[0] = new Point(StartLeft, StartTop);
        _points[1] = new Point(StartLeft + stub, StartTop);
        _points[2] = new Point(EndLeft - stub, EndTop);
        _points[3] = new Point(EndLeft, EndTop);
    }

    #region Interaction

    private void OnHoverPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this).Position;
        bool over = HitTestLine(pt);
        if (over && !IsHighlighted) { IsHighlighted = true; Focus(FocusState.Pointer); }
        else if (!over && IsHighlighted) IsHighlighted = false;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Windows.System.VirtualKey.Delete && IsHighlighted)
        {
            if (DataContext is IWorkflowLinkViewModel vm)
                vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private bool HitTestLine(Point pt)
    {
        const double hitRadius = 6.0;
        BuildPoints();
        for (int i = 0; i < _points.Length - 1; i++)
            if (DistSeg(pt, _points[i], _points[i + 1]) <= hitRadius) return true;
        return false;
    }

    private static double DistSeg(Point p, Point a, Point b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double len2 = abx * abx + aby * aby;
        if (len2 < 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0, 1);
        double px = a.X + t * abx - p.X, py = a.Y + t * aby - p.Y;
        return Math.Sqrt(px * px + py * py);
    }

    #endregion
}
