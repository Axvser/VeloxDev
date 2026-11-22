// VeloxDev customization: the grid palette, spacing and drawing live here; the class is a plain visual control.
using System.Drawing;
using System.Globalization;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using DrawingPen = System.Drawing.Pen;
using VisualBorder = VeloxDev.WinForms.Xaml.Visuals.Controls.Border;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace TemplateNamespace;

/// <summary>The workflow backdrop: the world grid and axes behind the surface.</summary>
public sealed class TemplateClass : VisualBorder, IWorkflowGridDecorator
{
    private const double Epsilon = 0.001d;

    /// <summary>Creates the backdrop and applies the palette.</summary>
    public TemplateClass()
    {
        Background = XamlRuntime.ParseColor("TemplateGridBackground");
        MinorLineColor = XamlRuntime.ParseColor("TemplateMinorGridColor");
        MajorLineColor = XamlRuntime.ParseColor("TemplateMajorGridColor");
        AxisColor = XamlRuntime.ParseColor("TemplateAxisColor");
        GridSpacing = ParseSpacing("TemplateGridSpacing");
        MajorLineEvery = int.Parse("TemplateMajorLineEvery", CultureInfo.InvariantCulture);
        Padding = new XamlThickness(0f);
    }

    /// <summary>Horizontal scroll offset written by the hosting surface.</summary>
    public double ScrollOffsetX { get; set; }

    /// <summary>Vertical scroll offset written by the hosting surface.</summary>
    public double ScrollOffsetY { get; set; }

    /// <summary>Horizontal content offset written by the hosting surface.</summary>
    public double ContentOffsetX { get; set; }

    /// <summary>Vertical content offset written by the hosting surface.</summary>
    public double ContentOffsetY { get; set; }

    /// <summary>This decorator draws no separate ruler band; the visual surface owns the rulers.</summary>
    public double RulerBand => 0d;

    /// <summary>World distance between minor grid lines, in pixels.</summary>
    public double GridSpacing { get; set; }

    /// <summary>How many minor lines make one major line.</summary>
    public int MajorLineEvery { get; set; }

    /// <summary>The colour of a minor grid line.</summary>
    public Color MinorLineColor { get; set; }

    /// <summary>The colour of a major grid line.</summary>
    public Color MajorLineColor { get; set; }

    /// <summary>The colour of the world axes.</summary>
    public Color AxisColor { get; set; }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        var step = Math.Max(8d, GridSpacing);
        var majorStep = step * Math.Max(1, MajorLineEvery);
        var localLeft = WorkflowSurfaceMath.GridCanvasLeft(ScrollOffsetX, ContentOffsetX);
        var localTop = WorkflowSurfaceMath.GridCanvasTop(ScrollOffsetY, ContentOffsetY);

        using var minorPen = new DrawingPen(MinorLineColor, 1f);
        using var majorPen = new DrawingPen(MajorLineColor, 1f);
        using var axisPen = new DrawingPen(AxisColor, 1.2f);

        var firstVertical = WorkflowSurfaceMath.GridFirstLine(localLeft, step);
        for (var value = firstVertical; value <= localLeft + width + step; value += step)
        {
            var x = (float)WorkflowSurfaceMath.GridX(value, localLeft, 0d);
            var pen = IsNearZero(value) ? axisPen : IsMajorLine(value, majorStep) ? majorPen : minorPen;
            graphics.DrawLine(pen, x, 0f, x, height);
        }

        var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(localTop, step);
        for (var value = firstHorizontal; value <= localTop + height + step; value += step)
        {
            var y = (float)WorkflowSurfaceMath.GridY(value, localTop, 0d);
            var pen = IsNearZero(value) ? axisPen : IsMajorLine(value, majorStep) ? majorPen : minorPen;
            graphics.DrawLine(pen, 0f, y, width, y);
        }
    }

    private static bool IsMajorLine(double value, double majorStep)
    {
        if (majorStep <= 0d)
        {
            return false;
        }

        var remainder = value % majorStep;
        return Math.Abs(remainder) < Epsilon
               || Math.Abs(remainder - majorStep) < Epsilon
               || Math.Abs(remainder + majorStep) < Epsilon;
    }

    private static bool IsNearZero(double value) => Math.Abs(value) < Epsilon;

    // Accept the "40d" spelling used by the grid parameters on the other platforms.
    private static double ParseSpacing(string text)
        => double.Parse(text.TrimEnd('d', 'D'), NumberStyles.Float, CultureInfo.InvariantCulture);
}
