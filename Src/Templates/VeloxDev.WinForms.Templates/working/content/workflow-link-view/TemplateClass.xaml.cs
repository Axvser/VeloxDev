// VeloxDev customization: customize the curve, colours and hit-test policy here.
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;
using DrawingPen = System.Drawing.Pen;

namespace TemplateNamespace;

/// <summary>Cubic Bézier connection that publishes the curve Core hit-tests and paints it in world coordinates.</summary>
public sealed partial class TemplateClass : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    private const double MinimumPull = 40d;
    private ILinkComponent? _publishedLink;

    public static readonly DependencyProperty StartLeftProperty = DependencyProperty.Register(
        nameof(StartLeft), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d) { AffectsRender = true });
    public static readonly DependencyProperty StartTopProperty = DependencyProperty.Register(
        nameof(StartTop), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d) { AffectsRender = true });
    public static readonly DependencyProperty EndLeftProperty = DependencyProperty.Register(
        nameof(EndLeft), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d) { AffectsRender = true });
    public static readonly DependencyProperty EndTopProperty = DependencyProperty.Register(
        nameof(EndTop), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d) { AffectsRender = true });
    public static readonly DependencyProperty CanRenderProperty = DependencyProperty.Register(
        nameof(CanRender), typeof(bool), typeof(TemplateClass),
        new PropertyMetadata(true) { AffectsRender = true });
    public static readonly DependencyProperty IsVirtualProperty = DependencyProperty.Register(
        nameof(IsVirtual), typeof(bool), typeof(TemplateClass),
        new PropertyMetadata(false) { AffectsRender = true });
    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)) { AffectsRender = true });
    public static readonly DependencyProperty HighlightColorProperty = DependencyProperty.Register(
        nameof(HighlightColor), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.White) { AffectsRender = true });
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(TemplateClass),
        new PropertyMetadata(false) { AffectsRender = true });

    public TemplateClass()
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

    // 链接视图铺满画布，曲线却可能画到负世界坐标里；高亮/悬停的脏区必须按曲线包围盒算，
    // 否则只重画 0..Bounds 的象限，系统截图触发整面重画后才“突然”完整。
    protected override RectangleF GetInvalidationBounds()
    {
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
