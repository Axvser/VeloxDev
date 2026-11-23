// VeloxDev customization: customize the curve, colours and hit-test policy here.
using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;
using DrawingPen = System.Drawing.Pen;

namespace Demo.Views.Workflow;

/// <summary>Cubic Bézier connection that publishes the curve Core hit-tests and paints it in world coordinates.</summary>
public sealed partial class LinkView : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    private const double MinimumPull = 40d;
    private ILinkComponent? _publishedLink;

    public static readonly DependencyProperty StartLeftProperty = DependencyProperty.Register(
        nameof(StartLeft), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });
    public static readonly DependencyProperty StartTopProperty = DependencyProperty.Register(
        nameof(StartTop), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });
    public static readonly DependencyProperty EndLeftProperty = DependencyProperty.Register(
        nameof(EndLeft), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });
    public static readonly DependencyProperty EndTopProperty = DependencyProperty.Register(
        nameof(EndTop), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });
    public static readonly DependencyProperty CanRenderProperty = DependencyProperty.Register(
        nameof(CanRender), typeof(bool), typeof(LinkView),
        new PropertyMetadata(true) { AffectsRender = true });
    public static readonly DependencyProperty IsVirtualProperty = DependencyProperty.Register(
        nameof(IsVirtual), typeof(bool), typeof(LinkView),
        new PropertyMetadata(false) { AffectsRender = true });
    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(LinkView),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)) { AffectsRender = true });
    public static readonly DependencyProperty HighlightColorProperty = DependencyProperty.Register(
        nameof(HighlightColor), typeof(Color), typeof(LinkView),
        new PropertyMetadata(Color.White) { AffectsRender = true });
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(LinkView),
        new PropertyMetadata(false) { AffectsRender = true });

    public LinkView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
    }

    public double StartLeft { get => (double)GetValue(StartLeftProperty)!; set => SetValue(StartLeftProperty, value); }

    public double StartTop { get => (double)GetValue(StartTopProperty)!; set => SetValue(StartTopProperty, value); }

    public double EndLeft { get => (double)GetValue(EndLeftProperty)!; set => SetValue(EndLeftProperty, value); }

    public double EndTop { get => (double)GetValue(EndTopProperty)!; set => SetValue(EndTopProperty, value); }

    public bool CanRender { get => (bool)GetValue(CanRenderProperty)!; set => SetValue(CanRenderProperty, value); }

    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty)!; set => SetValue(IsVirtualProperty, value); }

    public Color LineColor { get => (Color)GetValue(LineColorProperty)!; set => SetValue(LineColorProperty, value); }

    public Color HighlightColor { get => (Color)GetValue(HighlightColorProperty)!; set => SetValue(HighlightColorProperty, value); }

    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty)!; set => SetValue(IsHighlightedProperty, value); }

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
        var thickness = IsHighlighted ? 3.5f : 2f;

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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = null;
        }

        base.Dispose(disposing);
    }
}
