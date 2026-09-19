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
    // AimFlowBrush; the seed value is only what it wears until the link's own colour is known.
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

        // The brush is the view's own and the stops are only created here: AimFlowBrush aims it, gives it its
        // colours and builds the declaration whose endpoints those colours are.
        AimFlowBrush();

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

        // The gradient runs along the link's own axis and is mixed from the link's own colour, so an endpoint
        // and the colour are both inputs to the brush rather than to the drawing code. Re-aiming is all this
        // may do: the cycle owns the band's position, so a handler here that re-seated the stops would park the
        // band at the sender's anchor for as long as the gesture lasted — a stutter, not flow.
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
        IsTabStop = !IsVirtual;
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
    private Windows.UI.Color Lit { get; set; }

    /// <summary>The line's resting colour: the lit colour dimmed to a little under two thirds.</summary>
    private Windows.UI.Color Dim { get; set; }

    private Transition<PolylineCurveView>? _flow;
    private bool _running;

    /// <summary>
    /// The flow, as the three phases it is made of, declared one after the other and repeated forever.
    /// </summary>
    /// <remarks>
    /// Built per view rather than held in a <c>static readonly</c> field, because two of its endpoints are the
    /// link's own colours, and a declaration that reads a local is shared by every later execution of it — here
    /// that would paint one link's band in another link's colour. WinUI adds a second reason for the same shape:
    /// a <c>Transition&lt;T&gt;</c> field is built on whichever thread first touches the declaring type, and that
    /// is a documented hazard in this repository's WinUI demos (see
    /// <c>Examples/Transition/WinUI/Demo/MainWindow.xaml.cs</c>). Built here, where every caller is already on
    /// the UI thread, the hazard cannot arise — at the cost of one declaration per view.
    /// <para>
    /// The paths go into the brush itself: <c>GradientStops[1]</c> is the band and the two stops either side of
    /// it are its shoulders, so a phase is a handful of indexed writes and the phase structure is readable
    /// rather than computed. A straight line rather than an eased curve, because the band should move at a
    /// constant speed — an ease would make each cycle pause at the ends and read as pulses instead of flow.
    /// </para>
    /// <para>
    /// Nothing here asks for a repaint, where every segment of the WPF port attaches one. WinUI's gradient stop
    /// is a dependency object under a dependency-property brush and these are in-place writes to the brush the
    /// <see cref="Path"/> already strokes with — which is exactly what the phase-scalar design this replaces
    /// did, and it carried no repaint path either. The rewrite is therefore repaint-neutral against the code it
    /// replaces: whatever the framework did for those same writes, it does for these. The WPF adapter's repaint
    /// exists because a DrawingContext keeps the brush by reference without subscribing to it, which is a fact
    /// about WPF's immediate-mode drawing and not about WinUI's retained <see cref="Path"/>.
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
    /// <para>
    /// WinUI's <see cref="LinearGradientBrush"/> has no <c>MappingMode</c>: its axis is always expressed in the
    /// own 0..1 space of the geometry the shape paints. This Path's geometry is the whole polyline, in the
    /// control's own coordinates, so each endpoint has to be converted into a position inside the geometry's
    /// bounds — a gradient declared as (0,0)→(1,1) would sweep along the bounding box's diagonal rather than
    /// along the link, and on a link whose endpoints run up and to the left it would run backwards as well.
    /// </para>
    /// </remarks>
    private void AimFlowBrush()
    {
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

        FlowBrush.StartPoint = new Point((StartLeft - minX) / width, (StartTop - minY) / height);
        FlowBrush.EndPoint = new Point((EndLeft - minX) / width, (EndTop - minY) / height);

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
            stops.Add(new GradientStop { Color = Dim, Offset = BandStart - BandHalfWidth });
            stops.Add(new GradientStop { Color = Dim, Offset = BandStart });
            stops.Add(new GradientStop { Color = Dim, Offset = BandStart + BandHalfWidth });
        }
        else
        {
            stops[0].Color = Dim;
            stops[2].Color = Dim;
        }

        // The arrowhead is the destination marker and carries the band's colour rather than the gradient, so it
        // is repainted with the declaration rather than on the render path: it is the one part of a link that
        // must not be left resting dim.
        _arrowBrush.Color = Lit;

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
    /// Starts the cycle from the sender's end. Started on load so a pooled view that is handed a different link
    /// animates that link rather than the one it was built for.
    /// </summary>
    private void StartFlow()
    {
        if (IsVirtual || !CanRender)
        {
            StopFlow();
            return;
        }

        // Loaded is the only place a view is known to be in the tree, and WinUI can raise a property change
        // while it is not — a pooled view being prepared for its next link, for instance. A flow started there
        // would animate a link nobody is looking at with no unload left to stop it, so it is skipped; Loaded
        // starts one as soon as the view is really on screen.
        if (!_isLoaded) return;

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
    /// running on it, driving the brush of whatever it is reused for.
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
        // must not leave the old animation running on it.
        StopFlow();
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
        _path.Stroke = IsHighlighted || IsVirtual ? _strokeBrush : FlowBrush;

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
