// VeloxDev customization: Customize line geometry, color, and thickness here.
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; a drawing
// alias keeps `new Size(...)` and `RectangleF(...)` usage unambiguous.
using Size = System.Drawing.Size;

namespace Demo.Views;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs, carrying a travelling highlight so the
/// direction of data flow is readable at a glance.
/// Passive visual only — no hover, highlight, or keyboard interaction.
/// </summary>
public sealed class LinkView : Control
{
    private const double Phi = 0.6180339887;

    private IWorkflowLinkViewModel? _link;
    private INotifyPropertyChanged? _notifier;
    private INotifyPropertyChanged? _senderNotifier;
    private INotifyPropertyChanged? _receiverNotifier;

    private float _startLeft;
    private float _startTop;
    private float _endLeft;
    private float _endTop;
    private bool _canRender = true;
    private bool _isVirtual;
    private Color _lineColor = ParseColor("#DDFFFFFF");

    // The band's cycle, shared with every other link the canvas draws. Held rather than owned: the clock
    // belongs to the canvas (see WorkflowCanvas's flow notes), and this view only reads the place in the
    // cycle that the clock is at.
    private LinkFlow? _flow;

    // This link's own gradient: the axis is its two endpoints and the stops are the band. Held for as long as
    // its axis is, because GDI+ takes a gradient's endpoints in its constructor and offers no way to re-aim
    // them afterwards — a link that moves gets a rebuilt brush, a band that moves does not (MoveBand).
    private LinearGradientBrush? _flowBrush;

    // The blend handed to that brush, kept here rather than fetched from it: InterpolationColors' getter
    // returns a copy, so a stop written into the returned object is written into nothing (measured — see
    // MoveBand). Five slots rather than the band's three, because GDI+ refuses a blend that does not span
    // the whole axis — the two extra stops are the link's resting colour again, at either end.
    private readonly ColorBlend _band = new(5);

    // The two colours the band is mixed from, derived from this link's own colour. The reference keeps them
    // on its LinkFlow; they belong to the link here, because two links need not be the same colour.
    private Color _lit;
    private Color _dim;

    public LinkView()
    {
        // Passive overlay: no hit-testing, sits behind the nodes. The link is
        // rendered by the host canvas (Render) rather than as a child window, and
        // uses an opaque background so it never participates in WinForms' fragile
        // transparent compositing.
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
        // No BackColor assignment: the link is rendered by the host canvas (Render)
        // and never shown as a child window, so it keeps the default opaque
        // background (a translucent BackColor would throw in .NET 10).
        TabStop = false;
        Enabled = false;

        // Seed the two colours the band is mixed from, so a link drawn before a canvas hands it a flow is
        // still drawn in the pair this effect is built on (the arrowhead uses the lit one).
        UpdateFlowBrush();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float StartLeft { get => _startLeft; set { _startLeft = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float StartTop { get => _startTop; set { _startTop = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float EndLeft { get => _endLeft; set { _endLeft = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float EndTop { get => _endTop; set { _endTop = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CanRender { get => _canRender; set { _canRender = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsVirtual { get => _isVirtual; set { _isVirtual = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color LineColor
    {
        get => _lineColor;
        set
        {
            _lineColor = value;
            // The band is mixed from this colour, so the gradient is rebuilt with it — the brush holds both
            // the axis and the two colours, and neither can be changed on a brush GDI+ has already made.
            UpdateFlowBrush();
            RequestPaint();
        }
    }

    /// <summary>
    /// Optional callback invoked instead of <see cref="Control.Invalidate"/> when link
    /// geometry or visibility changes. A surface that renders this link inside its own
    /// <c>OnPaint</c> (rather than as a child control) sets this to invalidate the host
    /// canvas, keeping the connection live while dragging.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action? ExternalInvalidate { get; set; }

    private void RequestPaint()
    {
        if (ExternalInvalidate is { } external) external();
        else Invalidate();
    }

    /// <summary>
    /// The band's cycle, shared with every other link on the canvas. The canvas sets it when it builds the
    /// renderer; a link without one is drawn flat, which is also the state of a view built by hand.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal LinkFlow? Flow
    {
        get => _flow;
        set
        {
            if (ReferenceEquals(_flow, value)) return;

            _flow = value;
            // The gradient exists only for a flow: it is what the band is painted with, and the resting
            // colour it holds is the flow's other half.
            UpdateFlowBrush();
            RequestPaint();
        }
    }

    /// <summary>Wires a link model so anchor/visibility changes repaint this view.</summary>
    public void Bind(IWorkflowLinkViewModel? link)
    {
        if (ReferenceEquals(_link, link))
        {
            Sync(link);
            return;
        }

        UnsubscribeEndpoints();

        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnLinkChanged;
            _notifier = null;
        }

        _link = link;
        Tag = link;

        if (link is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnLinkChanged;
        }

        SubscribeEndpoints();
        Sync(link);
    }

    private void SubscribeEndpoints()
    {
        if (_link?.Sender is INotifyPropertyChanged s)
        {
            _senderNotifier = s;
            s.PropertyChanged += OnEndpointChanged;
        }

        if (_link?.Receiver is INotifyPropertyChanged r)
        {
            _receiverNotifier = r;
            r.PropertyChanged += OnEndpointChanged;
        }
    }

    private void UnsubscribeEndpoints()
    {
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

    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new PropertyChangedEventHandler(OnEndpointChanged), sender, e);
            return;
        }

        SyncEndpoints();
    }

    /// <summary>
    /// View-model accessor honored by <see cref="ViewManager"/> when a pooled
    /// view is recycled. Setting it re-binds this view to the new link.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowLinkViewModel? ViewModel
    {
        get => _link;
        set => Bind(value);
    }

    private void OnLinkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new PropertyChangedEventHandler(OnLinkChanged), sender, e);
            return;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible) or null or "")
        {
            CanRender = _link?.IsVisible == true;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.Sender)
            or nameof(IWorkflowLinkViewModel.Receiver)
            or null or "")
        {
            SyncEndpoints();
        }
    }

    private void SyncEndpoints()
    {
        if (_link is null) return;

        var sender = _link.Sender;
        var receiver = _link.Receiver;
        if (sender is not null)
        {
            StartLeft = (float)sender.Anchor.Horizontal;
            StartTop = (float)sender.Anchor.Vertical;
        }

        if (receiver is not null)
        {
            EndLeft = (float)receiver.Anchor.Horizontal;
            EndTop = (float)receiver.Anchor.Vertical;
        }

        IsVirtual = IsVirtualLink(_link);

        // The one place the gradient is re-aimed: every anchor the canvas writes back arrives through here
        // (the four anchor setters are only ever written from this method and from Bind), so this is the
        // WinForms equivalent of the reference's per-property check on its four endpoint properties.
        UpdateFlowBrush();
        RequestPaint();
    }

    public void Sync(IWorkflowLinkViewModel? link)
    {
        if (link is null) return;

        CanRender = link.IsVisible;
        SyncEndpoints();
    }

    private bool IsVirtualLink(IWorkflowLinkViewModel link)
        => link.Sender?.Parent is null && link.Receiver?.Parent is null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeEndpoints();
            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnLinkChanged;
                _notifier = null;
            }

            // A GDI+ brush is not collected on its own, and this one is rebuilt on every anchor write while a
            // node is dragged.
            _flowBrush?.Dispose();
            _flowBrush = null;
        }

        base.Dispose(disposing);
    }

    // ── Flow effect ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Orients the gradient along the link and gives the band its two colours — everything about the effect
    /// that is per link rather than per frame: the axis is the link's own endpoints and the two colours are
    /// mixed from the link's own colour.
    /// </summary>
    /// <remarks>
    /// The brush is rebuilt rather than re-aimed, which is the one piece of the reference this port cannot
    /// keep. Avalonia's <c>LinearGradientBrush</c> takes its two points as properties and the reference
    /// re-writes them; GDI+ takes them as constructor arguments and has no settable equivalent, so a link
    /// that moved would keep painting the axis it was born with. What is kept across frames is the
    /// <see cref="ColorBlend"/> — the band's own stops — since that is the part written every frame (see
    /// <see cref="MoveBand"/>). Rebuilt from here rather than from <see cref="Render"/> because the axis only
    /// changes when the canvas writes an anchor back, and both writers of an anchor funnel through
    /// <see cref="SyncEndpoints"/>.
    /// </remarks>
    private void UpdateFlowBrush()
    {
        _lit = LitOf(_lineColor);
        _dim = DimOf(_lit);

        _flowBrush?.Dispose();
        _flowBrush = null;

        if (_flow is null) return;

        var from = new PointF(_startLeft, _startTop);
        var to = new PointF(_endLeft, _endTop);

        // A link is drawn only once both of its anchors have been measured, and before that they are NaN —
        // which GDI+ refuses outright rather than drawing nothing — and, in a view built by hand, the
        // origin twice. A gradient between two identical points has no axis for a band to travel along, so
        // the flat pen is the honest answer until there is one.
        if (!IsFinite(from) || !IsFinite(to)) return;
        if (Math.Abs(to.X - from.X) < 0.5f && Math.Abs(to.Y - from.Y) < 0.5f) return;

        // The wrap mode is left at the brush's own default (Tile). The reference asks for Pad, and this is
        // where GDI+ simply does not offer it: WrapMode.Clamp — the nearest thing to Pad — throws
        // ArgumentException on a linear gradient brush, measured, so it cannot be mirrored. It also cannot
        // be missed: a stroke runs from the axis's first point to its last and never samples past them, and
        // the last stop is the same resting colour the first one is (see MoveBand), so an out-of-range
        // sample has nothing to differ from.
        _flowBrush = new LinearGradientBrush(from, to, _dim, _dim);

        MoveBand();
    }

    /// <summary>
    /// Places the band for the point in the cycle the shared flow is at, and hands back the brush it was
    /// placed in — the two things <see cref="Render"/> needs from the effect, in that order.
    /// </summary>
    /// <remarks>
    /// The band is written as five stops where the reference writes three, and that is forced rather than
    /// chosen: GDI+ refuses <c>SetPresetBlend</c> unless the first stop is at 0 and the last at 1. Measured
    /// — a blend at 0.25/0.50/0.75 is refused, 0.25/0.50/1.00 is refused, 0.00/0.50/0.75 is refused, and
    /// only a blend spanning the whole axis is accepted. Since the band's stops sit between 0.02 and 0.98
    /// of the axis by construction, the reference's three cannot be written at all; the two extra stops are
    /// the resting colour again at either end, which is the same picture Pad would have produced — outside
    /// the band the gradient is flat at the resting colour, so the lit length of a link is the band and not
    /// a row of them.
    /// <para>
    /// Two further details are GDI+ rather than the reference, and both were measured rather than assumed.
    /// The blend is a field that is re-assigned: <c>InterpolationColors</c>' getter hands back a fresh
    /// <see cref="ColorBlend"/>, so stops written into the object it returned are written into nothing — a
    /// render of a brush whose returned copy had been mutated is pixel-for-pixel identical to one before
    /// the mutation, while re-assigning that same object moves the band. And the pen that strokes with this
    /// brush is built in <see cref="Render"/> after this call and never held across one: a pen built from a
    /// brush keeps the stops that brush had when the pen was made, likewise measured, so a pen kept from the
    /// previous frame would pin the band to its last position and the link would look static while the
    /// numbers under it moved.
    /// </para>
    /// </remarks>
    private LinearGradientBrush MoveBand()
    {
        var flow = _flow!;
        var brush = _flowBrush!;

        _band.Colors![0] = _dim;
        _band.Colors[1] = _dim;
        _band.Colors[2] = LinkFlow.Blend(_dim, _lit, flow.Mix);
        _band.Colors[3] = _dim;
        _band.Colors[4] = _dim;
        _band.Positions![0] = 0f;
        _band.Positions[1] = (float)flow.Trailing;
        _band.Positions[2] = (float)flow.Centre;
        _band.Positions[3] = (float)flow.Leading;
        _band.Positions[4] = 1f;

        brush.InterpolationColors = _band;
        return brush;
    }

    private static bool IsFinite(PointF p) => float.IsFinite(p.X) && float.IsFinite(p.Y);

    /// <summary>
    /// The band's colour: the link's own colour at full strength, lifted a little towards white so a link
    /// that is already white still has somewhere brighter to go (this demo's links are white at 87%, so
    /// there the lift is what removes the translucency).
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
    /// the line colour pushed <em>towards white</em> — is what this effect started as, and it is invisible.
    /// The Avalonia demo's links are cyan, and cyan lifted 75% towards white differs from cyan in one channel
    /// out of three, on a 2px line, against a dark canvas; this demo's are white, where pushing towards
    /// white is nothing at all. Making the resting line the dim one puts the contrast where the eye can find
    /// it at a glance, and it is the half of the difference that survives on any hue.
    /// </remarks>
    private static Color DimOf(Color color) => Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics);
    }

    /// <summary>
    /// Draws the link geometry onto an arbitrary <see cref="Graphics"/> surface. In OnPaint the
    /// canvas first writes back all slot anchors, then calls this method uniformly in world
    /// coordinates after TranslateTransform(origin) — links are no longer separate transparent
    /// overlay child controls, avoiding the WinForms issue where overlapping full-size sibling
    /// windows are clipped by WS_CLIPSIBLINGS (only the topmost is drawn) and links become
    /// invisible. When used as a standalone control, OnPaint takes the same path.
    /// </summary>
    public void Render(Graphics g)
    {
        if (!_canRender) return;

        // NaN gate (mirrors WorkflowLinkRenderEx.IsRenderReady in the XAML adapters): slot
        // anchors default to NaN until the canvas measures them, so a real link must not paint
        // a stale frame at NaN/origin before measurement lands. Placeholder endpoints (Parent
        // is null, e.g. the VirtualLink gesture) are exempt and render immediately.
        if (_link is not null && !WorkflowSlotUpdateGate.IsLinkRenderReady(_link)) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var points = BuildPoints();
        if (points.Length < 2) return;

        // The travelling highlight is only meaningful on a settled connection. A virtual link is the rubber
        // band under the pointer, and a link with no gradient yet is one the canvas has not measured — both
        // keep the flat pen this demo drew every link with before. This view draws no other kind (it is
        // passive: no hover and no selection), so those two conditions are the whole of what the reference
        // also asks of IsSelected and CanRender.
        //
        // The pen is built per frame from the brush the band was just written into, rather than kept: see
        // MoveBand for the measurement behind that. The brush itself is not disposed with the pen — it is a
        // field, and a pen does not own the brush it was built from.
        using var pen = _isVirtual || _flowBrush is null
            ? new Pen(_lineColor, float.Parse("2", CultureInfo.InvariantCulture))
            : new Pen(MoveBand(), float.Parse("2", CultureInfo.InvariantCulture));
        if (_isVirtual)
        {
            pen.DashStyle = DashStyle.Dash;
            pen.DashPattern = [4f, 2f];
        }

        g.DrawLines(pen, points);

        if (!_isVirtual)
        {
            DrawArrowhead(g, points[^2], points[^1]);
        }
    }

    private PointF[] BuildPoints()
    {
        var s = new PointF(_startLeft, _startTop);
        var e = new PointF(_endLeft, _endTop);
        double dx = _endLeft - _startLeft;
        double stub = dx / 2.0 * (1.0 - Phi);
        var p1 = new PointF(s.X + (float)stub, s.Y);
        var p4 = new PointF(e.X - (float)stub, e.Y);
        return [s, p1, p4, e];
    }

    private void DrawArrowhead(Graphics g, PointF from, PointF tip)
    {
        float tx = tip.X - from.X;
        float ty = tip.Y - from.Y;
        float len = (float)Math.Sqrt(tx * tx + ty * ty);
        if (len < 0.03f) return;

        // Unit vector along the last segment, plus its perpendicular.
        float ux = tx / len;
        float uy = ty / len;
        const float al = 12f, aw = 8f;
        float bx = tip.X - ux * al;
        float by = tip.Y - uy * al;
        float px = -uy, py = ux;

        var pts = new[]
        {
            tip,
            new PointF(bx + px * (aw / 2f), by + py * (aw / 2f)),
            new PointF(bx - px * (aw / 2f), by - py * (aw / 2f)),
        };

        // The arrowhead is the destination marker, so it carries the band's colour rather than the gradient:
        // the line rests dim, and an arrowhead dimmed with it would be the one part of the link that never
        // lights up.
        using var brush = new SolidBrush(_lit);
        g.FillPolygon(brush, pts);
    }

    private static Color ParseColor(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var digits = value.Substring(1);
            if (digits.Length == 8)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            if (digits.Length == 6)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        return Color.FromName(value);
    }
}

/// <summary>
/// The band's place in its cycle, as the whole of the animated state: cycle progress goes in, the band's
/// three stop offsets and the colour mix come out.
/// </summary>
/// <remarks>
/// The object the flow animation writes into. <c>Transition&lt;T&gt;</c> animates a member of a reference
/// type, so this is held rather than the view: the animated path is <see cref="Phase"/>, and its setter is
/// what turns that one number into the band.
/// <para>
/// One instance serves every link a canvas draws, so all the bands move together on one clock — see
/// <c>WorkflowCanvas</c> for why the clock is the canvas's rather than each link's. The line is drawn dim
/// and the band is the same colour at full strength, so what travels is a lit length of the link rather
/// than a different colour painted on it. The three offsets ARE the band: the middle one carries the lit
/// colour and the other two sit <see cref="HalfWidth"/> either side of it, which is what keeps it a band
/// instead of one wide smear along the whole line. (The brush that paints it carries five stops, not three:
/// the two extra are the resting colour at either end of the axis, which GDI+ requires and which change
/// nothing — see <see cref="LinkView"/>'s <c>MoveBand</c>.)
/// </para>
/// </remarks>
internal sealed class LinkFlow
{
    /// <summary>Half the band's width, in gradient-offset units.</summary>
    private const double HalfWidth = 0.04;

    // One cycle, as fractions of it. The phases have different lengths because they cover different
    // distances: the band travels a third of the link while forming, a third while fully lit, and a third
    // while leaving.
    private const double EnterEnd = 0.30;
    private const double FadeStart = 0.66;
    private const double BandFrom = 0.06;
    private const double BandFormed = 0.34;
    private const double BandLeaving = 0.66;
    private const double BandTo = 0.94;

    /// <summary>
    /// Raised by every write, which is how a frame reaches the screen: the canvas that owns this flow
    /// repaints its link surface from here.
    /// </summary>
    public Action? Changed { get; set; }

    private double _phase;

    /// <summary>
    /// Cycle progress, 0→1: the whole of the animated state. Writing it places the band, and the animation
    /// writes it every frame.
    /// </summary>
    public double Phase
    {
        get => _phase;
        set { _phase = value; Apply(); }
    }

    /// <summary>Where the band's middle stop sits, in gradient-offset units along the link.</summary>
    public double Centre { get; private set; }

    /// <summary>Where the band's trailing stop sits — behind the middle, towards the sender.</summary>
    public double Trailing { get; private set; }

    /// <summary>Where the band's leading stop sits — ahead of the middle, towards the receiver.</summary>
    public double Leading { get; private set; }

    /// <summary>
    /// How much of the band's colour is the lit one: 0 rests on the line's own colour and 1 is fully lit.
    /// Each link mixes its own two colours from it.
    /// </summary>
    public double Mix { get; private set; }

    /// <summary>
    /// Places the band for the current phase — the three phases the cycle is made of, as one piecewise
    /// mapping.
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

        Centre = centre;
        Trailing = centre - HalfWidth;
        Leading = centre + HalfWidth;
        Mix = mix;

        // The reference writes the stops into the brush it draws with, and the framework repaints the control
        // from that write. GDI+ has no such link between a brush and a frame — writing a brush moves nothing
        // until something paints it again — so the repaint is asked for here instead, and it is the same idea
        // either way: the write of the animated value is what produces the next frame.
        Changed?.Invoke();
    }

    /// <summary>Linear mix of two colours, alpha included.</summary>
    public static Color Blend(Color from, Color to, double t) => Color.FromArgb(
        (byte)Math.Round(from.A + (to.A - from.A) * t),
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));
}
