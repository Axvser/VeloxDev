// VeloxDev customization: Customize line geometry, color, and thickness here.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// The view only paints: it publishes its curve for hit-testing and implements
/// <see cref="ILinkHighlight"/> so the surface lights it on hover. It handles no input itself.
/// </summary>
public partial class TemplateClass : UserControl, ILinkHighlight
{
    // Extension point: the least horizontal pull of the two control points. Keep it in step with the curve
    // that is published for hit-testing below.
    private const double MinimumPull = 40;

    // The link this view last published a curve to; retracted on rebind so a pooled view cannot leave a
    // stale curve answering for a link that no longer draws here.
    private IWorkflowLinkViewModel? _publishedLink;

    public TemplateClass()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        Panel.SetZIndex(this, -100);

        DataContextChanged += OnDataContextChanged;
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(TemplateClass), new PropertyMetadata(true, OnRenderChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(TemplateClass), new PropertyMetadata(false, OnRenderChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(TemplateClass), new PropertyMetadata((Color)ColorConverter.ConvertFromString("TemplateLinkColor"), OnRenderChanged));
    public static readonly DependencyProperty HighlightColorProperty =
        DependencyProperty.Register(nameof(HighlightColor), typeof(Color), typeof(TemplateClass), new PropertyMetadata((Color)ColorConverter.ConvertFromString("#FFFFFFFF"), OnRenderChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(TemplateClass), new PropertyMetadata(false, OnRenderChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    /// <summary>The soft light the link turns into while it is hovered.</summary>
    public Color HighlightColor { get => (Color)GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }

    /// <inheritdoc />
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TemplateClass)d).InvalidateVisual();

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        // Retract the previous link's curve, then let the next draw publish the new one.
        _publishedLink?.PublishCurve(null);
        _publishedLink = null;
        InvalidateVisual();
    }

    private bool IsVirtualLink
        => IsVirtual
            || DataContext is IWorkflowLinkViewModel
            {
                Sender.Parent: null,
                Receiver.Parent: null
            };

    #endregion

    #region Render

    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);
        if (!CanRender) return;
        if (DataContext is IWorkflowLinkViewModel link && !link.IsRenderReady()) return;

        // Publish the curve the surface hit-tests against: same control points as BuildCurve, same
        // canvas-local space. Replace this together with BuildCurve if you change the shape.
        PublishCurve(LinkCurve.BuildCubic(StartLeft, StartTop, EndLeft, EndTop, MinimumPull));

        var color = IsHighlighted ? HighlightColor : LineColor;
        var thickness = TemplateLinkThickness;
        var geometry = BuildCurve(StartLeft, StartTop, EndLeft, EndTop);

        // Extension point: this is the hover feedback. Swap the halo's width or alpha, or HighlightColor,
        // to restyle the highlighted link.
        if (IsHighlighted)
        {
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(AtAlpha(color, 0.18)), thickness + 8)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            }, geometry);
        }

        var brush = new SolidColorBrush(color);
        var pen = IsVirtualLink
            ? new Pen(brush, thickness) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }
            : new Pen(brush, thickness);

        ctx.DrawGeometry(null, pen, geometry);
    }

    // Extension point: the hover halo's opacity (0..1); 0 turns the glow off.
    private static Color AtAlpha(Color color, double alpha)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    // Extension point: the control points set the curve's shape. Both are pulled horizontally by
    // max(40, |dx| / 2), which is what makes the line leave each port horizontally — keep that
    // property if you replace the formula.
    private static Geometry BuildCurve(double startLeft, double startTop, double endLeft, double endTop)
    {
        var dx = endLeft - startLeft;
        var pull = Math.Max(MinimumPull, Math.Abs(dx) * 0.5);
        var figure = new PathFigure
        {
            StartPoint = new Point(startLeft, startTop),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment(
            new Point(startLeft + pull, startTop),
            new Point(endLeft - pull, endTop),
            new Point(endLeft, endTop),
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    // Extension point: change the second argument if one view no longer draws exactly one link.
    private void PublishCurve(LinkCurve curve)
    {
        var link = DataContext as IWorkflowLinkViewModel;
        if (!ReferenceEquals(_publishedLink, link))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = link;
        }

        if (link is not null)
        {
            link.PublishCurve(curve, this);
        }
    }

    #endregion
}
