// VeloxDev customization: Customize line geometry, color, thickness, and highlight here.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// The surface's interaction hub lights <see cref="IsHighlighted"/> while the pointer is over this link.
/// </summary>
public partial class TemplateClass : Control, ILinkHighlight
{
    // Horizontal pull shared by the drawn curve and the hit-test curve, so both describe the same shape.
    private const double PullMinimum = 40;

    // The flattened curve published to the link's helper; the surface hit-tests this exact shape
    // (see ILinkHitTestable / LinkHitTestEx). Extension point: rebuild it wherever the drawn curve changes.
    private LinkCurve? _curve;

    public TemplateClass()
    {
        InitializeComponent();
        // The link is interactive by default: hit-testable so the hover can find it, focusable so Delete
        // reaches the adapter's key route. Only the painted stroke answers — this control draws a geometry
        // and has no background, so the framework's hit test is the stroke, not the canvas-sized box.
        IsHitTestVisible = true;
        Focusable = true;

        // Keep this handler: the view is canvas-sized, so focus would otherwise scroll the canvas on hover.
        // It stays scoped to this view — a focused input inside a node card must still scroll in.
        AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);

        RefreshGeometry();
    }

    #region StyledProperty

    public static readonly StyledProperty<double> StartLeftProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(StartLeft));

    public static readonly StyledProperty<double> StartTopProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(StartTop));

    public static readonly StyledProperty<double> EndLeftProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(EndLeft));

    public static readonly StyledProperty<double> EndTopProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(EndTop));

    public static readonly StyledProperty<bool> CanRenderProperty =
        AvaloniaProperty.Register<TemplateClass, bool>(nameof(CanRender), true);

    public static readonly StyledProperty<bool> IsVirtualProperty =
        AvaloniaProperty.Register<TemplateClass, bool>(nameof(IsVirtual), false);

    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<TemplateClass, Color>(nameof(LineColor), Color.Parse("TemplateLinkColor"));

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(LineThickness), TemplateLinkThickness);

    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<TemplateClass, bool>(nameof(IsHighlighted), false);

    public static readonly StyledProperty<Color> HighlightColorProperty =
        AvaloniaProperty.Register<TemplateClass, Color>(nameof(HighlightColor), Color.Parse("#FFFFFFFF"));

    public double StartLeft
    {
        get => GetValue(StartLeftProperty);
        set => SetValue(StartLeftProperty, value);
    }

    public double StartTop
    {
        get => GetValue(StartTopProperty);
        set => SetValue(StartTopProperty, value);
    }

    public double EndLeft
    {
        get => GetValue(EndLeftProperty);
        set => SetValue(EndLeftProperty, value);
    }

    public double EndTop
    {
        get => GetValue(EndTopProperty);
        set => SetValue(EndTopProperty, value);
    }

    public bool CanRender
    {
        get => GetValue(CanRenderProperty);
        set => SetValue(CanRenderProperty, value);
    }

    public bool IsVirtual
    {
        get => GetValue(IsVirtualProperty);
        set => SetValue(IsVirtualProperty, value);
    }

    public Color LineColor
    {
        get => GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    // Set by the surface's interaction hub while the pointer is over this link; the render below repaints on change.
    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    // Extension point: the white glow shown while this link is highlighted. White is deliberate — the line
    // reads as lit rather than recoloured, and the halo drawn around it is what makes it a glow.
    public Color HighlightColor
    {
        get => GetValue(HighlightColorProperty);
        set => SetValue(HighlightColorProperty, value);
    }

    static TemplateClass()
    {
        AffectsRender<TemplateClass>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty, IsHighlightedProperty, HighlightColorProperty);
    }

    #endregion

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // The curve follows the endpoints, and the control is pooled: a rebound control must retract the old
        // link's curve before it publishes its own, or the old link answers the pointer at the new control.
        if (change.Property == DataContextProperty
            && change.OldValue is IWorkflowLinkViewModel old && !ReferenceEquals(old, change.NewValue))
        {
            old.PublishCurve(null);
        }

        if (change.Property == StartLeftProperty || change.Property == StartTopProperty
            || change.Property == EndLeftProperty || change.Property == EndTopProperty
            || change.Property == DataContextProperty)
        {
            RefreshGeometry();
        }
    }

    // Extension point: this is the one geometry build. It feeds both the drawn curve and the published
    // hit-test curve, so the two can never describe different shapes.
    private void RefreshGeometry()
    {
        _curve = LinkCurve.BuildCubic(StartLeft, StartTop, EndLeft, EndTop, PullMinimum);
        (DataContext as IWorkflowLinkViewModel)?.PublishCurve(_curve, this);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!CanRender) return;

        var color = IsHighlighted ? HighlightColor : LineColor;
        var thickness = IsHighlighted ? LineThickness + 1.5 : LineThickness;
        var brush = new ImmutableSolidColorBrush(color);

        var pen = IsVirtualLink
            ? new Pen(brush, thickness) { DashStyle = new DashStyle([4.0, 2.0], 0) }
            : new Pen(brush, thickness);

        var geometry = BuildCurve(StartLeft, StartTop, EndLeft, EndTop);

        // Extension point: the halo painted under the line while highlighted. Widen or fade the pen here.
        if (IsHighlighted)
        {
            var glowPen = new Pen(new ImmutableSolidColorBrush(color, 0.25), thickness + 6);
            context.DrawGeometry(null, glowPen, geometry);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    // Extension point: the control points set the curve's shape. Both are pulled horizontally by
    // max(PullMinimum, |dx| / 2), which is what makes the line leave each port horizontally — keep that
    // property if you replace the formula.
    private static StreamGeometry BuildCurve(double startLeft, double startTop, double endLeft, double endTop)
    {
        var dx = endLeft - startLeft;
        var pull = Math.Max(PullMinimum, Math.Abs(dx) * 0.5);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(startLeft, startTop), false);
            ctx.CubicBezierTo(
                new Point(startLeft + pull, startTop),
                new Point(endLeft - pull, endTop),
                new Point(endLeft, endTop));
        }

        return geometry;
    }

    private bool IsVirtualLink
        => IsVirtual
            || DataContext is IWorkflowLinkViewModel
            {
                Sender.Parent: null,
                Receiver.Parent: null
            };
}
