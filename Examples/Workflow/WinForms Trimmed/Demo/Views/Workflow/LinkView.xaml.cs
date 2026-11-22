// VeloxDev customization: opt-in per-link view; set Link and call Refresh after the layout moved its ends.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using DrawingPen = System.Drawing.Pen;

namespace Demo.Views.Workflow;

/// <summary>
/// An opt-in visual view for one workflow link. The default tree draws every visible link in a single link layer;
/// use this only when each link needs a view of its own.
/// </summary>
/// <remarks>
/// Place the view over the canvas at its origin: the curve is drawn in canvas-local coordinates, the same space the
/// link's anchors use, and is published so Core's hit test can find it.
/// </remarks>
public sealed partial class LinkView : VisualElement
{
    private const double PullMinimum = 40d;

    private ILinkComponent? _link;
    private Color _lineColor = XamlRuntime.ParseColor("#DDFFFFFF");
    private float _lineWidth = float.Parse("2", CultureInfo.InvariantCulture);

    /// <summary>Creates the link view and runs the generated initializer.</summary>
    public LinkView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
    }

    /// <summary>The link this view renders, or <see langword="null"/>.</summary>
    public ILinkComponent? Link
    {
        get => _link;
        set
        {
            if (ReferenceEquals(_link, value))
            {
                return;
            }

            Retract();
            _link = value;
            InvalidateVisual();
        }
    }

    /// <summary>The colour of the curve.</summary>
    public Color LineColor
    {
        get => _lineColor;
        set { _lineColor = value; InvalidateVisual(); }
    }

    /// <summary>The width of the curve, in pixels.</summary>
    public float LineWidth
    {
        get => _lineWidth;
        set { _lineWidth = value; InvalidateVisual(); }
    }

    /// <summary>Repaints the curve after its endpoints moved.</summary>
    public void Refresh() => InvalidateVisual();

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (_link is not { IsVisible: true } link
            || link.Sender is not { } sender
            || link.Receiver is not { } receiver)
        {
            Retract();
            return;
        }

        var start = sender.Anchor;
        var end = receiver.Anchor;
        if (!IsFinite(start) || !IsFinite(end))
        {
            Retract();
            return;
        }

        var points = LinkCurve.LinkCurvePoints(
            link, start.Horizontal, start.Vertical, end.Horizontal, end.Vertical, PullMinimum);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new DrawingPen(_lineColor, _lineWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        graphics.DrawBezier(
            pen,
            (float)points[0].X, (float)points[0].Y,
            (float)points[1].X, (float)points[1].Y,
            (float)points[2].X, (float)points[2].Y,
            (float)points[3].X, (float)points[3].Y);

        link.PublishCurve(
            LinkCurve.BuildLinkCubic(
                link, start.Horizontal, start.Vertical, end.Horizontal, end.Vertical, PullMinimum),
            this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Retract();
        }

        base.Dispose(disposing);
    }

    private void Retract()
    {
        if (_link is not null)
        {
            _link.PublishCurve(null);
        }
    }

    private static bool IsFinite(Anchor anchor)
        => !double.IsNaN(anchor.Horizontal)
           && !double.IsNaN(anchor.Vertical)
           && !double.IsInfinity(anchor.Horizontal)
           && !double.IsInfinity(anchor.Vertical);
}
