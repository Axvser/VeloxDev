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
/// <para>
/// The view is also the object the flow animates — <c>Transition&lt;TemplateLinkView&gt;</c>. A Razor component
/// is a class, so the band's three stop offsets and its colour are this component's own members and the markup
/// reads the cycle's position straight off it, with nothing in between to map back into stops.
/// </para>
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
    /// The band's three stop offsets, in gradient units along the link, and its colour: the whole of the
    /// animated state, and the reason this component — rather than a model behind it — is the animation's
    /// target. <c>Transition&lt;T&gt;</c> animates a member of a reference type, and a Razor component is a
    /// class, so the animated paths read straight off the view and there is no scalar in between to map back
    /// into stops. The offsets are formatted invariantly by <see cref="N"/> in the markup, because Razor
    /// writes a bare <c>double</c> with the current culture and a comma decimal separator would serialize
    /// an SVG attribute no browser can read.
    /// </summary>
    private double BandTail { get; set; }
    private double BandCentre { get; set; }
    private double BandLead { get; set; }

    /// <summary>The band's colour, as CSS markup: what the cycle writes and the middle stop reads.</summary>
    private string BandColor { get; set; } = "rgba(0,0,0,0)";

    private Transition<TemplateLinkView>? _flow;
    private bool _flowRunning;
    private IWorkflowLinkViewModel? _flowLink;

    // The flow's three colours, as channels rather than markup so they can be mixed. Parsed once per
    // parameter change, not per frame.
    private (int A, int R, int G, int B) _dimColor = (0x9E, 0xFF, 0xFF, 0xFF);
    private (int A, int R, int G, int B) _litColor = (0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>The band's colour at full strength, as CSS, which the arrowhead also carries.</summary>
    private string LitCss => Css(_litColor);

    /// <summary>The line's resting colour, as CSS: the band's two shoulders, which the cycle never writes.</summary>
    private string DimCss => Css(_dimColor);

    /// <summary>Identifies this link's gradient; one definition per link, referenced by its stroke.</summary>
    private string FlowId => $"veloxdev-flow-{MarkerSuffix}";

    /// <summary>The settled link's stroke: the flow gradient. A virtual one keeps its flat dashed colour.</summary>
    private string FlowStroke => EffectiveIsVirtual ? LineColor : $"url(#{FlowId})";

    /// <summary>
    /// The flow, as the three phases it is made of, declared one after the other and repeated forever.
    /// </summary>
    /// <remarks>
    /// Built per component rather than held in a <c>static readonly</c> field, because two of its endpoints are the
    /// link's own colours, and a declaration that reads a local is shared by every later execution of it — here
    /// that would paint one link's band in another link's colour.
    /// <para>
    /// The paths go into the component itself: the three stop offsets and the band's colour, so a phase is a
    /// handful of named writes and the phase structure is readable rather than computed. A straight line rather
    /// than an eased curve, because the band should move at a constant speed — an ease would make each cycle pause
    /// at the ends and read as pulses instead of flow.
    /// </para>
    /// </remarks>
    private Transition<TemplateLinkView> BuildFlow() => Transition<TemplateLinkView>.Create()
        // Phase 1 — the band forms as it enters: it travels a third of the link while coming up from the
        // resting colour to the lit one.
        .Property(v => v.BandTail, BandFormed - BandHalfWidth)
        .Property(v => v.BandCentre, BandFormed)
        .Property(v => v.BandLead, BandFormed + BandHalfWidth)
        .Property(v => v.BandColor, LitCss)
        .Effect(Repainting(EnterDuration))
        .Then()
        // Phase 2 — it travels fully lit and unchanged, which is the phase that reads as flow rather than as a
        // pulse: nothing about it changes except where it is.
        .Property(v => v.BandTail, BandLeaving - BandHalfWidth)
        .Property(v => v.BandCentre, BandLeaving)
        .Property(v => v.BandLead, BandLeaving + BandHalfWidth)
        .Effect(Repainting(TravelDuration))
        .Then()
        // Phase 3 — it leaves, settling back to the resting colour over the last third of the travel. That is
        // also what makes the seam invisible when the cycle repeats: the line is uniformly dim at both ends of
        // a cycle, so the value snapping back to its captured start cannot be seen.
        .Property(v => v.BandTail, BandExit - BandHalfWidth)
        .Property(v => v.BandCentre, BandExit)
        .Property(v => v.BandLead, BandExit + BandHalfWidth)
        .Property(v => v.BandColor, DimCss)
        .Effect(Repainting(ExitDuration))
        .Repeat(int.MaxValue);

    /// <summary>
    /// One phase's effect: a straight line over <paramref name="duration"/>, with this component's repaint
    /// attached.
    /// </summary>
    /// <remarks>
    /// A repaint per frame is what this platform needs instead of a brush. In Avalonia the animated paths reach
    /// the rendered object and the framework notices; here the browser paints the gradient and the component only
    /// carries the values, so each frame has to be handed to the renderer by hand. <c>LateUpdate</c> rather than
    /// <c>Update</c>: it fires after the frame's writes have landed, so what renders is the frame the animation
    /// just wrote rather than the one before it.
    /// </remarks>
    private TransitionEffect Repainting(TimeSpan duration)
    {
        var effect = new TransitionEffect()
        {
            Duration = duration,
            Ease = Eases.Default,
        };

        effect.LateUpdate += (_, _) => InvokeAsync(StateHasChanged);
        return effect;
    }

    /// <summary>
    /// The gradient's axis, in the canvas coordinates the polyline is drawn in — the link's own endpoints,
    /// not its bounding box, so the band travels along the link rather than across a diagonal of its box.
    /// Re-derived per render, and it writes nothing the cycle owns: see <see cref="AimFlow"/>.
    /// </summary>
    private string[] FlowAxis => Link?.Sender is { } sender && Link.Receiver is { } receiver
        ?
        [
            N(sender.Anchor.Horizontal), N(sender.Anchor.Vertical),
            N(receiver.Anchor.Horizontal), N(receiver.Anchor.Vertical),
        ]
        : ["0", "0", "0", "0"];

    /// <summary>
    /// Starts the cycle from the sender's end on a settled link and stops it on one that is not: a virtual link
    /// is the rubber band under the pointer, and a band that streamed along it would claim a connection that does
    /// not exist yet. Also restarts when this view is handed a different link, which is what the render-ready gate
    /// makes routine — a pooled view is reused before its first link is ever measured.
    /// </summary>
    private void SyncFlow()
    {
        if (EffectiveIsVirtual || !EffectiveCanRender)
        {
            StopFlow();
            return;
        }

        if (_flowRunning && ReferenceEquals(_flowLink, Link)) return;

        _flowLink = Link;
        StartFlow();
    }

    /// <summary>
    /// Starts the cycle from the sender's end. Started when a view is handed a drawable link, so a pooled view
    /// reused for another link animates that link rather than the one it was built for.
    /// </summary>
    private void StartFlow()
    {
        if (EffectiveIsVirtual || !EffectiveCanRender)
        {
            StopFlow();
            return;
        }

        AimFlow();

        // The transition reads its start values from the target, so the component has to be at the cycle's start
        // before Execute — and the loop replays that captured start at every seam, so this is also the state each
        // later cycle begins from.
        BandTail = BandStart - BandHalfWidth;
        BandCentre = BandStart;
        BandLead = BandStart + BandHalfWidth;
        BandColor = DimCss;

        _flow!.Execute(this);
        _flowRunning = true;
    }

    /// <summary>
    /// Stops the cycle: a view that is no longer drawable, or one whose render is over, must not leave the old
    /// animation running on it.
    /// </summary>
    private void StopFlow()
    {
        if (!_flowRunning) return;

        // The cycle is started on the component itself, so this is also the call that unregisters it: from here on
        // no frame can reach the renderer through it.
        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _flowRunning = false;
        _flowLink = null;
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
    /// Recomputes the flow's two colours from the link's own, and rebuilds the declaration whose endpoints they
    /// are. Called whenever the view is handed its link, its parameters or its colour — the last of which is also
    /// the only thing that can change what the declaration captures.
    /// </summary>
    /// <remarks>
    /// Nothing here writes the band's position. The cycle owns those and writes them every frame from the endpoints
    /// it captured, so re-seating them from a path that runs whenever the link moves — which is every frame of a
    /// drag, since the anchors change under the band — would fight it for a frame. That reads as a band that
    /// stutters while the canvas moves, and there is no code path here that could reset it.
    /// </remarks>
    private void AimFlow()
    {
        var lit = LitOf(ParseColor(LineColorOverride ?? "#DDFFFFFF"));
        if (_flow is not null && lit == _litColor)
        {
            return;
        }

        _litColor = lit;
        _dimColor = DimOf(lit);
        _flow = BuildFlow();

        // A view recycled onto a link of another colour gets its cycle restarted, from its own colour's starting
        // state rather than the previous link's.
        if (_flowRunning)
        {
            StartFlow();
        }
    }

    /// <summary>
    /// The band's colour: the link's own colour at full strength, lifted a little so a link that is already
    /// white still has somewhere brighter to go.
    /// </summary>
    private static (int A, int R, int G, int B) LitOf((int A, int R, int G, int B) color)
    {
        const double lift = 0.45;

        int Up(int channel) => (int)Math.Round(channel + (255 - channel) * lift);

        return (0xFF, Up(color.R), Up(color.G), Up(color.B));
    }

    /// <summary>
    /// The line's resting colour: the lit colour dimmed to a little under two thirds, which is what makes a
    /// lit band read as a band.
    /// </summary>
    /// <remarks>
    /// Dimming by alpha is what keeps the hue. The alternative that suggests itself — a "highlight" that is
    /// the line colour pushed <em>towards white</em> — has been tried in this demo suite and is invisible:
    /// on a 2px line against a dark canvas, a cyan link lifted 75% towards white differs from cyan in one
    /// channel out of three.
    /// </remarks>
    private static (int A, int R, int G, int B) DimOf((int A, int R, int G, int B) color)
        => ((int)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    /// <summary>
    /// Writes a colour as CSS markup. The alpha goes out invariantly for the same reason the offsets do — and here
    /// it is more than an attribute: this string is an endpoint of the cycle, so a comma decimal separator would
    /// not merely garble the rendered stop but leave the band's colour unparseable to the sampler that mixes it.
    /// </summary>
    private static string Css((int A, int R, int G, int B) color) => color.A >= 0xFF
        ? $"rgb({color.R},{color.G},{color.B})"
        : string.Create(CultureInfo.InvariantCulture,
            $"rgba({color.R},{color.G},{color.B},{color.A / 255d:0.###})");

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    #endregion

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Sync(Link);

        // The declaration's endpoints are the link's own colours, so the colours come first: the cycle is built
        // from them, and a virtual link that never starts a cycle still needs them for the arrowhead.
        AimFlow();
        SyncFlow();
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
        AimFlow();
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
        // streaming would keep calling StateHasChanged into a renderer that has moved on. The cycle is started
        // on the component itself, so stopping it is also what unregisters it.
        StopFlow();

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
