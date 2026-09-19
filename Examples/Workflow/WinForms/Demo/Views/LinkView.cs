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

    // The band's place in the cycle, for the frame that is about to be drawn: where its middle stop sits and
    // how much of its colour is the lit one. Pushed in before every Render (SetFlow) rather than owned here:
    // the clock belongs to the surface, because a renderer the canvas holds in a list has no window of its
    // own to be invalidated (see WorkflowCanvas's flow notes).
    private double _bandCentre;
    private double _bandMix;

    // This link's own gradient: the axis is its two endpoints and the stops are the band. Held for as long as
    // its axis is, because GDI+ takes a gradient's endpoints in its constructor and offers no way to re-aim
    // them afterwards — a link that moves gets a rebuilt brush, a band that moves does not (MoveBand).
    private LinearGradientBrush? _flowBrush;

    // The blend handed to that brush, kept here rather than fetched from it: InterpolationColors' getter
    // returns a copy, so a stop written into the returned object is written into nothing (measured — see
    // MoveBand). Five slots rather than the band's three, because GDI+ refuses a blend that does not span
    // the whole axis — the two extra stops are the link's resting colour again, at either end.
    private readonly ColorBlend _band = new(5);

    // The two colours the band is mixed from, derived from this link's own colour — per link rather than per
    // surface, because two links need not be the same colour.
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

        // Seed the two colours the band is mixed from: the gradient is only built once the link has an axis,
        // but the arrowhead is drawn from the lit colour from the first frame, and a view built by hand is
        // never pushed a frame's numbers at all.
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

    /// <summary>Half the band's width, in gradient-offset units.</summary>
    private const double HalfWidth = 0.04;

    /// <summary>
    /// The band's place for the frame that is about to be drawn: where its middle stop sits along the link,
    /// and how much of its colour is the lit one. Pushed in by <c>WorkflowCanvas</c> before each
    /// <see cref="Render"/>, from the two numbers its clock writes.
    /// </summary>
    /// <remarks>
    /// Two numbers rather than the four the reference animates (its band's three stop offsets and its
    /// colour), because these are the two the other two are derived from and the same pair serves every
    /// renderer on the surface. The band's shoulders are <see cref="HalfWidth"/> either side of the centre
    /// and its colour is the mix of this link's own pair (see <see cref="MoveBand"/>), so nothing per link
    /// has to cross this boundary — which is what lets one pair of values be handed to every link, including
    /// links of different colours.
    /// </remarks>
    public void SetFlow(double centre, double mix)
    {
        _bandCentre = centre;
        _bandMix = mix;
    }

    /// <summary>
    /// Orients the gradient along the link and gives the band its two colours — everything about the effect
    /// that is per link rather than per frame: the axis is the link's own endpoints and the two colours are
    /// mixed from the link's own colour. The band's position is not among them; that is the clock's, and
    /// arrives per frame through <see cref="SetFlow"/>.
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
    /// <para>
    /// The gradient is built for every link, not only for one a canvas animates: its axis and its colours
    /// are the link's own, and the clock is not an input to either. The two checks below are therefore the
    /// only thing that can leave a link without a brush — which is what the guard in <see cref="Render"/>
    /// tests for.
    /// </para>
    /// </remarks>
    private void UpdateFlowBrush()
    {
        _lit = LitOf(_lineColor);
        _dim = DimOf(_lit);

        _flowBrush?.Dispose();
        _flowBrush = null;

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
    /// Places the band where the clock last put it — the two numbers <see cref="SetFlow"/> was handed — and
    /// hands back the brush it was placed in: the two things <see cref="Render"/> needs from the effect, in
    /// that order.
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
        var brush = _flowBrush!;

        // The band's two shoulders are not animated: the clock writes where the middle stop is, and they are
        // HalfWidth either side of it, which is what keeps this a band instead of one wide smear of lit
        // colour down the whole link.
        var trailing = _bandCentre - HalfWidth;
        var leading = _bandCentre + HalfWidth;

        _band.Colors![0] = _dim;
        _band.Colors[1] = _dim;
        _band.Colors[2] = Blend(_dim, _lit, _bandMix);
        _band.Colors[3] = _dim;
        _band.Colors[4] = _dim;
        _band.Positions![0] = 0f;
        _band.Positions[1] = (float)trailing;
        _band.Positions[2] = (float)_bandCentre;
        _band.Positions[3] = (float)leading;
        _band.Positions[4] = 1f;

        brush.InterpolationColors = _band;
        return brush;
    }

    /// <summary>
    /// Linear mix of two colours, alpha included: the band's colour at the mix the cycle is at, where zero
    /// rests on the line's own colour and one is fully lit.
    /// </summary>
    private static Color Blend(Color from, Color to, double t) => Color.FromArgb(
        (byte)Math.Round(from.A + (to.A - from.A) * t),
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));

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
        // band under the pointer; a link with no gradient is one whose axis is not there yet, which is the
        // one case UpdateFlowBrush leaves without a brush (its endpoints are not finite, or both of them
        // land on the same point). Both keep the flat pen this demo drew every link with before — this view
        // draws no other kind (it is passive: no hover and no selection), so those two conditions are the
        // whole of what the reference also asks of IsSelected and CanRender.
        //
        // The second of them used to read "no flow was attached", and it is not that any more: the gradient
        // is built from the link's own geometry and colour, and the clock that moves the band is not an
        // input to the brush at all. What can still leave a view without one is the geometry — and the
        // check is load-bearing rather than tidy, because MoveBand writes into the brush: with no axis
        // there is nothing to write into, and nothing to draw a band along either. (A view nothing has
        // pushed numbers into is drawn in the resting colour throughout, since a mix of zero makes the whole
        // gradient the resting colour — the flat line it drew before.)
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
