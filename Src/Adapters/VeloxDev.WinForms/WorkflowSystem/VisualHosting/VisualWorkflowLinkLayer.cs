using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using DrawingPointF = System.Drawing.PointF;
using DrawingSize = System.Drawing.Size;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// The single visual element that paints every visible link and publishes the exact curve it drew for Core's hit
/// testing.
/// </summary>
/// <remarks>
/// The layer is the canvas' first child and never takes a pointer hit; links are resolved from their published
/// curves instead of from a per-link control. Curves are built in the canvas-local collapsed model space and only
/// offset by the canvas pan when painted, so the published curve and the drawn one describe the same shape.
/// </remarks>
public sealed class VisualWorkflowLinkLayer : VisualElement
{
    private const double PullMinimum = 40d;

    private readonly VisualWorkflowCanvas _canvas;
    private readonly List<Entry> _entries = [];
    private DrawingPointF _pan;
    private ILinkComponent? _hovered;
    private ITreeComponent? _tree;

    private double _flowHead;
    private double _flowIntensity;

    internal VisualWorkflowLinkLayer(VisualWorkflowCanvas canvas)
    {
        _canvas = canvas;
        IsHitTestVisible = false;
        LineColor = Color.FromArgb(0xCC, 0x6E, 0x8B, 0xFF);
        HoverLineColor = Color.White;
        FlowColor = Color.FromArgb(0xE6, 0xE7, 0xE7, 0xE7);
    }

    /// <summary>The colour of an idle link.</summary>
    public Color LineColor { get; set; }

    /// <summary>The colour of the hovered link.</summary>
    public Color HoverLineColor { get; set; }

    /// <summary>The width of an idle link, in pixels.</summary>
    public float LineWidth { get; set; } = 2f;

    /// <summary>The width of the hovered link, in pixels.</summary>
    public float HoverLineWidth { get; set; } = 3.5f;

    /// <summary>The colour of the travelling flow marker.</summary>
    public Color FlowColor { get; set; }

    /// <summary>Whether a soft glow is painted behind every link.</summary>
    public bool Glow { get; set; } = true;

    /// <summary>The link currently under the pointer, or <see langword="null"/>.</summary>
    public ILinkComponent? Hovered
    {
        get => _hovered;
        set
        {
            if (ReferenceEquals(_hovered, value))
            {
                return;
            }

            _hovered = value;
            InvalidateVisual();
        }
    }

    internal DrawingPointF Pan
    {
        get => _pan;
        set
        {
            if (_pan == value)
            {
                return;
            }

            _pan = value;
            InvalidateVisual();
        }
    }

    /// <summary>Rebuilds the drawn geometry and republishes every curve.</summary>
    public void Refresh()
    {
        _entries.Clear();
        if (_tree is null)
        {
            return;
        }

        foreach (var link in _tree.Links)
        {
            AddEntry(link, publish: true);
        }

        AddEntry(_tree.VirtualLink, publish: false);
        InvalidateVisual();
    }

    /// <summary>Moves the travelling flow marker.</summary>
    /// <param name="head">How far along each link the marker has travelled, from zero to one.</param>
    /// <param name="intensity">How visible the marker is, from zero to one.</param>
    public void SetFlow(double head, double intensity)
    {
        _flowHead = head;
        _flowIntensity = intensity;
        InvalidateVisual();
    }

    /// <summary>Requests a repaint without rebuilding the geometry; used by a host's per-frame animation.</summary>
    public void InvalidateLayer() => InvalidateVisual();

    internal void Attach(ITreeComponent tree)
    {
        _tree = tree;
        Refresh();
    }

    internal void Detach()
    {
        if (_tree is not null)
        {
            foreach (var link in _tree.Links)
            {
                link.PublishCurve(null);
            }
        }

        _entries.Clear();
        _tree = null;
        _hovered = null;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override DrawingSize MeasureOverride(DrawingSize availableSize) => availableSize;

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var entry in _entries)
        {
            var hovered = ReferenceEquals(entry.Link, _hovered);
            var color = hovered ? HoverLineColor : entry.IsVirtual ? FlowColor : LineColor;
            var width = hovered ? HoverLineWidth : LineWidth;

            if (Glow && !entry.IsVirtual)
            {
                using var glow = new Pen(Color.FromArgb(hovered ? 70 : 32, color), width + 4f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                DrawBezier(graphics, glow, entry.Points);
            }

            using var pen = new Pen(color, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                DashStyle = entry.IsVirtual ? DashStyle.Dash : DashStyle.Solid,
            };
            DrawBezier(graphics, pen, entry.Points);

            if (!entry.IsVirtual && _flowIntensity > 0.001d)
            {
                DrawFlow(graphics, entry);
            }
        }
    }

    private void DrawFlow(Graphics graphics, Entry entry)
    {
        if (entry.Curve.Length <= 0d)
        {
            return;
        }

        var (x, y) = entry.Curve.PointAtLength(entry.Curve.Length * Clamp(_flowHead, 0d, 1d));
        var size = Math.Max(2f, LineWidth + 2f);
        using var glow = new SolidBrush(Color.FromArgb((int)Math.Round(90 * _flowIntensity), FlowColor));
        using var core = new SolidBrush(Color.FromArgb((int)Math.Round(230 * _flowIntensity), FlowColor));
        graphics.FillEllipse(
            glow,
            (float)x + _pan.X - size * 2f,
            (float)y + _pan.Y - size * 2f,
            size * 4f,
            size * 4f);
        graphics.FillEllipse(
            core,
            (float)x + _pan.X - size,
            (float)y + _pan.Y - size,
            size * 2f,
            size * 2f);
    }

    private void DrawBezier(Graphics graphics, Pen pen, (double X, double Y)[] points)
    {
        graphics.DrawBezier(
            pen,
            (float)points[0].X + _pan.X,
            (float)points[0].Y + _pan.Y,
            (float)points[1].X + _pan.X,
            (float)points[1].Y + _pan.Y,
            (float)points[2].X + _pan.X,
            (float)points[2].Y + _pan.Y,
            (float)points[3].X + _pan.X,
            (float)points[3].Y + _pan.Y);
    }

    private void AddEntry(ILinkComponent? link, bool publish)
    {
        if (link is null
            || !link.IsVisible
            || link.Sender is not { } sender
            || link.Receiver is not { } receiver)
        {
            if (publish && link is not null)
            {
                link.PublishCurve(null);
            }

            return;
        }

        var start = sender.Anchor;
        var end = receiver.Anchor;
        if (!IsFinite(start) || !IsFinite(end))
        {
            if (publish)
            {
                link.PublishCurve(null);
            }

            return;
        }

        var points = LinkCurve.LinkCurvePoints(
            link, start.Horizontal, start.Vertical, end.Horizontal, end.Vertical, PullMinimum);
        var curve = LinkCurve.BuildLinkCubic(
            link, start.Horizontal, start.Vertical, end.Horizontal, end.Vertical, PullMinimum);

        if (publish)
        {
            link.PublishCurve(curve, this);
        }

        _entries.Add(new Entry(link, points, curve, !publish));
    }

    private static bool IsFinite(Wf.Anchor anchor)
        => !double.IsNaN(anchor.Horizontal)
           && !double.IsNaN(anchor.Vertical)
           && !double.IsInfinity(anchor.Horizontal)
           && !double.IsInfinity(anchor.Vertical);

    private static double Clamp(double value, double minimum, double maximum)
        => value < minimum ? minimum : value > maximum ? maximum : value;

    private sealed class Entry(ILinkComponent link, (double X, double Y)[] points, LinkCurve curve, bool isVirtual)
    {
        internal ILinkComponent Link { get; } = link;
        internal (double X, double Y)[] Points { get; } = points;
        internal LinkCurve Curve { get; } = curve;
        internal bool IsVirtual { get; } = isVirtual;
    }
}
