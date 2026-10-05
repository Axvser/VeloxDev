// VeloxDev customization: Customize line geometry, color, and thickness here.
using System;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// The view only paints: it publishes its curve for hit-testing and handles no input itself. Hover feedback is
/// the host's — subscribe <c>IInputEvents</c> on the helper and handle the routed pointer events.
/// </summary>
public partial class TemplateClass : UserControl
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

    /// <summary>The canvas-local X of the start end.</summary>
    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local Y of the start end.</summary>
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local X of the end end.</summary>
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local Y of the end end.</summary>
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>Whether the link should be drawn at all.</summary>
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(TemplateClass), new PropertyMetadata(true, OnRenderChanged));

    /// <summary>Whether this is the drag preview rather than a real link.</summary>
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(TemplateClass), new PropertyMetadata(false, OnRenderChanged));

    /// <summary>The stroke colour.</summary>
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(TemplateClass),
            new PropertyMetadata(ParseColor("TemplateLinkColor", Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)), OnRenderChanged));

    /// <summary>The canvas-local X of the start end.</summary>
    public double StartLeft { get => GetValue(StartLeftProperty) is double value ? value : 0d; set => SetValue(StartLeftProperty, value); }

    /// <summary>The canvas-local Y of the start end.</summary>
    public double StartTop { get => GetValue(StartTopProperty) is double value ? value : 0d; set => SetValue(StartTopProperty, value); }

    /// <summary>The canvas-local X of the end end.</summary>
    public double EndLeft { get => GetValue(EndLeftProperty) is double value ? value : 0d; set => SetValue(EndLeftProperty, value); }

    /// <summary>The canvas-local Y of the end end.</summary>
    public double EndTop { get => GetValue(EndTopProperty) is double value ? value : 0d; set => SetValue(EndTopProperty, value); }

    /// <summary>Whether the link should be drawn at all.</summary>
    public bool CanRender { get => GetValue(CanRenderProperty) is true; set => SetValue(CanRenderProperty, value); }

    /// <summary>Whether this is the drag preview rather than a real link.</summary>
    public bool IsVirtual { get => GetValue(IsVirtualProperty) is true; set => SetValue(IsVirtualProperty, value); }

    /// <summary>The stroke colour.</summary>
    public Color LineColor
    {
        get => GetValue(LineColorProperty) is Color color ? color : Colors.White;
        set => SetValue(LineColorProperty, value);
    }

    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TemplateClass)d).InvalidateVisual();

    // 颜色 token 在生成时被替换成字面量；解不出来就退回默认色。
    private static Color ParseColor(string text, Color fallback)
        => ColorConverter.ConvertFromString(text) is Color color ? color : fallback;

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

    /// <inheritdoc />
    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);

        if (!CanRender)
        {
            return;
        }

        var link = DataContext as IWorkflowLinkViewModel;
        if (link is not null && !link.IsRenderReady())
        {
            return;
        }

        // 自盒化：Jalium 的渲染器按 RenderSize 裁剪子元素、不看画了什么，画到盒外的部分会被**静默丢掉**
        // （见 WorkflowLinkBounds 的说明）。所以先把盒子挪到这条线自己的包围盒上，再用盒原点把坐标烘回元素局部。
        if (!WorkflowLinkBounds.Apply(this, [new Point(StartLeft, StartTop), new Point(EndLeft, EndTop)], out var originX, out var originY))
        {
            return;
        }

        // Publish the curve the surface hit-tests against — the same control points as the drawing below, in
        // canvas-local space. Replace this together with BuildCurve if you change the shape.
        PublishCurve(LinkCurve.BuildLinkCubic(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull));

        const double thickness = TemplateLinkThickness;
        var brush = new SolidColorBrush(LineColor);
        var pen = IsVirtualLink
            ? new Pen(brush, thickness) { DashStyle = new DashStyle([4d, 2d], 0d) }
            : new Pen(brush, thickness);

        ctx.DrawGeometry(null, pen, BuildCurve(link, originX, originY));
    }

    // Extension point: the control points set the curve's shape. Core derives them from each port's own edge
    // (LinkCurve.LinkCurvePoints) — keep that source if you replace the drawing.
    private Geometry BuildCurve(IWorkflowLinkViewModel? link, double originX, double originY)
    {
        var points = LinkCurve.LinkCurvePoints(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull);
        var figure = new PathFigure
        {
            StartPoint = new Point(points[0].X - originX, points[0].Y - originY),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment(
            new Point(points[1].X - originX, points[1].Y - originY),
            new Point(points[2].X - originX, points[2].Y - originY),
            new Point(points[3].X - originX, points[3].Y - originY),
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
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
