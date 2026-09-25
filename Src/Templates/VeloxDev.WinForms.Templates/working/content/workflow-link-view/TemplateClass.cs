// VeloxDev customization: Customize line geometry, color, and thickness here. The window
// region follows the stroke, so keep it in step when you change the polyline or the thickness.
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
// Alias Size so System.Drawing.Size doesn't clash with VeloxDev.WorkflowSystem.Size.
using Size = System.Drawing.Size;

namespace TemplateNamespace;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs. Materialized and recycled by
/// <see cref="ViewPool"/> (one view per visible link, the drag preview included) and paints
/// itself — passive visual only, no hover, highlight, or keyboard interaction.
/// </summary>
/// <remarks>
/// <para>A WinForms child window is opaque and cannot composite over its siblings, so the
/// window region is carved to the stroke band of the polyline instead of a bounding box: the
/// grid behind stays visible around the line and only the line area can ever cover the canvas.
/// Keep <see cref="BackColor"/> equal to the surface's grid background, or the carved band
/// becomes a visible seam.</para>
/// <para>The view sits behind the node cards. Re-ordering pooled views is the surface's job
/// (the pool fronts every view it materializes) — see the tree view's link-layer arrangement.</para>
/// </remarks>
public sealed class TemplateClass : Control
{
    private const double Phi = 0.6180339887;

    // Extra width the region gets on each side of the stroke, so the antialiased edge of the
    // line is not clipped by the region boundary.
    private const float RegionPad = 1.5f;

    private IWorkflowLinkViewModel? _link;
    private INotifyPropertyChanged? _notifier;
    private INotifyPropertyChanged? _senderNotifier;
    private INotifyPropertyChanged? _receiverNotifier;

    private bool _canRender = true;
    private bool _isVirtual;
    private Color _lineColor = ParseColor("TemplateLinkColor");
    private float _thickness = float.Parse("TemplateLinkThickness", CultureInfo.InvariantCulture);

    // Current frame: the polyline in window-local coordinates, plus the window origin it was
    // translated by. Null when there is nothing to paint.
    private PointF[]? _windowPoints;
    private Region? _windowRegion;

    public TemplateClass()
    {
        // Opaque fill matching the grid the window is carved out of: the window covers exactly
        // the stroke band, so this fill has to be invisible against the canvas.
        BackColor = ParseColor("TemplateSurfaceBackground");
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
        // Disabled: the stroke band must not swallow mouse input meant for the canvas
        // underneath (pan) or for the connection gesture.
        TabStop = false;
        Enabled = false;
        ApplyRegion(null);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color LineColor { get => _lineColor; set { _lineColor = value; Invalidate(); } }

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

    /// <summary>Re-reads this view's link and re-carves the window. Called on bind and on recycle.</summary>
    public void Sync(IWorkflowLinkViewModel? link)
    {
        if (link is null) return;

        _canRender = link.IsVisible;
        RebuildGeometry();
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

        RebuildGeometry();
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
            _canRender = _link?.IsVisible == true;
        }

        RebuildGeometry();
    }

    // Rebuilds the window box and region from the current endpoints. Endpoints are canvas-local
    // (the slot layout writes them from each slot control's on-screen position), so the box is
    // used as-is, without the pan the node views add themselves.
    private void RebuildGeometry()
    {
        var link = _link;
        // NaN gate: slot anchors are NaN until the canvas measures them; skip real links until
        // ready. Virtual-link placeholders (Parent is null) are exempt.
        if (link is null || !_canRender || !WorkflowSlotUpdateGate.IsLinkRenderReady(link))
        {
            _windowPoints = null;
            ApplyRegion(null);
            return;
        }

        var sender = link.Sender;
        var receiver = link.Receiver;
        if (sender is null || receiver is null)
        {
            _windowPoints = null;
            ApplyRegion(null);
            return;
        }

        // Fresh compute, never a cached value: a pooled view recycled from a virtual (gesture)
        // link onto a real link must not keep painting dashed.
        _isVirtual = sender.Parent is null && receiver.Parent is null;

        var points = BuildPoints(sender.Anchor, receiver.Anchor);
        if (!IsDrawable(points))
        {
            _windowPoints = null;
            ApplyRegion(null);
            return;
        }

        using var strokePen = new Pen(Color.Black, _thickness + 2 * RegionPad) { LineJoin = LineJoin.Miter };
        using var strokePath = new GraphicsPath();
        strokePath.AddLines(points);
        strokePath.Widen(strokePen);

        var bounds = strokePath.GetBounds();
        var originX = (float)Math.Floor(bounds.Left);
        var originY = (float)Math.Floor(bounds.Top);
        using (var shift = new Matrix(1f, 0f, 0f, 1f, -originX, -originY))
        {
            strokePath.Transform(shift);
        }

        // Land on whole pixels so the drawn line keeps the canvas-local path it had when the
        // canvas painted it; the fractional remainder stays in the point coordinates.
        Location = new Point((int)originX, (int)originY);
        Size = new Size(
            Math.Max(1, (int)Math.Ceiling(bounds.Right) - (int)originX + 1),
            Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - (int)originY + 1));

        var local = new PointF[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            local[i] = new PointF(points[i].X - originX, points[i].Y - originY);
        }

        _windowPoints = local;
        ApplyRegion(strokePath);
        Invalidate();
    }

    // Hands the carved shape to the window. WinForms copies the region into the window, so the
    // previous managed region is ours to dispose.
    private void ApplyRegion(GraphicsPath? strokePath)
    {
        var next = strokePath is null ? new Region() : new Region(strokePath);
        var previous = _windowRegion;
        _windowRegion = next;
        Region = next;
        previous?.Dispose();
    }

    // GDI+ refuses to widen a path it cannot stroke: a line whose endpoints land on the same
    // pixel (the connection gesture's first frame) or whose anchors are unmeasured (NaN).
    private static bool IsDrawable(PointF[] points)
    {
        foreach (var point in points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return false;
        }

        return Math.Abs(points[0].X - points[^1].X) >= 0.5f || Math.Abs(points[0].Y - points[^1].Y) >= 0.5f;
    }

    private PointF[] BuildPoints(Anchor sender, Anchor receiver)
    {
        var s = new PointF((float)sender.Horizontal, (float)sender.Vertical);
        var e = new PointF((float)receiver.Horizontal, (float)receiver.Vertical);
        double dx = e.X - s.X;
        double stub = dx / 2.0 * (1.0 - Phi);
        var p1 = new PointF(s.X + (float)stub, s.Y);
        var p4 = new PointF(e.X - (float)stub, e.Y);
        return [s, p1, p4, e];
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var points = _windowPoints;
        if (points is null || points.Length < 2) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(_lineColor, _thickness);
        if (_isVirtual)
        {
            pen.DashStyle = DashStyle.Dash;
            pen.DashPattern = [4f, 2f];
        }

        g.DrawLines(pen, points);
    }

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

            _windowRegion?.Dispose();
            _windowRegion = null;
        }

        base.Dispose(disposing);
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
