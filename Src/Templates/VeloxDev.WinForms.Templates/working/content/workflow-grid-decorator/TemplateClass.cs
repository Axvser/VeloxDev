using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A lightweight workflow grid that draws a dotted background, axis, and rulers.
/// Implements <see cref="IWorkflowGridDecorator"/> for automatic offset updates
/// from <see cref="WorkflowSurfaceBehavior"/>.
/// </summary>
public sealed class TemplateClass : Panel, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;
    // Other template code uses 28px, but WinForms reads visually smaller, so the default is enlarged to 36px.
    private const double DefaultRulerThickness = 36;

    private readonly Color _background = ParseColor("TemplateGridBackground");
    private readonly Color _rulerBackground = ParseColor("TemplateRulerBackground");
    private readonly Color _labelColor = ParseColor("TemplateRulerLabelColor");
    private readonly Color _minorGridColor = ParseColor("TemplateMinorGridColor");
    private readonly Color _majorGridColor = ParseColor("TemplateMajorGridColor");
    private readonly Color _axisColor = ParseColor("TemplateAxisColor");
    private readonly Color _tickColor = ParseColor("TemplateRulerTickColor");
    private readonly Color _dividerColor = ParseColor("TemplateRulerDividerColor");
    private readonly double _gridSpacing = ParseGridValue("TemplateGridSpacing");
    private readonly int _majorLineEvery = int.Parse("TemplateMajorLineEvery", CultureInfo.InvariantCulture);
    private readonly Font _labelFont = new("Segoe UI", 13f, GraphicsUnit.Pixel);

    public TemplateClass()
    {
        DoubleBuffered = true;
        BackColor = _background;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RulerThickness { get; set; } = DefaultRulerThickness;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetX { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetY { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetX { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetY { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        // Draw the entire surface here: WinForms transparent children (the tree
        // view's scroll viewport, canvas, links host) composite the parent's
        // OnPaintBackground — never its OnPaint — so the grid and rulers must be
        // painted in this method to show through the viewport.
        Render(e.Graphics);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Surface rendering lives in OnPaintBackground; see the note there.
    }

    private void Render(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0, 0, Width, Height);
        var ruler = Math.Max(0, RulerThickness);

        using var bgBrush = new SolidBrush(_background);
        using var rulerBrush = new SolidBrush(_rulerBackground);
        g.FillRectangle(bgBrush, bounds);

        // Full-area grid (no contentRect clip) so lines extend under the translucent
        // bands; the bands are filled after, dimming whatever scrolls beneath them (the
        // Jalium floating-ruler model). The grid keeps the +ruler offset because a
        // standalone decorator sits below a content viewport that is translated by
        // RulerThickness, keeping the world origin at the band edge.
        DrawGrid(g, bounds, ruler);

        g.FillRectangle(rulerBrush, 0, 0, bounds.Width, (float)ruler);
        g.FillRectangle(rulerBrush, 0, 0, (float)ruler, bounds.Height);

        DrawRulers(g, bounds, ruler);
    }

    private void DrawGrid(Graphics g, RectangleF bounds, double ruler)
    {
        var spacing = Math.Max(8, _gridSpacing);
        var majorStep = spacing * Math.Max(1, _majorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;
        var worldRight = worldLeft + bounds.Width;
        var worldBottom = worldTop + bounds.Height;

        using var minorPen = new Pen(_minorGridColor, 1f);
        using var majorPen = new Pen(_majorGridColor, 1f);
        using var axisPen = new Pen(_axisColor, 1.2f);

        // Grid x = ruler + (value - worldLeft): the standalone decorator draws the world
        // grid beneath a content viewport translated by RulerThickness, so the origin stays
        // at the band edge. Lines span the full viewport so they extend under the bands.
        var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = (float)(ruler + (value - worldLeft));
            var pen = SelectPen(value, majorStep, minorPen, majorPen, axisPen);
            g.DrawLine(pen, x, 0, x, bounds.Height);
        }

        var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = (float)(ruler + (value - worldTop));
            var pen = SelectPen(value, majorStep, minorPen, majorPen, axisPen);
            g.DrawLine(pen, 0, y, bounds.Width, y);
        }
    }

    private void DrawRulers(Graphics g, RectangleF bounds, double ruler)
    {
        var spacing = Math.Max(8, _gridSpacing);
        var majorStep = spacing * Math.Max(1, _majorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;
        var worldRight = worldLeft + bounds.Width;
        var worldBottom = worldTop + bounds.Height;

        using var dividerPen = new Pen(_dividerColor, 1f);
        using var tickPen = new Pen(_tickColor, 1f);
        using var axisPen = new Pen(_axisColor, 1f);
        using var labelBrush = new SolidBrush(_labelColor);
        using var format = new StringFormat(StringFormat.GenericTypographic);

        g.DrawLine(dividerPen, (float)ruler, 0, (float)ruler, bounds.Height);
        g.DrawLine(dividerPen, 0, (float)ruler, bounds.Width, (float)ruler);

        // Top ruler. Ticks share the grid's x = ruler + (value - worldLeft). Skip
        // x < ruler so the corner junction and left band stay clean (no ticks/labels).
        var saved = g.Save();
        g.SetClip(new RectangleF((float)ruler, 0, Math.Max(0, bounds.Width - (float)ruler), (float)ruler));
        var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = (float)(ruler + (value - worldLeft));
            if (x < ruler)
            {
                continue;
            }

            var isMajor = IsMajorLine(value, majorStep);
            var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
            var pen = IsNearZero(value) ? axisPen : tickPen;
            g.DrawLine(pen, x, (float)ruler, x, (float)(ruler - tickLength));

            if (isMajor)
            {
                var text = FormatGridValue(value);
                var size = g.MeasureString(text, _labelFont);
                g.DrawString(text, _labelFont, labelBrush, x + 3, 2, format);
            }
        }
        g.Restore(saved);

        // Left ruler.
        saved = g.Save();
        g.SetClip(new RectangleF(0, (float)ruler, (float)ruler, Math.Max(0, bounds.Height - (float)ruler)));
        var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = (float)(ruler + (value - worldTop));
            if (y < ruler)
            {
                continue;
            }

            var isMajor = IsMajorLine(value, majorStep);
            var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
            var pen = IsNearZero(value) ? axisPen : tickPen;
            g.DrawLine(pen, (float)ruler, y, (float)(ruler - tickLength), y);

            if (isMajor)
            {
                var text = FormatGridValue(value);
                var size = g.MeasureString(text, _labelFont);
                g.DrawString(text, _labelFont, labelBrush, 3, y + 2, format);
            }
        }
        g.Restore(saved);
    }

    private Pen SelectPen(double value, double majorStep, Pen minorPen, Pen majorPen, Pen axisPen)
        => IsNearZero(value) ? axisPen : IsMajorLine(value, majorStep) ? majorPen : minorPen;

    private static bool IsMajorLine(double value, double majorStep)
        => majorStep > 0
            && (Math.Abs(value % majorStep) < MajorLineEpsilon
                || Math.Abs(value % majorStep - majorStep) < MajorLineEpsilon
                || Math.Abs(value % majorStep + majorStep) < MajorLineEpsilon);

    private static bool IsNearZero(double value)
        => Math.Abs(value) < MajorLineEpsilon;

    private static string FormatGridValue(double value)
    {
        var abs = Math.Abs(value);
        if (abs < 10000)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        if (abs < 1000000)
        {
            return Math.Round(value / 1000d, 1).ToString(CultureInfo.InvariantCulture) + "K";
        }

        return Math.Round(value / 1000000d, 1).ToString(CultureInfo.InvariantCulture) + "M";
    }

    private static double ParseGridValue(string value)
    {
        var text = value.Trim();
        if (text.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 1);
        }

        return double.Parse(text, CultureInfo.InvariantCulture);
    }

    private static Color ParseColor(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var digits = value.Substring(1);
            if (digits.Length == 8)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            if (digits.Length == 6)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        return Color.FromName(value);
    }
}
