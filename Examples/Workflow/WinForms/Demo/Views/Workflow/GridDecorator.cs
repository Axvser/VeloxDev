// VeloxDev customization: The workflow grid. The decorator owns the world-coordinate maths, the grid lines and
// the axes; WorkflowSurfaceBehavior pushes the scroll/content offsets in and draws the floating ruler bands.
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow grid: the surface background, the world grid and the axes.
/// </summary>
public sealed class GridDecorator : Panel, IWorkflowGridDecorator
{
    private const double Eps = 0.001;
    private const int DefaultGridSpacing = 40;
    private const int DefaultMajorFreq = 5;
    private const int DefaultRulerThickness = 36;

    private readonly Color _gridBackground = WorkflowSurfaceColors.Parse("#171717");
    private readonly Color _minorGridColor = Color.FromArgb(35, 35, 35);
    private readonly Color _majorGridColor = Color.FromArgb(46, 46, 46);
    private readonly Color _axisColor = Color.FromArgb(58, 58, 58);

    private double _scrollOffsetX;
    private double _scrollOffsetY;
    private double _contentOffsetX;
    private double _contentOffsetY;

    /// <summary>Creates the grid and enables its double-buffered paint style.</summary>
    public GridDecorator()
    {
        DoubleBuffered = true;
        BackColor = Opaque(_gridBackground);
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    /// <summary>World distance between minor grid lines, in pixels.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double GridSpacing { get; set; } = DefaultGridSpacing;

    /// <summary>How many minor lines make a major one.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MajorLineEvery { get; set; } = DefaultMajorFreq;

    /// <summary>Thickness of the ruler band the surface reserves, in pixels.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RulerThickness { get; set; } = DefaultRulerThickness;

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetX
    {
        get => _scrollOffsetX;
        set { if (_scrollOffsetX != value) { _scrollOffsetX = value; Invalidate(); } }
    }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetY
    {
        get => _scrollOffsetY;
        set { if (_scrollOffsetY != value) { _scrollOffsetY = value; Invalidate(); } }
    }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetX
    {
        get => _contentOffsetX;
        set { if (_contentOffsetX != value) { _contentOffsetX = value; Invalidate(); } }
    }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetY
    {
        get => _contentOffsetY;
        set { if (_contentOffsetY != value) { _contentOffsetY = value; Invalidate(); } }
    }

    /// <inheritdoc />
    public double RulerBand => RulerThickness;

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        Render(e.Graphics);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
    }

    private void Render(Graphics g)
    {
        var bounds = new RectangleF(0, 0, Width, Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var background = new SolidBrush(_gridBackground);
        g.FillRectangle(background, bounds);

        DrawGrid(g, bounds);
    }

    private void DrawGrid(Graphics g, RectangleF bounds)
    {
        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);

        var worldLeft = WorkflowSurfaceMath.GridWorldLeft(_scrollOffsetX, _contentOffsetX);
        var worldTop = WorkflowSurfaceMath.GridWorldTop(_scrollOffsetY, _contentOffsetY);
        var worldRight = worldLeft + bounds.Width;
        var worldBottom = worldTop + bounds.Height;

        using var minorPen = new Pen(_minorGridColor, 1f);
        using var majorPen = new Pen(_majorGridColor, 1f);
        using var axisPen = new Pen(_axisColor, 1.2f);

        var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, 0);
            var pen = IsNearZero(value) ? axisPen : IsMajorLine(value, majorStep) ? majorPen : minorPen;
            g.DrawLine(pen, x, 0, x, bounds.Height);
        }

        var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, 0);
            var pen = IsNearZero(value) ? axisPen : IsMajorLine(value, majorStep) ? majorPen : minorPen;
            g.DrawLine(pen, 0, y, bounds.Width, y);
        }
    }

    private static bool IsMajorLine(double value, double majorStep)
    {
        if (majorStep <= 0)
        {
            return false;
        }

        var remainder = value % majorStep;
        return Math.Abs(remainder) < Eps
               || Math.Abs(remainder - majorStep) < Eps
               || Math.Abs(remainder + majorStep) < Eps;
    }

    private static bool IsNearZero(double value) => Math.Abs(value) < Eps;

    private static Color Opaque(Color color) => Color.FromArgb(255, color);
}

/// <summary>The viewport: a XAML scrollable control that lets the grid decorator paint through it.</summary>
public sealed class SurfaceScrollableControl : VeloxDev.WinForms.Xaml.Controls.ScrollableControl
{
    /// <summary>Creates the viewport with a transparent background.</summary>
    public SurfaceScrollableControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
}

/// <summary>
/// The pool host: a transparent XAML canvas that lets the grid show through and completes the half of an
/// edge-anchored port that spills outside its card (WinForms clips a child window to its parent).
/// </summary>
public sealed class SurfaceCanvas : VeloxDev.WinForms.Xaml.Controls.Canvas
{
    /// <summary>Creates the canvas with a transparent background.</summary>
    public SurfaceCanvas()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        foreach (Control card in Controls)
        {
            if (card is not WorkflowNodeCard)
            {
                continue;
            }

            foreach (var port in EnumeratePorts(card))
            {
                if (!port.Visible || port.ViewModel is null)
                {
                    continue;
                }


                var screen = port.PointToScreen(new Point(port.Width / 2, port.Height / 2));
                var local = PointToClient(screen);
                port.RenderGlyph(e.Graphics, local.X, local.Y, Math.Min(port.Width, port.Height));
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<SlotView> EnumeratePorts(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is SlotView port)
            {
                yield return port;
            }

            foreach (var nested in EnumeratePorts(child))
            {
                yield return nested;
            }
        }
    }
}
