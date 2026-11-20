// VeloxDev customization: the grid palette and spacing live here.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed class TemplateClass : Panel, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private const double DefaultRulerThickness = 36;

    private Color _gridBackground = WorkflowSurfaceColors.Parse("TemplateGridBackground");
    private Color _minorGridColor = WorkflowSurfaceColors.Parse("TemplateMinorGridColor");
    private Color _majorGridColor = WorkflowSurfaceColors.Parse("TemplateMajorGridColor");
    private Color _axisColor = WorkflowSurfaceColors.Parse("TemplateAxisColor");
    private Color _rulerBackground = WorkflowSurfaceColors.Parse("TemplateRulerBackground");
    private Color _rulerTickColor = WorkflowSurfaceColors.Parse("TemplateRulerTickColor");
    private Color _rulerLabelColor = WorkflowSurfaceColors.Parse("TemplateRulerLabelColor");
    private Color _rulerDividerColor = WorkflowSurfaceColors.Parse("TemplateRulerDividerColor");
    private Color _surfaceBorderBrush = WorkflowSurfaceColors.Parse("#33FFFFFF");
    private double _gridSpacing = ParseGridValue("TemplateGridSpacing");
    private int _majorLineEvery = int.Parse("TemplateMajorLineEvery", CultureInfo.InvariantCulture);
    private double _rulerThickness = DefaultRulerThickness;
    private double _scrollOffsetX;
    private double _scrollOffsetY;
    private double _contentOffsetX;
    private double _contentOffsetY;
    private float _surfaceBorderThickness = 1f;
    private float _surfaceCornerRadius = 3f;

    public TemplateClass()
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

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color MinorGridColor
    {
        get => _minorGridColor;
        set { if (_minorGridColor != value) { _minorGridColor = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color MajorGridColor
    {
        get => _majorGridColor;
        set { if (_majorGridColor != value) { _majorGridColor = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color AxisColor
    {
        get => _axisColor;
        set { if (_axisColor != value) { _axisColor = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color RulerBackground
    {
        get => _rulerBackground;
        set { _rulerBackground = value; }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color RulerTickColor
    {
        get => _rulerTickColor;
        set { _rulerTickColor = value; }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color RulerLabelColor
    {
        get => _rulerLabelColor;
        set { _rulerLabelColor = value; }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color RulerDividerColor
    {
        get => _rulerDividerColor;
        set { _rulerDividerColor = value; }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color SurfaceBorderBrush
    {
        get => _surfaceBorderBrush;
        set { if (_surfaceBorderBrush != value) { _surfaceBorderBrush = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float SurfaceBorderThickness
    {
        get => _surfaceBorderThickness;
        set { if (_surfaceBorderThickness != value) { _surfaceBorderThickness = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float SurfaceCornerRadius
    {
        get => _surfaceCornerRadius;
        set { if (_surfaceCornerRadius != value) { _surfaceCornerRadius = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double GridSpacing
    {
        get => _gridSpacing;
        set { if (_gridSpacing != value) { _gridSpacing = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int MajorLineEvery
    {
        get => _majorLineEvery;
        set { if (_majorLineEvery != value) { _majorLineEvery = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double RulerThickness
    {
        get => _rulerThickness;
        set { if (_rulerThickness != value) { _rulerThickness = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetX
    {
        get => _scrollOffsetX;
        set { if (_scrollOffsetX != value) { _scrollOffsetX = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetY
    {
        get => _scrollOffsetY;
        set { if (_scrollOffsetY != value) { _scrollOffsetY = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetX
    {
        get => _contentOffsetX;
        set { if (_contentOffsetX != value) { _contentOffsetX = value; Invalidate(); } }
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetY
    {
        get => _contentOffsetY;
        set { if (_contentOffsetY != value) { _contentOffsetY = value; Invalidate(); } }
    }

    public double RulerBand => RulerThickness;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        base.OnPaintBackground(e);
        Render(e.Graphics);
    }

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

public sealed class SurfaceScrollableControl : VeloxDev.WinForms.Xaml.Controls.ScrollableControl
{
    public SurfaceScrollableControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
}

public sealed class SurfaceCanvas : VeloxDev.WinForms.Xaml.Controls.Canvas
{
    public SurfaceCanvas()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
}
