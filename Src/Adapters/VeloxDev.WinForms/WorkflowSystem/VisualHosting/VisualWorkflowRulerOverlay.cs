using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using DrawingPointF = System.Drawing.PointF;
using DrawingSize = System.Drawing.Size;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// The floating translucent ruler bands on the top and left edges of a visual workflow surface.
/// </summary>
/// <remarks>
/// The bands sit above the canvas in the surface composition, so cards, links and the grid scroll underneath
/// them. Ticks are taken from the same world grid the backdrop paints, which keeps them aligned at every pan and
/// zoom. The overlay never takes a pointer hit.
/// </remarks>
public sealed class VisualWorkflowRulerOverlay : VisualElement
{
    private readonly Dictionary<float, Font> _autoFonts = [];
    private DrawingPointF _pan;

    /// <summary>Creates the ruler overlay with the adapter's dark palette.</summary>
    public VisualWorkflowRulerOverlay()
    {
        IsHitTestVisible = false;
        BandColor = Color.FromArgb(0xC8, 0x25, 0x25, 0x26);
        TickColor = Color.FromArgb(0xFF, 0x55, 0x55, 0x55);
        DividerColor = Color.FromArgb(0xFF, 0x3A, 0x3D, 0x40);
        LabelColor = Color.FromArgb(0xFF, 0x88, 0x88, 0x88);
        AxisColor = Color.FromArgb(0xFF, 0x4D, 0x4D, 0x4D);
    }

    /// <summary>Thickness of the top and left bands, in pixels.</summary>
    public double RulerThickness { get; set; } = 28d;

    /// <summary>World distance between minor ticks, in pixels.</summary>
    public double GridSpacing { get; set; } = 40d;

    /// <summary>How many minor ticks make one labelled tick.</summary>
    public int MajorLineEvery { get; set; } = 5;

    /// <summary>The fill of the two bands.</summary>
    public Color BandColor { get; set; }

    /// <summary>The colour of a minor tick.</summary>
    public Color TickColor { get; set; }

    /// <summary>The colour of the two band dividers.</summary>
    public Color DividerColor { get; set; }

    /// <summary>The colour of the major-tick labels.</summary>
    public Color LabelColor { get; set; }

    /// <summary>The colour of the tick at world zero.</summary>
    public Color AxisColor { get; set; }

    /// <summary>The font used for major-tick labels; <see langword="null"/> derives a size from the band thickness.</summary>
    public Font? LabelFont { get; set; }

    /// <summary>The left band's width from the last paint; always <see cref="RulerThickness"/>.</summary>
    public float LeftBandWidth { get; private set; }

    /// <summary>The signed canvas pan, in pixels; setting it repaints the rulers.</summary>
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

    /// <summary>Requests a repaint after a palette or spacing setting changed.</summary>
    public void Refresh() => InvalidateVisual();

    /// <inheritdoc />
    protected override DrawingSize MeasureOverride(DrawingSize availableSize) => availableSize;

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        var ruler = (float)Math.Max(0d, RulerThickness);
        if (width <= 0f || height <= 0f || ruler <= 0f)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var font = LabelFontForRender();

        var spacing = Math.Max(8d, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = WorkflowSurfaceMath.GridCanvasLeft(-_pan.X, 0d);
        var worldTop = WorkflowSurfaceMath.GridCanvasTop(-_pan.Y, 0d);
        var worldRight = worldLeft + width;
        var worldBottom = worldTop + height;

        // Keep the labels readable instead of fixed to every major line: the interval grows with the label's
        // measured size, so a smaller band / a denser grid can never produce overlapping text.
        var sampleWidth = graphics.MeasureString(
            WorkflowSurfaceGrid.FormatGridValue(majorStep),
            font).Width;
        var labelEveryX = Math.Max(1, (int)Math.Ceiling((sampleWidth + 14f) / (float)majorStep));
        var labelHeight = graphics.MeasureString("0", font).Height;
        var labelEveryY = Math.Max(1, (int)Math.Ceiling((labelHeight + 6f) / (float)majorStep));

        var leftBand = ruler;
        LeftBandWidth = leftBand;

        using (var band = new SolidBrush(BandColor))
        {
            graphics.FillRectangle(band, 0f, 0f, width, ruler);
            graphics.FillRectangle(band, 0f, 0f, leftBand, height);
        }

        using (var divider = new Pen(DividerColor, 1f))
        {
            graphics.DrawLine(divider, leftBand, 0f, leftBand, height);
            graphics.DrawLine(divider, 0f, ruler, width, ruler);
        }

        using var tick = new Pen(TickColor, 1f);
        using var axis = new Pen(AxisColor, 1.2f);
        using var label = new SolidBrush(LabelColor);

        var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, 0d);
            if (x < leftBand)
            {
                continue;
            }

            var major = WorkflowSurfaceGrid.IsMajorLine(value, majorStep);
            var length = major ? ruler - 6f : Math.Max(6f, ruler * 0.35f);
            graphics.DrawLine(WorkflowSurfaceGrid.IsNearZero(value) ? axis : tick, x, ruler, x, ruler - length);

            if (major && LabelIsDue(value, majorStep, labelEveryX))
            {
                graphics.DrawString(WorkflowSurfaceGrid.FormatGridValue(value), font, label, x + 3f, 2f);
            }
        }

        var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, 0d);
            if (y < ruler)
            {
                continue;
            }

            var major = WorkflowSurfaceGrid.IsMajorLine(value, majorStep);
            var length = major ? ruler - 6f : Math.Max(6f, ruler * 0.35f);
            graphics.DrawLine(WorkflowSurfaceGrid.IsNearZero(value) ? axis : tick, leftBand, y, leftBand - length, y);

            if (major && LabelIsDue(value, majorStep, labelEveryY))
            {
                DrawLeftLabel(graphics, FormatLeftLabel(value), font, label, leftBand, y);
            }
        }
    }

    // The left band must stay the same thickness as the top one. The label is always horizontal, like the
    // WPF/Avalonia rulers; it abbreviates to K/M earlier and, if still too wide, is drawn a size smaller.
    private void DrawLeftLabel(Graphics graphics, string text, Font font, Brush brush, float band, float y)
    {
        var fitted = LabelFont is null
            ? FitLeftFont(graphics, text, font, band - 2f)
            : font;
        graphics.DrawString(text, fitted, brush, 1f, y + 2f);
    }

    private Font FitLeftFont(Graphics graphics, string text, Font font, float maxWidth)
    {
        var size = font.Size;
        while (size > 7f)
        {
            var candidate = AutoFont(size);
            if (graphics.MeasureString(text, candidate).Width <= maxWidth)
            {
                return candidate;
            }

            size -= 0.5f;
        }

        return AutoFont(7f);
    }

    private static string FormatLeftLabel(double value)
    {
        var abs = Math.Abs(value);
        if (abs < 1000d)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        if (abs < 100000d)
        {
            return (value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "K";
        }

        if (abs < 999500d)
        {
            return (value / 1000d).ToString("0", CultureInfo.InvariantCulture) + "K";
        }

        return (value / 1000000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var font in _autoFonts.Values)
            {
                font.Dispose();
            }

            _autoFonts.Clear();
        }

        base.Dispose(disposing);
    }

    private Font LabelFontForRender()
    {
        if (LabelFont is { } custom)
        {
            return custom;
        }

        // Points, not pixels: the font follows the surface DPI while its size stays tied to the band.
        var size = (float)Math.Clamp(RulerThickness * 0.27d, 8d, 12d);
        return AutoFont(size);
    }

    private Font AutoFont(float size)
    {
        if (!_autoFonts.TryGetValue(size, out var font))
        {
            font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
            _autoFonts[size] = font;
        }

        return font;
    }

    private static bool LabelIsDue(double value, double majorStep, int every)
    {
        if (every <= 1)
        {
            return true;
        }

        var index = (long)Math.Round(value / majorStep);
        return index % every == 0;
    }

}
