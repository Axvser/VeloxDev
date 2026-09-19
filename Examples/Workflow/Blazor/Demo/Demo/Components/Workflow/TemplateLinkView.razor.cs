using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow link view rendered as an orthogonal polyline with golden-ratio
/// stubs, mirroring the WPF template's geometry. Points derive from the endpoint slot
/// anchors; the polyline spans the whole canvas so links are absolutely positioned
/// (overflow visible) and redraw whenever the endpoints move.
/// </summary>
public partial class TemplateLinkView : ComponentBase, IDisposable
{
    private const double Phi = 0.6180339887;

    /// <summary>Gets or sets the link rendered by this view.</summary>
    [Parameter]
    public IWorkflowLinkViewModel? Link { get; set; }

    /// <summary>Gets or sets the canvas size the link spans.</summary>
    [Parameter]
    public double CanvasWidth { get; set; } = 1920;

    /// <summary>Gets or sets the canvas height the link spans.</summary>
    [Parameter]
    public double CanvasHeight { get; set; } = 1080;

    /// <summary>Gets or sets an optional line-color override (defaults to <c>#DDFFFFFF</c>).</summary>
    [Parameter]
    public string? LineColorOverride { get; set; }

    /// <summary>Gets or sets an optional thickness override (defaults to <c>2</c>).</summary>
    [Parameter]
    public string? ThicknessOverride { get; set; }

    /// <summary>Gets or sets an explicit virtual-link override (defaults to the sender/receiver-parent heuristic).</summary>
    [Parameter]
    public bool? IsVirtualOverride { get; set; }

    /// <summary>Gets or sets an explicit visibility override (defaults to <see cref="IWorkflowLinkViewModel.IsVisible"/>).</summary>
    [Parameter]
    public bool? CanRenderOverride { get; set; }

    private INotifyPropertyChanged? _notifier;
    private INotifyPropertyChanged? _senderNotifier;
    private INotifyPropertyChanged? _receiverNotifier;

    private string LineColor => LineColorOverride ?? ToCss("#DDFFFFFF");
    private double Thickness
    {
        get
        {
            if (ThicknessOverride is not null
                && double.TryParse(ThicknessOverride, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var t))
            {
                return t;
            }

            return double.Parse("2", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Converts XAML-style <c>#AARRGGBB</c> color literals (as used by the template symbols)
    /// into CSS color values, so symbol-driven colors work in Razor views. Also passes
    /// through named colors and CSS <c>rgb()/rgba()</c> strings unchanged.
    /// </summary>
    private static string ToCss(string value)
    {
        var text = value.Trim();
        if (text.Length == 9 && text[0] == '#')
        {
            var alpha = text.Substring(1, 2);
            var rgb = text.Substring(3);
            if (byte.TryParse(alpha, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var a))
            {
                return $"rgba({HexByte(rgb, 0)},{HexByte(rgb, 2)},{HexByte(rgb, 4)},{a / 255d:0.###})";
            }
        }

        if (text.Length == 7 && text[0] == '#')
        {
            return text;
        }

        return text;
    }

    private static int HexByte(string hex, int offset)
        => Convert.ToInt32(hex.Substring(offset, 2), 16);
    private bool CanRender { get; set; } = true;
    private bool IsVirtual { get; set; }

    private bool EffectiveCanRender => CanRenderOverride ?? CanRender;
    private bool EffectiveIsVirtual => IsVirtualOverride ?? IsVirtual;

    private string CanvasWidthCss => CanvasWidth.ToString("0.#");
    private string CanvasHeightCss => CanvasHeight.ToString("0.#");
    private string ThicknessCss => Thickness.ToString("0.#");
    private string MarkerSuffix => Link?.GetHashCode().ToString("X8") ?? "virtual";

    #region Flow effect

    /// <summary>
    /// The object the flow animation writes into. <c>Transition&lt;T&gt;</c> animates a member of a
    /// reference type, and on this platform that object is not a visual — the browser paints the gradient,
    /// so the only state is the cycle's progress. Raising <see cref="INotifyPropertyChanged"/> is what
    /// repaints the component: a Razor view has no property system that would notice a value on its own.
    /// </summary>
    private sealed class LinkFlow : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs PhaseChanged = new(nameof(Phase));

        private double _phase;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Cycle progress, 0→1: the whole of the animated state.</summary>
        public double Phase
        {
            get => _phase;
            set
            {
                _phase = value;
                PropertyChanged?.Invoke(this, PhaseChanged);
            }
        }
    }

    /// <summary>One stop of the flow gradient: where along the link it sits, and its colour.</summary>
    /// <remarks>
    /// Both members are already markup: the offset is formatted invariantly here rather than in the view,
    /// because Razor writes a bare <c>double</c> with the current culture and a comma decimal separator
    /// would serialize an SVG attribute that no browser can read.
    /// </remarks>
    private readonly record struct FlowStop(string Offset, string Color);

    // The band, in gradient-offset units along the link: half its width, and where its centre sits at the
    // end of each phase. The phases cover different distances, so they take different fractions of the
    // cycle rather than equal thirds.
    private const double BandHalfWidth = 0.04;
    private const double EnterEnd = 0.30;
    private const double FadeStart = 0.66;
    private const double BandFrom = 0.06;
    private const double BandFormed = 0.34;
    private const double BandLeaving = 0.66;
    private const double BandTo = 0.94;

    /// <summary>
    /// Walks the band across the link once per cycle, forever, so the link reads as carrying data from the
    /// sender's anchor to the receiver's. <see cref="LinkFlow.Phase"/> is the only animated value; the
    /// gradient's stops are derived from it.
    /// <para>
    /// Declared once and executed per component rather than built per call: the endpoint is the same every
    /// cycle, which is the case the animation reference puts in a <c>static readonly</c> field. A straight
    /// line rather than an eased curve, because the band should move at a constant speed — an ease would
    /// make each cycle pause at the ends and read as a series of pulses instead of a flow.
    /// </para>
    /// <para>
    /// The phases are one looping segment and a piecewise mapping rather than three segments joined with
    /// <c>Then()</c>, because nothing in the engine repeats a chain: a segment's <c>LoopTime</c> repeats
    /// that segment, the queue of segments is walked exactly once, and the loop guard reads a pass counter
    /// the whole run shares — so a second segment starts, completes, and writes no frame at all.
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

    private readonly LinkFlow _flow = new();

    private bool _flowRunning;
    private IWorkflowLinkViewModel? _flowLink;

    // The flow's three colours, as channels rather than markup so they can be mixed. Parsed once per
    // parameter change, not per frame.
    private (int A, int R, int G, int B) _dimColor = (0x9E, 0xFF, 0xFF, 0xFF);
    private (int A, int R, int G, int B) _litColor = (0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>Identifies this link's gradient; one definition per link, referenced by its stroke.</summary>
    private string FlowId => $"veloxdev-flow-{MarkerSuffix}";

    /// <summary>The settled link's stroke: the flow gradient. A virtual one keeps its flat dashed colour.</summary>
    private string FlowStroke => EffectiveIsVirtual ? LineColor : $"url(#{FlowId})";

    /// <summary>The band's colour, which the arrowhead also carries.</summary>
    private string LitCss => Css(_litColor);

    /// <summary>
    /// The gradient's three stops for the current cycle position: the band's two shoulders at the resting
    /// colour, and the lit colour — or the mix on its way to it — between them.
    /// </summary>
    private FlowStop[] CurrentFlowStops()
    {
        var phase = _flow.Phase;
        double centre;
        double mix;
        if (phase < EnterEnd)
        {
            var t = phase / EnterEnd;
            centre = BandFrom + (BandFormed - BandFrom) * t;
            mix = t;
        }
        else if (phase < FadeStart)
        {
            var t = (phase - EnterEnd) / (FadeStart - EnterEnd);
            centre = BandFormed + (BandLeaving - BandFormed) * t;
            mix = 1d;
        }
        else
        {
            var t = (phase - FadeStart) / (1d - FadeStart);
            centre = BandLeaving + (BandTo - BandLeaving) * t;
            mix = 1d - t;
        }

        var dim = Css(_dimColor);
        return
        [
            new FlowStop(N(centre - BandHalfWidth), dim),
            new FlowStop(N(centre), Css(Blend(_dimColor, _litColor, mix))),
            new FlowStop(N(centre + BandHalfWidth), dim),
        ];
    }

    /// <summary>
    /// The gradient's axis, in the canvas coordinates the polyline is drawn in — the link's own endpoints,
    /// not its bounding box, so the band travels along the link rather than across a diagonal of its box.
    /// </summary>
    private string[] FlowAxis => Link?.Sender is { } sender && Link.Receiver is { } receiver
        ?
        [
            N(sender.Anchor.Horizontal), N(sender.Anchor.Vertical),
            N(receiver.Anchor.Horizontal), N(receiver.Anchor.Vertical),
        ]
        : ["0", "0", "0", "0"];

    /// <summary>
    /// Starts the flow on a settled link and stops it on one that is not: a virtual link is the rubber band
    /// under the pointer, and a band that streamed along it would claim a connection that does not exist
    /// yet. Also restarts when this view is handed a different link, which is what the render-ready gate
    /// makes routine — a pooled view is reused before its first link is ever measured.
    /// </summary>
    private void SyncFlow()
    {
        if (EffectiveIsVirtual || !EffectiveCanRender)
        {
            if (!_flowRunning) return;

            Transition.Exit(_flow, IncludeMutual: true, IncludeNoMutual: true);
            _flowRunning = false;
            _flowLink = null;
            return;
        }

        if (_flowRunning && ReferenceEquals(_flowLink, Link)) return;

        // The transition reads its start value from the target, so the cycle has to be at its beginning
        // before Execute — and the loop replays that captured start at every seam.
        _flow.Phase = 0d;
        Flow.Execute(_flow);
        _flowRunning = true;
        _flowLink = Link;
    }

    /// <summary>Parses the XAML-style colour literal the template symbols carry, as four channels.</summary>
    /// <remarks>
    /// A literal this cannot read — a CSS colour name, say — falls back to the templates' own
    /// <c>#DDFFFFFF</c>, because the flow needs channel values to mix and a band it cannot mix is a band it
    /// cannot draw.
    /// </remarks>
    private static (int A, int R, int G, int B) ParseColor(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 8
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                return ((argb >> 24) & 0xFF, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
            }

            if (hex.Length == 6
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                return (0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
        }

        return (0xDD, 0xFF, 0xFF, 0xFF);
    }

    /// <summary>
    /// Recomputes the flow's colours from the link's own. The lit colour keeps the hue and is lifted a
    /// little towards white; the resting line is the lit colour dimmed to about three fifths, which is what
    /// makes a lit band read as a band.
    /// </summary>
    /// <remarks>
    /// Dimming by alpha is what keeps the hue. The alternative that suggests itself — a "highlight" that is
    /// the line colour pushed <em>towards white</em> — has been tried in this demo suite and is invisible:
    /// on a 2px line against a dark canvas, a cyan link lifted 75% towards white differs from cyan in one
    /// channel out of three.
    /// </remarks>
    private void UpdateFlowColors()
    {
        const double lift = 0.45;
        var line = ParseColor(LineColorOverride ?? "#DDFFFFFF");

        byte Up(int channel) => (byte)Math.Round(channel + (255 - channel) * lift);

        _litColor = (0xFF, Up(line.R), Up(line.G), Up(line.B));
        _dimColor = ((int)Math.Round(_litColor.A * 0.62), _litColor.R, _litColor.G, _litColor.B);
    }

    private void OnFlowChanged(object? sender, PropertyChangedEventArgs e) => InvokeAsync(StateHasChanged);

    private static (int A, int R, int G, int B) Blend(
        (int A, int R, int G, int B) from,
        (int A, int R, int G, int B) to,
        double t) => (
        (int)Math.Round(from.A + (to.A - from.A) * t),
        (int)Math.Round(from.R + (to.R - from.R) * t),
        (int)Math.Round(from.G + (to.G - from.G) * t),
        (int)Math.Round(from.B + (to.B - from.B) * t));

    private static string Css((int A, int R, int G, int B) color) => color.A >= 0xFF
        ? $"rgb({color.R},{color.G},{color.B})"
        : $"rgba({color.R},{color.G},{color.B},{color.A / 255d:0.###})";

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    #endregion

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Sync(Link);
        UpdateFlowColors();
        SyncFlow();
        _flow.PropertyChanged += OnFlowChanged;
    }

    private void Sync(IWorkflowLinkViewModel? link)
    {
        if (link is null) return;

        CanRender = link.IsVisible;
        IsVirtual = IsVirtualLink(link);

        if (link is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnLinkChanged;
        }

        if (link.Sender is INotifyPropertyChanged s)
        {
            _senderNotifier = s;
            s.PropertyChanged += OnEndpointChanged;
        }

        if (link.Receiver is INotifyPropertyChanged r)
        {
            _receiverNotifier = r;
            r.PropertyChanged += OnEndpointChanged;
        }
    }

    private void OnLinkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible) or null or "")
        {
            CanRender = Link?.IsVisible == true;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.Sender)
            or nameof(IWorkflowLinkViewModel.Receiver)
            or null or "")
        {
            IsVirtual = IsVirtualLink(Link);
        }

        SyncFlow();
        InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        UpdateFlowColors();
        SyncFlow();
        if (IsVirtualOverride is not null || CanRenderOverride is not null)
        {
            InvokeAsync(StateHasChanged);
        }
    }

    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor) or null or "")
        {
            InvokeAsync(StateHasChanged);
        }
    }

    private bool IsVirtualLink(IWorkflowLinkViewModel? link)
        => link is null || (link.Sender?.Parent is null && link.Receiver?.Parent is null);

    private string BuildPoints()
    {
        var link = Link;
        if (link is null) return "";

        var sender = link.Sender;
        var receiver = link.Receiver;
        if (sender is null || receiver is null) return "";

        // NaN gate: slot anchors default to NaN (unmeasured placeholder). Rendering before
        // the GUI measures the endpoints would serialize NaN coordinates and paint a stale
        // frame that jumps back once measurement lands — the first-entry flicker the XAML
        // adapters guard against via WorkflowLinkRenderEx.IsRenderReady(). Skip until both
        // non-virtual endpoints are measured. Placeholder endpoints (Parent is null, e.g. the
        // VirtualLink gesture) are exempt and render immediately.
        if (!WorkflowSlotUpdateGate.IsLinkRenderReady(link)) return "";

        double sx = sender.Anchor.Horizontal;
        double sy = sender.Anchor.Vertical;
        double ex = receiver.Anchor.Horizontal;
        double ey = receiver.Anchor.Vertical;

        // Placeholder endpoints (VirtualLink gesture) can still carry NaN anchors on the
        // reset intermediate frames (Reset nulls the anchors before clearing IsVisible), which
        // would serialize a "NaN,NaN" polyline. Suppress until the coordinates are real.
        if (double.IsNaN(sx) || double.IsNaN(sy) || double.IsNaN(ex) || double.IsNaN(ey)) return "";

        double dx = ex - sx;
        // Signed stub keeps the orthogonal bend on the correct side when dragging leftward.
        double stub = dx / 2.0 * (1.0 - Phi);
        double p1x = sx + stub;
        double p4x = ex - stub;

        return $"{sx:F1},{sy:F1} {p1x:F1},{sy:F1} {p4x:F1},{ey:F1} {ex:F1},{ey:F1}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The animation outlives neither the component nor its render: a disposed link view that kept
        // streaming would keep calling StateHasChanged into a renderer that has moved on.
        Transition.Exit(_flow, IncludeMutual: true, IncludeNoMutual: true);
        _flowRunning = false;
        _flow.PropertyChanged -= OnFlowChanged;

        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnLinkChanged;
            _notifier = null;
        }

        if (_senderNotifier is not null)
        {
            _senderNotifier.PropertyChanged -= OnEndpointChanged;
            _senderNotifier = null;
        }

        if (_receiverNotifier is not null)
        {
            _receiverNotifier.PropertyChanged -= OnEndpointChanged;
            _receiverNotifier = null;
        }
    }
}
