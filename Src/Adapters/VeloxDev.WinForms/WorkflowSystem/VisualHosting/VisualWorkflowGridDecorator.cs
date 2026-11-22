using System.Drawing;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using DrawingPointF = System.Drawing.PointF;
using DrawingSize = System.Drawing.Size;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// The workflow backdrop: the surface colour, the world grid and the two axes, drawn in canvas-local space with the
/// shared <see cref="WorkflowSurfaceMath"/> / <see cref="WorkflowSurfaceGrid"/> rules.
/// </summary>
/// <remarks>
/// The decorator never takes a pointer hit. It follows the canvas pan through <see cref="Pan"/>, so the grid lines
/// stay anchored to the world instead of to the window. Derive to change the palette or paint extra world decoration.
/// </remarks>
public class VisualWorkflowGridDecorator : VisualElement
{
    private DrawingPointF _pan;

    /// <summary>Creates the backdrop with the adapter's dark palette.</summary>
    public VisualWorkflowGridDecorator()
    {
        IsHitTestVisible = false;
        Background = Color.FromArgb(0xFF, 0x17, 0x17, 0x17);
        MinorLineColor = Color.FromArgb(0x23, 0x23, 0x23);
        MajorLineColor = Color.FromArgb(0x2E, 0x2E, 0x2E);
        AxisColor = Color.FromArgb(0x3A, 0x3A, 0x3A);
    }

    /// <summary>World distance between minor grid lines, in pixels.</summary>
    public double GridSpacing { get; set; } = 40d;

    /// <summary>How many minor lines make one major line.</summary>
    public int MajorLineEvery { get; set; } = 5;

    /// <summary>The colour of a minor grid line.</summary>
    public Color MinorLineColor { get; set; }

    /// <summary>The colour of a major grid line.</summary>
    public Color MajorLineColor { get; set; }

    /// <summary>The colour of the world axes.</summary>
    public Color AxisColor { get; set; }

    /// <summary>The signed canvas pan, in pixels; setting it repaints the backdrop.</summary>
    public DrawingPointF Pan
    {
        get => _pan;
        set
        {
            if (_pan == value)
            {
                return;
            }

            _pan = value;
            InvalidateVisual();
        }
    }

    /// <summary>Requests a repaint after a palette or grid setting changed.</summary>
    public void Refresh() => InvalidateVisual();

    /// <inheritdoc />
    protected override DrawingSize MeasureOverride(DrawingSize availableSize) => availableSize;

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        if (Background is { } background)
        {
            using var fill = new SolidBrush(background.ApproximateColor);
            graphics.FillRectangle(fill, 0f, 0f, width, height);
        }

        var spacing = Math.Max(8d, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var localLeft = WorkflowSurfaceMath.GridCanvasLeft(-_pan.X, 0d);
        var localTop = WorkflowSurfaceMath.GridCanvasTop(-_pan.Y, 0d);

        using var minorPen = new Pen(MinorLineColor, 1f);
        using var majorPen = new Pen(MajorLineColor, 1f);
        using var axisPen = new Pen(AxisColor, 1.2f);

        var firstVertical = WorkflowSurfaceMath.GridFirstLine(localLeft, spacing);
        for (var value = firstVertical; value <= localLeft + width + spacing; value += spacing)
        {
            var x = (float)WorkflowSurfaceMath.GridX(value, localLeft, 0d);
            var pen = WorkflowSurfaceGrid.SelectPen(value, majorStep, minorPen, majorPen, axisPen);
            graphics.DrawLine(pen, x, 0f, x, height);
        }

        var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(localTop, spacing);
        for (var value = firstHorizontal; value <= localTop + height + spacing; value += spacing)
        {
            var y = (float)WorkflowSurfaceMath.GridY(value, localTop, 0d);
            var pen = WorkflowSurfaceGrid.SelectPen(value, majorStep, minorPen, majorPen, axisPen);
            graphics.DrawLine(pen, 0f, y, width, y);
        }
    }
}
