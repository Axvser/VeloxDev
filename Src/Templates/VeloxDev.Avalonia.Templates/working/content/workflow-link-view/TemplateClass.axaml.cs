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
/// It only paints and publishes its curve; hover feedback is the host's — subscribe
/// <c>IWorkflowInputEvents</c> on the helper and handle the routed pointer events.
/// </summary>
public partial class TemplateClass : Control
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

    static TemplateClass()
    {
        AffectsRender<TemplateClass>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty);
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

        var brush = new ImmutableSolidColorBrush(LineColor);

        var pen = IsVirtualLink
            ? new Pen(brush, LineThickness) { DashStyle = new DashStyle([4.0, 2.0], 0) }
            : new Pen(brush, LineThickness);

        var geometry = BuildCurve(StartLeft, StartTop, EndLeft, EndTop);

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
