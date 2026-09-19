using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Graphics;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Viewport-sized single-draw link overlay for a workflow surface. Renders every real link
/// (plus the in-progress virtual connection) in ONE <see cref="GraphicsView"/> draw pass that
/// lives in the decorator's coordinate space — the same frame the grid and ruler use.
///
/// It deliberately does NOT live inside the scrolling world canvas (which grows by 1/Scale on
/// zoom-in): a canvas-sized GraphicsView exceeds the Win2D ~16k device-pixel texture cap at
/// deep zoom and the whole layer silently disappears. Sized to the viewport instead, the overlay
/// transforms each endpoint's collapsed canvas-local anchor <c>c</c> (= slot/node <see cref="Anchor"/>
/// value) to viewport pixels via the identity shared with the grid drawable:
///   px = RulerThickness + c + ContentOffset − ScrollOffset
/// where ContentOffset == Layout.ActualOffset (the world origin) and ScrollOffset == the
/// ScrollViewer offset. No negative-coordinate shift / TranslationX counter-compensation is
/// needed: geometry the canvas itself would clip simply never enters the viewport, and links
/// that fall outside it are culled by bounding box before drawing.
/// </summary>
public sealed class WorkflowLinkOverlay : GraphicsView
{
    private const float ArrowHeadLength = 12f;
    private const float ArrowHeadWidth = 8f;
    private const double CullMargin = 24d;

    private static readonly Color DefaultWhite = Color.FromArgb("#DDFFFFFF");

    public static readonly BindableProperty WorkflowTreeProperty = BindableProperty.Create(
        nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(WorkflowLinkOverlay), null,
        propertyChanged: OnWorkflowTreeChanged);

    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(
        nameof(ScrollOffsetX), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(
        nameof(ScrollOffsetY), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(
        nameof(ContentOffsetX), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(
        nameof(ContentOffsetY), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty RulerThicknessProperty = BindableProperty.Create(
        nameof(RulerThickness), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty LinkLineColorProperty = BindableProperty.Create(
        nameof(LinkLineColor), typeof(Color), typeof(WorkflowLinkOverlay), DefaultWhite, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty VirtualLineColorProperty = BindableProperty.Create(
        nameof(VirtualLineColor), typeof(Color), typeof(WorkflowLinkOverlay), DefaultWhite, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(
        nameof(StrokeWidth), typeof(double), typeof(WorkflowLinkOverlay), 4d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty LinkFlowEnabledProperty = BindableProperty.Create(
        nameof(LinkFlowEnabled), typeof(bool), typeof(WorkflowLinkOverlay), false, propertyChanged: OnLinkFlowEnabledChanged);

    private IWorkflowTreeViewModel? _tree;
    private IWorkflowLinkViewModel? _virtualLink;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];
    private bool _invalidatePending;

    private double _flowPhase;

    public WorkflowLinkOverlay()
    {
        InputTransparent = true;
        Drawable = new LinkOverlayDrawable(this);

        // The flow is a per-frame animation, so it is bound to this element's own lifetime: loaded starts
        // it, unloaded stops it, and a view taken out of the tree must not keep a timer's worth of frames
        // arriving for a link layer nobody can see.
        Loaded += (_, _) => StartFlow();
        Unloaded += (_, _) => StopFlow();
    }

    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double RulerThickness { get => (double)GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }
    public Color? LinkLineColor { get => (Color?)GetValue(LinkLineColorProperty); set => SetValue(LinkLineColorProperty, value); }
    public Color? VirtualLineColor { get => (Color?)GetValue(VirtualLineColorProperty); set => SetValue(VirtualLineColorProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    /// <summary>
    /// Whether a settled link carries the travelling highlight that shows which way its data flows.
    /// Off by default, because it is decoration: a minimal editor draws plain links.
    /// </summary>
    public bool LinkFlowEnabled { get => (bool)GetValue(LinkFlowEnabledProperty); set => SetValue(LinkFlowEnabledProperty, value); }

    /// <summary>
    /// Cycle progress of the flow, 0→1: the one animated value the travelling highlight is drawn from.
    /// Public because the animation addresses it by name, and writing it is what schedules the repaint —
    /// the band's position and colour are derived from it while drawing.
    /// </summary>
    public double FlowPhase
    {
        get => _flowPhase;
        set
        {
            _flowPhase = value;
            ScheduleInvalidate();
        }
    }

    #region Flow effect

    /// <summary>Half the band's width, in gradient-offset units along the link.</summary>
    private const double FlowBandHalfWidth = 0.04;

    // One cycle, as fractions of it: the band travels a third of the link while forming, a third while
    // fully lit, and a third while leaving. The phases cover different distances, so they are not equal
    // thirds of the cycle.
    private const double FlowEnterEnd = 0.30;
    private const double FlowFadeStart = 0.66;
    private const double FlowBandFrom = 0.06;
    private const double FlowBandFormed = 0.34;
    private const double FlowBandLeaving = 0.66;
    private const double FlowBandTo = 0.94;

    /// <summary>
    /// Walks the band across every settled link once per cycle, forever. One animation drives the whole
    /// overlay rather than one per link, because this layer draws all links in a single pass: the bands
    /// therefore advance together, which is what a surface-wide flow looks like.
    /// <para>
    /// The phases are one looping segment and a piecewise mapping rather than three segments joined with
    /// <c>Then()</c>, because nothing in the engine repeats a chain: a segment's <c>LoopTime</c> repeats
    /// that segment, the queue of segments is walked exactly once, and the loop guard reads a pass counter
    /// the whole run shares — so a second segment samples no frames at all. One looping segment also gives
    /// a seamless seam: a loop replays the endpoints captured when it started, and the line is uniformly
    /// dim at both ends of a cycle.
    /// </para>
    /// </summary>
    private static readonly Transition<WorkflowLinkOverlay> Flow =
        Transition<WorkflowLinkOverlay>.Create()
            .Property(o => o.FlowPhase, 1d)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(1.8),
                LoopTime = int.MaxValue,
                Ease = Eases.Default,
            });

    /// <summary>
    /// Starts the flow. Called from <c>Loaded</c>, and again when the switch is turned on, so the band
    /// always starts from the sender's end rather than wherever the previous run left it.
    /// </summary>
    private void StartFlow()
    {
        if (!LinkFlowEnabled)
        {
            return;
        }

        // The transition reads its start value from the target, so the cycle has to be at its beginning
        // before Execute.
        FlowPhase = 0d;
        Flow.Execute(this);
    }

    private void StopFlow()
        => Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);

    private static void OnLinkFlowEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not WorkflowLinkOverlay overlay)
        {
            return;
        }

        if (newValue is true)
        {
            overlay.StartFlow();
        }
        else
        {
            overlay.StopFlow();
        }

        overlay.ScheduleInvalidate();
    }

    /// <summary>Where the band's centre sits, and how lit it is, at one position in the cycle.</summary>
    /// <remarks>
    /// The three phases, as one piecewise mapping: the band comes up from the resting colour over the first
    /// third of its travel — so it appears rather than sliding in from off-link — travels fully lit and
    /// unchanged for the middle third, which is the phase that reads as flow rather than as a pulse, and
    /// settles back over the last third.
    /// </remarks>
    private static (double Centre, double Mix) BandAt(double phase)
    {
        if (phase < FlowEnterEnd)
        {
            var t = phase / FlowEnterEnd;
            return (FlowBandFrom + (FlowBandFormed - FlowBandFrom) * t, t);
        }

        if (phase < FlowFadeStart)
        {
            var t = (phase - FlowEnterEnd) / (FlowFadeStart - FlowEnterEnd);
            return (FlowBandFormed + (FlowBandLeaving - FlowBandFormed) * t, 1d);
        }

        var fade = (phase - FlowFadeStart) / (1d - FlowFadeStart);
        return (FlowBandLeaving + (FlowBandTo - FlowBandLeaving) * fade, 1d - fade);
    }

    /// <summary>
    /// The band's colour: the link's own colour at full strength, lifted a little so a link that is already
    /// white still has somewhere brighter to go.
    /// </summary>
    private static Color FlowLit(Color color)
    {
        const double lift = 0.45;

        float Up(float channel) => (float)Math.Round(channel + (1d - channel) * lift, 6);

        return new Color(Up(color.Red), Up(color.Green), Up(color.Blue), 1f);
    }

    /// <summary>
    /// The link's resting colour: the lit colour dimmed to a little under two thirds, which is what makes a
    /// lit band read as a band.
    /// </summary>
    /// <remarks>
    /// Dimming by alpha is what keeps the hue. The alternative that suggests itself — a "highlight" that is
    /// the link colour pushed towards white — is invisible on a saturated light colour: on a 2px line
    /// against a dark canvas, a cyan link lifted 75% towards white differs from cyan in one channel out of
    /// three. Making the resting line the dim one puts the contrast where the eye finds it.
    /// </remarks>
    private static Color FlowDim(Color color)
        => new(color.Red, color.Green, color.Blue, (float)(color.Alpha * 0.62));

    private static Color FlowBlend(Color from, Color to, double t) => new(
        (float)Math.Round(from.Red + (to.Red - from.Red) * t, 6),
        (float)Math.Round(from.Green + (to.Green - from.Green) * t, 6),
        (float)Math.Round(from.Blue + (to.Blue - from.Blue) * t, 6),
        (float)Math.Round(from.Alpha + (to.Alpha - from.Alpha) * t, 6));

    /// <summary>How many pieces a flowed link is drawn in.</summary>
    /// <remarks>
    /// Enough that the steps between them are invisible at the stroke widths these demos use, and few
    /// enough that a viewport full of links stays cheap per frame.
    /// </remarks>
    private const int FlowSegments = 24;

    /// <summary>The colour a point on the link carries — where it is along the link is <paramref name="u"/>.</summary>
    private static Color BandColorAt(Color dim, Color lit, double u, double centre, double mix)
    {
        var reach = Math.Abs(u - centre) / FlowBandHalfWidth;
        return reach >= 1d ? dim : FlowBlend(dim, lit, mix * (1d - reach));
    }

    /// <summary>
    /// Strokes one link with the flow and returns the colour its arrowhead should carry. The link is drawn
    /// as a run of short pieces, each stroked with the colour the band's mapping gives it at that point
    /// along the link.
    /// </summary>
    /// <remarks>
    /// Sampled rather than painted with a gradient brush, because this version of <c>ICanvas</c> can set a
    /// <em>fill</em> paint and has no stroke equivalent — a gradient stroke is not expressible, and the
    /// pieces reproduce it exactly (the mapping between the two shoulders is linear, so a piece per
    /// <see cref="FlowSegments"/>th of the link is the same ramp drawn in steps). It also has one advantage
    /// the straight-axis gradients the other adapters stroke with do not: a link here is an elbow, and
    /// because the mapping is sampled along the link's own length the band follows the corner instead of
    /// being projected across it.
    /// </remarks>
    private Color ApplyFlowStroke(
        ICanvas canvas,
        Color color,
        float startX,
        float startY,
        float turn1X,
        float turn2X,
        float endX,
        float endY)
    {
        var lit = FlowLit(color);
        var dim = FlowDim(lit);
        var (centre, mix) = BandAt(_flowPhase);

        // The elbow is three runs — horizontal stub, diagonal, horizontal stub — and a piece is placed by
        // how far along the link it is, not by the fraction of a bounding box it crosses.
        var stub = MathF.Abs(turn1X - startX);
        var diagonal = MathF.Sqrt(((turn2X - turn1X) * (turn2X - turn1X)) + ((endY - startY) * (endY - startY)));
        var tail = MathF.Abs(endX - turn2X);
        var total = stub + diagonal + tail;
        if (total <= float.Epsilon)
        {
            return lit;
        }

        var previousX = startX;
        var previousY = startY;
        for (var piece = 1; piece <= FlowSegments; piece++)
        {
            var distance = (piece / (float)FlowSegments) * total;
            float x;
            float y;
            if (distance <= stub)
            {
                var t = stub <= float.Epsilon ? 1f : distance / stub;
                x = startX + ((turn1X - startX) * t);
                y = startY;
            }
            else if (distance <= stub + diagonal)
            {
                var t = diagonal <= float.Epsilon ? 1f : (distance - stub) / diagonal;
                x = turn1X + ((turn2X - turn1X) * t);
                y = startY + ((endY - startY) * t);
            }
            else
            {
                var t = tail <= float.Epsilon ? 1f : (distance - stub - diagonal) / tail;
                x = turn2X + ((endX - turn2X) * t);
                y = endY;
            }

            // Coloured at the piece's midpoint: a piece carries the colour of the length it covers, not of
            // its far end, which is what keeps a bright band from trailing half a piece behind itself.
            canvas.StrokeColor = BandColorAt(dim, lit, (piece - 0.5f) / FlowSegments, centre, mix);
            canvas.DrawLine(previousX, previousY, x, y);

            previousX = x;
            previousY = y;
        }

        return lit;
    }

    #endregion

    private static void OnWorkflowTreeChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is WorkflowLinkOverlay overlay)
        {
            overlay.AttachTree(newValue as IWorkflowTreeViewModel);
            overlay.ScheduleInvalidate();
        }
    }

    private static void OnVisualPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is WorkflowLinkOverlay overlay)
        {
            overlay.ScheduleInvalidate();
        }
    }

    // ── Model subscriptions (mirrors the superseded LinkLayerView pattern) ─────

    private void AttachTree(IWorkflowTreeViewModel? tree)
    {
        if (ReferenceEquals(_tree, tree))
        {
            return;
        }

        Unsubscribe();
        _tree = tree;
        if (tree is null)
        {
            return;
        }

        Subscribe(tree);
    }

    private void Subscribe(IWorkflowTreeViewModel tree)
    {
        if (tree is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnTreePropertyChanged;
        }

        if (tree.Nodes is INotifyCollectionChanged nc)
        {
            nc.CollectionChanged += OnNodesChanged;
            foreach (var n in tree.Nodes)
            {
                SubscribeNode(n);
            }
        }

        if (tree.Links is INotifyCollectionChanged lc)
        {
            lc.CollectionChanged += OnLinksChanged;
            foreach (var l in tree.Links)
            {
                SubscribeLink(l);
            }
        }

        SubscribeVirtualLink(tree.VirtualLink);
    }

    private void Unsubscribe()
    {
        if (_tree is null)
        {
            return;
        }

        if (_tree is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= OnTreePropertyChanged;
        }

        if (_tree.Nodes is INotifyCollectionChanged nc)
        {
            nc.CollectionChanged -= OnNodesChanged;
        }

        if (_tree.Links is INotifyCollectionChanged lc)
        {
            lc.CollectionChanged -= OnLinksChanged;
        }

        foreach (var n in _subscribedNodes)
        {
            if (n is INotifyPropertyChanged np)
            {
                np.PropertyChanged -= OnNodePropertyChanged;
            }
        }

        _subscribedNodes.Clear();

        foreach (var l in _subscribedLinks)
        {
            UnsubscribeLink(l);
        }

        _subscribedLinks.Clear();
        SubscribeVirtualLink(null);
        _tree = null;
    }

    private void SubscribeNode(IWorkflowNodeViewModel node)
    {
        if (_subscribedNodes.Add(node) && node is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void SubscribeLink(IWorkflowLinkViewModel link)
    {
        if (!_subscribedLinks.Add(link))
        {
            return;
        }

        if (link is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnLinkPropertyChanged;
        }

        if (link.Sender is INotifyPropertyChanged sp)
        {
            sp.PropertyChanged += OnSlotPropertyChanged;
        }

        if (link.Receiver is INotifyPropertyChanged rp)
        {
            rp.PropertyChanged += OnSlotPropertyChanged;
        }
    }

    private void UnsubscribeLink(IWorkflowLinkViewModel link)
    {
        if (link is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= OnLinkPropertyChanged;
        }

        if (link.Sender is INotifyPropertyChanged sp)
        {
            sp.PropertyChanged -= OnSlotPropertyChanged;
        }

        if (link.Receiver is INotifyPropertyChanged rp)
        {
            rp.PropertyChanged -= OnSlotPropertyChanged;
        }

        _subscribedLinks.Remove(link);
    }

    private void SubscribeVirtualLink(IWorkflowLinkViewModel? link)
    {
        if (ReferenceEquals(_virtualLink, link))
        {
            return;
        }

        if (_virtualLink is not null)
        {
            UnsubscribeLink(_virtualLink);
        }

        _virtualLink = link;
        if (link is not null)
        {
            SubscribeLink(link);
        }
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowTreeViewModel.VirtualLink) && _tree is not null)
        {
            SubscribeVirtualLink(_tree.VirtualLink);
            ScheduleInvalidate();
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var i in e.NewItems)
            {
                if (i is IWorkflowNodeViewModel n)
                {
                    SubscribeNode(n);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var i in e.OldItems)
            {
                if (i is IWorkflowNodeViewModel n && _subscribedNodes.Remove(n) && n is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged -= OnNodePropertyChanged;
                }
            }
        }

        ScheduleInvalidate();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var i in e.NewItems)
            {
                if (i is IWorkflowLinkViewModel l)
                {
                    SubscribeLink(l);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var i in e.OldItems)
            {
                if (i is IWorkflowLinkViewModel l)
                {
                    UnsubscribeLink(l);
                }
            }
        }

        ScheduleInvalidate();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            ScheduleInvalidate();
        }
    }

    private void OnLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible))
        {
            ScheduleInvalidate();
            return;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.Sender) or nameof(IWorkflowLinkViewModel.Receiver)
            && sender is IWorkflowLinkViewModel link && _subscribedLinks.Contains(link))
        {
            // Endpoints rewired — re-subscribe to the new slots' anchors.
            if (link.Sender is INotifyPropertyChanged sp)
            {
                sp.PropertyChanged -= OnSlotPropertyChanged;
                sp.PropertyChanged += OnSlotPropertyChanged;
            }

            if (link.Receiver is INotifyPropertyChanged rp)
            {
                rp.PropertyChanged -= OnSlotPropertyChanged;
                rp.PropertyChanged += OnSlotPropertyChanged;
            }

            ScheduleInvalidate();
        }
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor))
        {
            ScheduleInvalidate();
        }
    }

    /// <summary>
    /// A scroll/pan/zoom frame writes several offset DPs back-to-back, and every model
    /// event above wants a redraw too. Coalesce them into ONE Win2D Invalidate per frame
    /// by flushing at the next main-thread dispatch instead of invalidating inline.
    /// </summary>
    private void ScheduleInvalidate()
    {
        if (_invalidatePending)
        {
            return;
        }

        _invalidatePending = true;
        MainThread.BeginInvokeOnMainThread(FlushInvalidate);
    }

    private void FlushInvalidate()
    {
        _invalidatePending = false;
        Invalidate();
    }

    // ── Drawing ──────────────────────────────────────────────────────────────

    private static IEnumerable<IWorkflowLinkViewModel> EnumerateVisibleLinks(IWorkflowTreeViewModel tree)
    {
        foreach (var link in tree.Links)
        {
            if (link.IsVisible)
            {
                yield return link;
            }
        }

        if (tree.VirtualLink is { IsVisible: true } virtualLink)
        {
            yield return virtualLink;
        }
    }

    private static bool TryGetEndpoints(IWorkflowLinkViewModel link, out float startX, out float startY, out float endX, out float endY)
    {
        startX = (float)link.Sender.Anchor.Horizontal;
        startY = (float)link.Sender.Anchor.Vertical;
        endX = (float)link.Receiver.Anchor.Horizontal;
        endY = (float)link.Receiver.Anchor.Vertical;
        // A slot that has not been laid out yet has a NaN anchor — skip it rather
        // than feed NaN through Win2D.
        return !float.IsNaN(startX) && !float.IsNaN(startY) && !float.IsNaN(endX) && !float.IsNaN(endY);
    }

    private static bool IsVirtualLink(IWorkflowLinkViewModel link)
        => link.Sender.Parent is null || link.Receiver.Parent is null;

    private sealed class LinkOverlayDrawable(WorkflowLinkOverlay owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var tree = owner._tree;
            if (tree is null || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            var ruler = Math.Max(0d, owner.RulerThickness);
            var ox = owner.ContentOffsetX;
            var oy = owner.ContentOffsetY;
            var scrollX = owner.ScrollOffsetX;
            var scrollY = owner.ScrollOffsetY;
            var linkColor = owner.LinkLineColor;
            var virtualColor = owner.VirtualLineColor;
            var strokeWidth = (float)Math.Max(0.5d, owner.StrokeWidth);

            // Viewport bounds (expanded a little so a link edge never pops at the frame boundary).
            var left = dirtyRect.Left - (float)CullMargin;
            var right = dirtyRect.Right + (float)CullMargin;
            var top = dirtyRect.Top - (float)CullMargin;
            var bottom = dirtyRect.Bottom + (float)CullMargin;

            // Transform each collapsed canvas-local anchor c to viewport pixels:
            // px = Ruler + c + ContentOffset − ScrollOffset (shared with the grid drawable).
            float ToX(float c) => (float)(ruler + c + ox - scrollX);
            float ToY(float c) => (float)(ruler + c + oy - scrollY);

            foreach (var link in EnumerateVisibleLinks(tree))
            {
                if (!TryGetEndpoints(link, out var csx, out var csy, out var cex, out var cey))
                {
                    continue;
                }

                var isVirtual = IsVirtualLink(link);
                var color = isVirtual ? virtualColor : linkColor;
                if (color is null)
                {
                    continue;
                }

                const float phi = 0.6180339887f;
                var stub = ((cex - csx) / 2f) * (1f - phi);

                var startX = ToX(csx);
                var startY = ToY(csy);
                var turn1X = ToX(csx + stub);
                var turn2X = ToX(cex - stub);
                var endX = ToX(cex);
                var endY = ToY(cey);

                // Whole-link bounding-box cull: segments outside the viewport are clipped by the
                // canvas anyway, so drawing them is pure waste (and deep zoom could send huge
                // coordinates through Win2D otherwise).
                var minX = Math.Min(startX, Math.Min(turn1X, Math.Min(turn2X, endX)));
                var maxX = Math.Max(startX, Math.Max(turn1X, Math.Max(turn2X, endX)));
                var minY = Math.Min(startY, endY);
                var maxY = Math.Max(startY, endY);
                if (maxX < left || minX > right || maxY < top || minY > bottom)
                {
                    continue;
                }

                canvas.StrokeSize = strokeWidth;
                canvas.StrokeDashPattern = isVirtual ? [4, 2] : null;

                // A flowed link is drawn in pieces carrying the band's colours, and its arrowhead carries
                // the band's own colour — the line rests dim, and an arrowhead dimmed with it would be the
                // one part of the link that never lights up. A virtual link is the rubber band under the
                // pointer, so it keeps the flat dashed pen.
                var arrowColor = color;
                if (isVirtual || !owner.LinkFlowEnabled)
                {
                    canvas.StrokeColor = color;
                    canvas.DrawLine(startX, startY, turn1X, startY);
                    canvas.DrawLine(turn1X, startY, turn2X, endY);
                    canvas.DrawLine(turn2X, endY, endX, endY);
                }
                else
                {
                    arrowColor = owner.ApplyFlowStroke(
                        canvas, color, startX, startY, turn1X, turn2X, endX, endY);
                }

                if (!isVirtual)
                {
                    DrawArrowhead(canvas, turn2X, endY, endX, endY, arrowColor);
                }

                canvas.StrokeDashPattern = null;
            }
        }

        private static void DrawArrowhead(ICanvas canvas, float fromX, float fromY, float tipX, float tipY, Color color)
        {
            var dx = tipX - fromX;
            var dy = tipY - fromY;
            var length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length <= float.Epsilon)
            {
                return;
            }

            dx /= length;
            dy /= length;
            var normalX = -dy;
            var normalY = dx;
            var baseX = tipX - (dx * ArrowHeadLength);
            var baseY = tipY - (dy * ArrowHeadLength);
            var leftX = baseX + (normalX * (ArrowHeadWidth / 2f));
            var leftY = baseY + (normalY * (ArrowHeadWidth / 2f));
            var rightX = baseX - (normalX * (ArrowHeadWidth / 2f));
            var rightY = baseY - (normalY * (ArrowHeadWidth / 2f));

            var arrow = new PathF();
            arrow.MoveTo(tipX, tipY);
            arrow.LineTo(leftX, leftY);
            arrow.LineTo(rightX, rightY);
            arrow.Close();

            canvas.FillColor = color;
            canvas.FillPath(arrow);
        }
    }
}
