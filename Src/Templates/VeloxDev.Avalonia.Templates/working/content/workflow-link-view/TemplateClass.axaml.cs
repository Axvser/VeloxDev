// VeloxDev customization: Customize line geometry, color, and thickness here.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// Passive visual only — no hover, highlight, or keyboard interaction.
/// </summary>
public partial class TemplateClass : Control
{
    public TemplateClass()
    {
        InitializeComponent();
        IsHitTestVisible = false;
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!CanRender) return;

        var color = LineColor;
        var thickness = LineThickness;
        var brush = new ImmutableSolidColorBrush(color);

        var pen = IsVirtualLink
            ? new Pen(brush, thickness) { DashStyle = new DashStyle([4.0, 2.0], 0) }
            : new Pen(brush, thickness);

        context.DrawGeometry(null, pen, BuildCurve(StartLeft, StartTop, EndLeft, EndTop));
    }

    // Extension point: the control points set the curve's shape. Both are pulled horizontally by
    // max(40, |dx| / 2), which is what makes the line leave each port horizontally — keep that
    // property if you replace the formula.
    private static StreamGeometry BuildCurve(double startLeft, double startTop, double endLeft, double endTop)
    {
        var dx = endLeft - startLeft;
        var pull = Math.Max(40, Math.Abs(dx) * 0.5);
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
