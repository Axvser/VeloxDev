// VeloxDev customization: customize the curve, colours, thickness and hit-test policy here.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using Easel;
using VeloxDev.WorkflowSystem;
using DrawingPen = System.Drawing.Pen;

namespace Demo.Views.Workflow;

/// <summary>Cubic Bézier connection that publishes the curve Core hit-tests and paints it in world coordinates.</summary>
public sealed partial class LinkView : Easel.Visuals.Controls.UserControl
{
    private const double MinimumPull = 40d;
    private ILinkComponent? _publishedLink;

    #region Dependency properties

    /// <summary>The sender port's world X.</summary>
    public static readonly DependencyProperty StartLeftProperty = DependencyProperty.Register(
        nameof(StartLeft), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The sender port's world Y.</summary>
    public static readonly DependencyProperty StartTopProperty = DependencyProperty.Register(
        nameof(StartTop), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The receiver port's world X.</summary>
    public static readonly DependencyProperty EndLeftProperty = DependencyProperty.Register(
        nameof(EndLeft), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The receiver port's world Y.</summary>
    public static readonly DependencyProperty EndTopProperty = DependencyProperty.Register(
        nameof(EndTop), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>Whether the owning link is currently visible.</summary>
    public static readonly DependencyProperty CanRenderProperty = DependencyProperty.Register(
        nameof(CanRender), typeof(bool), typeof(LinkView),
        new PropertyMetadata(true) { AffectsRender = true });

    /// <summary>Whether the curve is drawn dashed as a virtual (unattached) link.</summary>
    public static readonly DependencyProperty IsVirtualProperty = DependencyProperty.Register(
        nameof(IsVirtual), typeof(bool), typeof(LinkView),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>The resting line colour.</summary>
    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(LinkView),
        new PropertyMetadata(ParseColor("#DDFFFFFF")) { AffectsRender = true });

    /// <summary>The resting line width.</summary>
    public static readonly DependencyProperty LineThicknessProperty = DependencyProperty.Register(
        nameof(LineThickness), typeof(float), typeof(LinkView),
        new PropertyMetadata(ParseThickness("2")) { AffectsRender = true });

    /// <summary>The line colour used while the link is highlighted.</summary>
    public static readonly DependencyProperty HighlightColorProperty = DependencyProperty.Register(
        nameof(HighlightColor), typeof(Color), typeof(LinkView),
        new PropertyMetadata(Color.White) { AffectsRender = true });

    /// <summary>The line width used while the link is highlighted.</summary>
    public static readonly DependencyProperty HighlightThicknessProperty = DependencyProperty.Register(
        nameof(HighlightThickness), typeof(float), typeof(LinkView),
        new PropertyMetadata(ParseThickness("2") * 1.75f) { AffectsRender = true });

    /// <summary>Whether the link is highlighted.</summary>
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(LinkView),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Creates the view and keeps it out of the hit-test path.</summary>
    public LinkView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
    }

    /// <summary>The sender port's world X.</summary>
    public double StartLeft { get => (double)GetValue(StartLeftProperty)!; set => SetValue(StartLeftProperty, value); }

    /// <summary>The sender port's world Y.</summary>
    public double StartTop { get => (double)GetValue(StartTopProperty)!; set => SetValue(StartTopProperty, value); }

    /// <summary>The receiver port's world X.</summary>
    public double EndLeft { get => (double)GetValue(EndLeftProperty)!; set => SetValue(EndLeftProperty, value); }

    /// <summary>The receiver port's world Y.</summary>
    public double EndTop { get => (double)GetValue(EndTopProperty)!; set => SetValue(EndTopProperty, value); }

    /// <summary>Whether the owning link is currently visible.</summary>
    public bool CanRender { get => (bool)GetValue(CanRenderProperty)!; set => SetValue(CanRenderProperty, value); }

    /// <summary>Whether the curve is drawn dashed as a virtual (unattached) link.</summary>
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty)!; set => SetValue(IsVirtualProperty, value); }

    /// <summary>The resting line colour.</summary>
    public Color LineColor { get => (Color)GetValue(LineColorProperty)!; set => SetValue(LineColorProperty, value); }

    /// <summary>The resting line width.</summary>
    public float LineThickness { get => (float)GetValue(LineThicknessProperty)!; set => SetValue(LineThicknessProperty, value); }

    /// <summary>The line colour used while the link is highlighted.</summary>
    public Color HighlightColor { get => (Color)GetValue(HighlightColorProperty)!; set => SetValue(HighlightColorProperty, value); }

    /// <summary>The line width used while the link is highlighted.</summary>
    public float HighlightThickness { get => (float)GetValue(HighlightThicknessProperty)!; set => SetValue(HighlightThicknessProperty, value); }

    /// <summary>Whether the link is highlighted.</summary>
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty)!; set => SetValue(IsHighlightedProperty, value); }

    #endregion

    #region Render

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, DataContextProperty))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = null;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override RectangleF GetInvalidationBounds()
    {
        // The view spans the whole canvas while the curve can reach negative world coordinates: invalidating only
        // 0..Bounds leaves those quadrants stale until a full repaint.
        if (CanRender && DataContext is ILinkComponent link && link.IsRenderReady()
            && double.IsFinite(StartLeft) && double.IsFinite(StartTop)
            && double.IsFinite(EndLeft) && double.IsFinite(EndTop))
        {
            var points = LinkCurve.LinkCurvePoints(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull);
            var minX = points.Min(point => point.X);
            var minY = points.Min(point => point.Y);
            var maxX = points.Max(point => point.X);
            var maxY = points.Max(point => point.Y);
            var bounds = RectangleF.FromLTRB((float)minX, (float)minY, (float)maxX, (float)maxY);
            bounds.Inflate(12f, 12f);
            return bounds;
        }

        return base.GetInvalidationBounds();
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (!CanRender || DataContext is not ILinkComponent link || !link.IsRenderReady())
        {
            return;
        }

        var points = LinkCurve.LinkCurvePoints(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull);
        var curve = LinkCurve.FromCubic(
            points[0].X, points[0].Y, points[1].X, points[1].Y,
            points[2].X, points[2].Y, points[3].X, points[3].Y);

        if (!ReferenceEquals(_publishedLink, link))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = link;
        }

        link.PublishCurve(curve, this);

        var color = IsHighlighted ? HighlightColor : LineColor;
        var isVirtual = IsVirtual || (link.Sender?.Parent is null && link.Receiver?.Parent is null);
        var thickness = IsHighlighted ? HighlightThickness : LineThickness;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        path.AddBezier(
            (float)points[0].X, (float)points[0].Y,
            (float)points[1].X, (float)points[1].Y,
            (float)points[2].X, (float)points[2].Y,
            (float)points[3].X, (float)points[3].Y);
        using var pen = new DrawingPen(color, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            DashStyle = isVirtual ? DashStyle.Custom : DashStyle.Solid,
        };
        if (isVirtual)
        {
            pen.DashPattern = [4f, 2f];
        }

        graphics.DrawPath(pen, path);
    }

    #endregion

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = null;
        }

        base.Dispose(disposing);
    }

    private static Color ParseColor(string token) => XamlRuntime.ParseColor(token);

    private static float ParseThickness(string token)
        => float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 2f;
}
