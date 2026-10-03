using System;
using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Paints the workflow grid and the two floating ruler bands.
/// </summary>
/// <remarks>
/// <para>
/// The grid is world-aligned and scrolls with the canvas; the ruler bands are viewport-fixed and are drawn after
/// the node and link views so they float above them. Derive from it for the palette and the spacing — the grid
/// line maths, the tick layout and the label formatting are the same in every host.
/// </para>
/// <para>
/// Brushes and pens are cached and rebuilt when a colour changes, because a surface redraws these every frame.
/// </para>
/// </remarks>
public class WorkflowGridDecorator
{
    /// <summary>Ruler band thickness, in pixels.</summary>
    public const double RulerThickness = 36;

    private Color _minorGridColor = Color.FromArgb(0xFF, 0x2A, 0x2D, 0x2E);
    private Color _majorGridColor = Color.FromArgb(0xFF, 0x3A, 0x3D, 0x40);
    private Color _axisColor = Color.FromArgb(0xFF, 0x4D, 0x4D, 0x4D);
    private Color _rulerBackground = Color.FromArgb(0xC8, 0x25, 0x25, 0x26);
    private Color _rulerLabelColor = Color.FromArgb(0xFF, 0x88, 0x88, 0x88);
    private Color _rulerTickColor = Color.FromArgb(0xFF, 0x55, 0x55, 0x55);
    private Color _rulerDividerColor = Color.FromArgb(0xFF, 0x3A, 0x3D, 0x40);

    private double _gridStep = 40;
    private int _majorLineEvery = 5;

    private Pen? _minorPen, _majorPen, _axisPen, _tickPen, _dividerPen;
    private SolidColorBrush? _rulerLabelBrush;

    /// <summary>Colour of the minor grid lines.</summary>
    public Color MinorGridColor
    {
        get => _minorGridColor;
        set { _minorGridColor = value; InvalidateResources(); }
    }

    /// <summary>Colour of the major grid lines.</summary>
    public Color MajorGridColor
    {
        get => _majorGridColor;
        set { _majorGridColor = value; InvalidateResources(); }
    }

    /// <summary>Colour of the world axes.</summary>
    public Color AxisColor
    {
        get => _axisColor;
        set { _axisColor = value; InvalidateResources(); }
    }

    /// <summary>Fill of the two ruler bands.</summary>
    public Color RulerBackground
    {
        get => _rulerBackground;
        set { _rulerBackground = value; InvalidateResources(); }
    }

    /// <summary>Colour of the ruler labels.</summary>
    public Color RulerLabelColor
    {
        get => _rulerLabelColor;
        set { _rulerLabelColor = value; InvalidateResources(); }
    }

    /// <summary>Colour of the ruler ticks.</summary>
    public Color RulerTickColor
    {
        get => _rulerTickColor;
        set { _rulerTickColor = value; InvalidateResources(); }
    }

    /// <summary>Colour of the lines dividing each band from the grid.</summary>
    public Color RulerDividerColor
    {
        get => _rulerDividerColor;
        set { _rulerDividerColor = value; InvalidateResources(); }
    }

    /// <summary>World distance between minor grid lines, in pixels.</summary>
    public double GridStep
    {
        get => _gridStep;
        set => _gridStep = value;
    }

    /// <summary>How many minor lines make a major one. Values below one are treated as one.</summary>
    public int MajorLineEvery
    {
        get => _majorLineEvery;
        set => _majorLineEvery = value;
    }

    /// <summary>World distance between major grid lines.</summary>
    public double MajorStep => _gridStep * Math.Max(1, _majorLineEvery);

    /// <summary>
    /// Draws the world grid.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    /// <param name="originX">The world-origin translate, X.</param>
    /// <param name="originY">The world-origin translate, Y.</param>
    /// <param name="width">The canvas width.</param>
    /// <param name="height">The canvas height.</param>
    /// <remarks>Call from the surface's render pass: the grid lives in the canvas and scrolls with it.</remarks>
    public void DrawGrid(DrawingContext dc, double originX, double originY, double width, double height)
    {
        EnsureResources();

        double worldLeft = -originX;
        double worldRight = worldLeft + width;
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldLeft, _gridStep); g <= worldRight; g += _gridStep)
        {
            double x = g + originX;
            Pen pen = g == 0 ? _axisPen! : (Math.Abs(g % MajorStep) < 0.001 ? _majorPen! : _minorPen!);
            dc.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }

        double worldTop = -originY;
        double worldBottom = worldTop + height;
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldTop, _gridStep); g <= worldBottom; g += _gridStep)
        {
            double y = g + originY;
            Pen pen = g == 0 ? _axisPen! : (Math.Abs(g % MajorStep) < 0.001 ? _majorPen! : _minorPen!);
            dc.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }

    /// <summary>
    /// Draws the two ruler bands, fixed at the viewport's top-left.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    /// <param name="originX">The world-origin translate, X.</param>
    /// <param name="originY">The world-origin translate, Y.</param>
    /// <param name="scrollX">The viewport's scroll offset, X — its top-left in canvas coordinates.</param>
    /// <param name="scrollY">The viewport's scroll offset, Y.</param>
    /// <param name="viewportWidth">The viewport width.</param>
    /// <param name="viewportHeight">The viewport height.</param>
    /// <remarks>
    /// Call after the node and link views are drawn, so the bands sit on top of them; they deliberately do not
    /// scroll with the canvas.
    /// </remarks>
    public void DrawRulers(
        DrawingContext dc,
        double originX, double originY,
        double scrollX, double scrollY,
        double viewportWidth, double viewportHeight)
    {
        EnsureResources();

        const double ruler = RulerThickness;
        var bg = new SolidColorBrush(_rulerBackground);
        dc.DrawRectangle(bg, null, new Rect(scrollX, scrollY, viewportWidth, ruler));
        dc.DrawRectangle(bg, null, new Rect(scrollX, scrollY, ruler, viewportHeight));
        dc.DrawLine(_dividerPen!, new Point(scrollX + ruler, scrollY), new Point(scrollX + ruler, scrollY + viewportHeight));
        dc.DrawLine(_dividerPen!, new Point(scrollX, scrollY + ruler), new Point(scrollX + viewportWidth, scrollY + ruler));

        // 上标尺：刻度落在穿过视口的世界网格线上，画布 x = world + originX。
        double worldLeft = WorkflowSurfaceMath.GridWorldLeft(scrollX, originX);
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldLeft, _gridStep); g + originX <= scrollX + viewportWidth; g += _gridStep)
        {
            double x = g + originX;
            if (x < scrollX + ruler) continue;
            bool major = IsMajor(g);
            double tick = major ? ruler - 6 : Math.Max(6, ruler * 0.35);
            Pen pen = IsNearZero(g) ? _axisPen! : _tickPen!;
            dc.DrawLine(pen, new Point(x, scrollY + ruler), new Point(x, scrollY + ruler - tick));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13) { Foreground = _rulerLabelBrush! };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(x + 3, scrollY + 2));
            }
        }

        // 左标尺：刻度落在穿过视口的世界网格线上，画布 y = world + originY。
        double worldTop = WorkflowSurfaceMath.GridWorldTop(scrollY, originY);
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldTop, _gridStep); g + originY <= scrollY + viewportHeight; g += _gridStep)
        {
            double y = g + originY;
            if (y < scrollY + ruler) continue;
            bool major = IsMajor(g);
            double tick = major ? ruler - 6 : Math.Max(6, ruler * 0.35);
            Pen pen = IsNearZero(g) ? _axisPen! : _tickPen!;
            dc.DrawLine(pen, new Point(scrollX + ruler, y), new Point(scrollX + ruler - tick, y));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13) { Foreground = _rulerLabelBrush! };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(scrollX + 3, y + 2));
            }
        }
    }

    private void InvalidateResources()
    {
        _minorPen = _majorPen = _axisPen = _tickPen = _dividerPen = null;
        _rulerLabelBrush = null;
    }

    private void EnsureResources()
    {
        if (_minorPen is not null) return;

        _minorPen = new Pen(new SolidColorBrush(_minorGridColor), 1);
        _majorPen = new Pen(new SolidColorBrush(_majorGridColor), 1);
        _axisPen = new Pen(new SolidColorBrush(_axisColor), 1.2);
        _tickPen = new Pen(new SolidColorBrush(_rulerTickColor), 1);
        _dividerPen = new Pen(new SolidColorBrush(_rulerDividerColor), 1);
        _rulerLabelBrush = new SolidColorBrush(_rulerLabelColor);
    }

    private bool IsMajor(double g) => Math.Abs(g % MajorStep) < 0.001;

    private static bool IsNearZero(double g) => Math.Abs(g) < 0.001;

    private static string Format(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }
}
