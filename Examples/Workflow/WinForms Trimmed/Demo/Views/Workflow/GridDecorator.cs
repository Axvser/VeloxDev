// VeloxDev customization: The workflow grid. This decorator owns the world-coordinate maths, the grid lines,
// the axes and the rounded surface outline; the surface behavior pushes the scroll/content offsets into it and
// draws the floating ruler bands itself from RulerBand.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow grid: the surface background, the world grid, the axes and the rounded surface outline.
/// </summary>
public sealed class GridDecorator : Panel, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    // Extension point: ruler-band thickness.
    private const double DefaultRulerThickness = 36;

    private Color _gridBackground = WorkflowSurfaceColors.Parse("#1E1E1E");
    private Color _minorGridColor = WorkflowSurfaceColors.Parse("#2A2D2E");
    private Color _majorGridColor = WorkflowSurfaceColors.Parse("#3A3D40");
    private Color _axisColor = WorkflowSurfaceColors.Parse("#4D4D4D");
    private Color _rulerBackground = WorkflowSurfaceColors.Parse("#70252526");
    private Color _rulerTickColor = WorkflowSurfaceColors.Parse("#555555");
    private Color _rulerLabelColor = WorkflowSurfaceColors.Parse("#888888");
    private Color _rulerDividerColor = WorkflowSurfaceColors.Parse("#3A3D40");
    private Color _surfaceBorderBrush = WorkflowSurfaceColors.Parse("#33FFFFFF");
    private double _gridSpacing = ParseGridValue("40d");
    private int _majorLineEvery = int.Parse("5", CultureInfo.InvariantCulture);
    private double _rulerThickness = DefaultRulerThickness;
    private double _scrollOffsetX;
    private double _scrollOffsetY;
    private double _contentOffsetX;
    private double _contentOffsetY;
    private float _surfaceBorderThickness = 1f;
    private float _surfaceCornerRadius = 3f;

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

    /// <summary>The surface behind the grid.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color GridBackground
    {
        get => _gridBackground;
        set
        {
            if (_gridBackground == value) return;
            _gridBackground = value;
            BackColor = Opaque(value);
            Invalidate();
        }
    }

    /// <summary>Colour of the minor grid lines.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color MinorGridColor
    {
        get => _minorGridColor;
        set { if (_minorGridColor != value) { _minorGridColor = value; Invalidate(); } }
    }

    /// <summary>Colour of the major grid lines.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color MajorGridColor
    {
        get => _majorGridColor;
        set { if (_majorGridColor != value) { _majorGridColor = value; Invalidate(); } }
    }

    /// <summary>Colour of the world axes.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color AxisColor
    {
        get => _axisColor;
        set { if (_axisColor != value) { _axisColor = value; Invalidate(); } }
    }

    /// <summary>Fill of the two ruler bands.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color RulerBackground
    {
        get => _rulerBackground;
        set { _rulerBackground = value; }
    }

    /// <summary>Colour of the ruler ticks.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color RulerTickColor
    {
        get => _rulerTickColor;
        set { _rulerTickColor = value; }
    }

    /// <summary>Colour of the ruler labels.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color RulerLabelColor
    {
        get => _rulerLabelColor;
        set { _rulerLabelColor = value; }
    }

    /// <summary>Colour of the lines dividing each band from the grid.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color RulerDividerColor
    {
        get => _rulerDividerColor;
        set { _rulerDividerColor = value; }
    }

    /// <summary>Colour of the rounded outline around the whole surface.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color SurfaceBorderBrush
    {
        get => _surfaceBorderBrush;
        set { if (_surfaceBorderBrush != value) { _surfaceBorderBrush = value; Invalidate(); } }
    }

    /// <summary>Width of the rounded outline around the whole surface, in pixels.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float SurfaceBorderThickness
    {
        get => _surfaceBorderThickness;
        set { if (_surfaceBorderThickness != value) { _surfaceBorderThickness = value; Invalidate(); } }
    }

    /// <summary>Corner radius of the rounded outline, in pixels.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float SurfaceCornerRadius
    {
        get => _surfaceCornerRadius;
        set { if (_surfaceCornerRadius != value) { _surfaceCornerRadius = value; Invalidate(); } }
    }

    /// <summary>World distance between minor grid lines, in pixels.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double GridSpacing
    {
        get => _gridSpacing;
        set { if (_gridSpacing != value) { _gridSpacing = value; Invalidate(); } }
    }

    /// <summary>How many minor lines make a major one. Values below one are treated as one.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int MajorLineEvery
    {
        get => _majorLineEvery;
        set { if (_majorLineEvery != value) { _majorLineEvery = value; Invalidate(); } }
    }

    /// <summary>Thickness of the ruler band the surface reserves and its floating overlay draws, in pixels.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double RulerThickness
    {
        get => _rulerThickness;
        set { if (_rulerThickness != value) { _rulerThickness = value; Invalidate(); } }
    }

    /// <inheritdoc />
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetX
    {
        get => _scrollOffsetX;
        set { if (_scrollOffsetX != value) { _scrollOffsetX = value; Invalidate(); } }
    }

    /// <inheritdoc />
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetY
    {
        get => _scrollOffsetY;
        set { if (_scrollOffsetY != value) { _scrollOffsetY = value; Invalidate(); } }
    }

    /// <inheritdoc />
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetX
    {
        get => _contentOffsetX;
        set { if (_contentOffsetX != value) { _contentOffsetX = value; Invalidate(); } }
    }

    /// <inheritdoc />
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
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
        if (e is null) throw new ArgumentNullException(nameof(e));

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
        DrawSurfaceBorder(g, bounds);
    }

    private void DrawGrid(Graphics g, RectangleF bounds)
    {
        var spacing = Math.Max(8, _gridSpacing);
        var majorStep = spacing * Math.Max(1, _majorLineEvery);

        var worldLeft = WorkflowSurfaceMath.GridCanvasLeft(_scrollOffsetX, _contentOffsetX);
        var worldTop = WorkflowSurfaceMath.GridCanvasTop(_scrollOffsetY, _contentOffsetY);
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

    private void DrawSurfaceBorder(Graphics g, RectangleF bounds)
    {
        if (_surfaceBorderThickness <= 0)
        {
            return;
        }

        var inset = _surfaceBorderThickness / 2f;
        var borderBounds = new RectangleF(
            inset,
            inset,
            Math.Max(0, bounds.Width - _surfaceBorderThickness),
            Math.Max(0, bounds.Height - _surfaceBorderThickness));

        using var path = WorkflowSurfaceGraphics.RoundedRectangle(borderBounds, _surfaceCornerRadius);
        using var pen = new Pen(_surfaceBorderBrush, _surfaceBorderThickness);
        g.DrawPath(pen, path);
    }

    private static bool IsMajorLine(double value, double majorStep)
    {
        if (majorStep <= 0)
        {
            return false;
        }

        var remainder = value % majorStep;
        return Math.Abs(remainder) < MajorLineEpsilon
               || Math.Abs(remainder - majorStep) < MajorLineEpsilon
               || Math.Abs(remainder + majorStep) < MajorLineEpsilon;
    }

    private static bool IsNearZero(double value)
        => Math.Abs(value) < MajorLineEpsilon;

    private static Color Opaque(Color color) => Color.FromArgb(255, color);

    private static double ParseGridValue(string value)
    {
        var text = value.Trim();
        if (text.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 1);
        }

        return double.Parse(text, CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// The viewport: a XAML scrollable control that lets the grid decorator paint through it.
/// </summary>
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
/// The pool host: a XAML canvas that lets the grid decorator paint through it.
/// </summary>
public sealed class SurfaceCanvas : VeloxDev.WinForms.Xaml.Controls.Canvas
{
    /// <summary>Creates the canvas with a transparent background.</summary>
    public SurfaceCanvas()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
}
