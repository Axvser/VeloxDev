using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>Grid + ruler rendering for the node-editor surface (authoritative model, the Jalium
/// NodeEditorDemo): world-aligned grid lines mapped to canvas = world + origin, with axis / major /
/// minor pens. The ruler band is drawn viewport-fixed (absolute floating — ticks track the world
/// grid through origin + scroll) and semi-transparent, matching the NodeEditorDemo's floating rulers
/// while living in the same component as the grid like the other GUI adapters.</summary>
public static class TemplateClass
{
    public const double GridStep = TemplateGridSpacing;
    public const double MajorStep = TemplateGridSpacing * TemplateMajorLineEvery;
    public const double RulerThickness = 36;

    private static readonly SolidColorBrush s_gridMinor = new((Color)ColorConverter.ConvertFromString("TemplateMinorGridColor"));
    private static readonly SolidColorBrush s_gridMajor = new((Color)ColorConverter.ConvertFromString("TemplateMajorGridColor"));
    private static readonly SolidColorBrush s_axisBrush = new((Color)ColorConverter.ConvertFromString("TemplateAxisColor"));
    private static readonly SolidColorBrush s_rulerBg = new((Color)ColorConverter.ConvertFromString("TemplateRulerBackground"));
    private static readonly SolidColorBrush s_rulerLabel = new((Color)ColorConverter.ConvertFromString("TemplateRulerLabelColor"));
    private static readonly SolidColorBrush s_rulerTick = new((Color)ColorConverter.ConvertFromString("TemplateRulerTickColor"));
    private static readonly SolidColorBrush s_rulerDivider = new((Color)ColorConverter.ConvertFromString("TemplateRulerDividerColor"));

    private static readonly Pen s_minorPen = new(s_gridMinor, 1);
    private static readonly Pen s_majorPen = new(s_gridMajor, 1);
    private static readonly Pen s_axisPen = new(s_axisBrush, 1.2);
    private static readonly Pen s_tickPen = new(s_rulerTick, 1);
    private static readonly Pen s_dividerPen = new(s_rulerDivider, 1);

    /// <summary>Draws the world grid into <paramref name="dc"/>. <paramref name="originX"/> /
    /// <paramref name="originY"/> is the world-origin translate (the surface's OriginX/OriginY).
    /// Call in the surface's OnRender — the grid lives in the canvas and scrolls with it.</summary>
    public static void DrawGrid(DrawingContext dc, double originX, double originY, double width, double height)
    {
        double worldLeft = -originX;
        double worldRight = worldLeft + width;
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldLeft, GridStep); g <= worldRight; g += GridStep)
        {
            double x = g + originX;
            Pen pen = g == 0 ? s_axisPen : (Math.Abs(g % MajorStep) < 0.001 ? s_majorPen : s_minorPen);
            dc.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }

        double worldTop = -originY;
        double worldBottom = worldTop + height;
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldTop, GridStep); g <= worldBottom; g += GridStep)
        {
            double y = g + originY;
            Pen pen = g == 0 ? s_axisPen : (Math.Abs(g % MajorStep) < 0.001 ? s_majorPen : s_minorPen);
            dc.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }

    /// <summary>Draws the ruler bands fixed at the viewport top-left (absolute floating — they do
    /// NOT scroll with the canvas). <paramref name="scrollX"/>/<paramref name="scrollY"/> is the
    /// scroll offset (= the viewport's top-left in canvas coordinates); ticks are drawn at the world
    /// grid lines that cross the viewport. Call in the surface's OnPostRender so the bands sit on
    /// top of the node/link views.</summary>
    public static void DrawRulers(DrawingContext dc, double originX, double originY, double scrollX, double scrollY, double viewportWidth, double viewportHeight)
    {
        const double ruler = RulerThickness;
        dc.DrawRectangle(s_rulerBg, null, new Rect(scrollX, scrollY, viewportWidth, ruler));
        dc.DrawRectangle(s_rulerBg, null, new Rect(scrollX, scrollY, ruler, viewportHeight));
        dc.DrawLine(s_dividerPen, new Point(scrollX + ruler, scrollY), new Point(scrollX + ruler, scrollY + viewportHeight));
        dc.DrawLine(s_dividerPen, new Point(scrollX, scrollY + ruler), new Point(scrollX + viewportWidth, scrollY + ruler));

        // Top ruler: ticks at world grid x crossing the viewport, canvas x = world + originX.
        double worldLeft = WorkflowSurfaceMath.GridWorldLeft(scrollX, originX);
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldLeft, GridStep); g + originX <= scrollX + viewportWidth; g += GridStep)
        {
            double x = g + originX;
            if (x < scrollX + ruler) continue;
            bool major = IsMajor(g);
            double tick = major ? ruler - 6 : Math.Max(6, ruler * 0.35);
            Pen pen = IsNearZero(g) ? s_axisPen : s_tickPen;
            dc.DrawLine(pen, new Point(x, scrollY + ruler), new Point(x, scrollY + ruler - tick));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13) { Foreground = s_rulerLabel };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(x + 3, scrollY + 2));
            }
        }

        // Left ruler: ticks at world grid y crossing the viewport, canvas y = world + originY.
        double worldTop = WorkflowSurfaceMath.GridWorldTop(scrollY, originY);
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldTop, GridStep); g + originY <= scrollY + viewportHeight; g += GridStep)
        {
            double y = g + originY;
            if (y < scrollY + ruler) continue;
            bool major = IsMajor(g);
            double tick = major ? ruler - 6 : Math.Max(6, ruler * 0.35);
            Pen pen = IsNearZero(g) ? s_axisPen : s_tickPen;
            dc.DrawLine(pen, new Point(scrollX + ruler, y), new Point(scrollX + ruler - tick, y));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13) { Foreground = s_rulerLabel };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(scrollX + 3, y + 2));
            }
        }
    }

    private static bool IsMajor(double g) => Math.Abs(g % MajorStep) < 0.001;
    private static bool IsNearZero(double g) => Math.Abs(g) < 0.001;

    private static string Format(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }
}
