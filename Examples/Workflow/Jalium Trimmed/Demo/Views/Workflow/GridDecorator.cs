using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Demo.Views.Workflow;

/// <summary>Grid + ruler rendering for the node-editor surface (authoritative model, the Jalium
/// NodeEditorDemo): world-aligned grid lines mapped to canvas = world + origin, with axis / major /
/// minor pens. The ruler band is drawn viewport-fixed (absolute floating — ticks track the world
/// grid through origin + scroll) and semi-transparent, matching the NodeEditorDemo's floating rulers
/// while living in the same component as the grid like the other GUI adapters.</summary>
public static class GridDecorator
{
    public const double GridStep = 40;
    public const double MajorStep = 200;
    public const double RulerThickness = 36;

    private static readonly SolidColorBrush s_gridMinor = new(Color.FromRgb(0x2A, 0x2D, 0x2E));
    private static readonly SolidColorBrush s_gridMajor = new(Color.FromRgb(0x3A, 0x3D, 0x40));
    private static readonly SolidColorBrush s_axisBrush = new(Color.FromRgb(0x4D, 0x4D, 0x4D));
    private static readonly SolidColorBrush s_rulerBg = new(Color.FromArgb(0xC8, 0x2D, 0x2D, 0x30));
    private static readonly SolidColorBrush s_rulerLabel = new(Color.FromRgb(0xC8, 0xC8, 0xC8));
    private static readonly SolidColorBrush s_rulerTick = new(Color.FromRgb(0x6E, 0x6E, 0x6E));
    private static readonly SolidColorBrush s_rulerDivider = new(Color.FromRgb(0x4D, 0x4D, 0x4D));

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
        for (double g = Math.Floor(worldLeft / GridStep) * GridStep; g <= worldRight; g += GridStep)
        {
            double x = g + originX;
            Pen pen = g == 0 ? s_axisPen : (Math.Abs(g % MajorStep) < 0.001 ? s_majorPen : s_minorPen);
            dc.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }

        double worldTop = -originY;
        double worldBottom = worldTop + height;
        for (double g = Math.Floor(worldTop / GridStep) * GridStep; g <= worldBottom; g += GridStep)
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
    public static void DrawRulers(DrawingContext dc, double originX, double originY, double scale, double scrollX, double scrollY, double viewportWidth, double viewportHeight)
    {
        // Under the scale-only canvas transform the bands are drawn in canvas-local units
        // (scroll/scale position, thickness/scale, font/scale) so they appear as a fixed-size
        // viewport overlay while the ticks stay aligned with the scaled grid.
        const double ruler = RulerThickness;
        double rs = ruler / scale;
        double vx = scrollX / scale, vy = scrollY / scale;
        double vw = viewportWidth / scale, vh = viewportHeight / scale;

        dc.DrawRectangle(s_rulerBg, null, new Rect(vx, vy, vw, rs));
        dc.DrawRectangle(s_rulerBg, null, new Rect(vx, vy, rs, vh));
        dc.DrawLine(s_dividerPen, new Point(vx + rs, vy), new Point(vx + rs, vy + vh));
        dc.DrawLine(s_dividerPen, new Point(vx, vy + rs), new Point(vx + vw, vy + rs));

        // Top ruler: ticks at world grid x crossing the viewport, canvas x = world + originX.
        double worldLeft = vx - originX;
        for (double g = Math.Floor(worldLeft / GridStep) * GridStep; g + originX <= vx + vw; g += GridStep)
        {
            double x = g + originX;
            if (x < vx + rs) continue;
            bool major = IsMajor(g);
            double tick = (major ? ruler - 6 : Math.Max(6, ruler * 0.35)) / scale;
            Pen pen = IsNearZero(g) ? s_axisPen : s_tickPen;
            dc.DrawLine(pen, new Point(x, vy + rs), new Point(x, vy + rs - tick));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13 / scale) { Foreground = s_rulerLabel };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(x + 3 / scale, vy + 2 / scale));
            }
        }

        // Left ruler: ticks at world grid y crossing the viewport, canvas y = world + originY.
        double worldTop = vy - originY;
        for (double g = Math.Floor(worldTop / GridStep) * GridStep; g + originY <= vy + vh; g += GridStep)
        {
            double y = g + originY;
            if (y < vy + rs) continue;
            bool major = IsMajor(g);
            double tick = (major ? ruler - 6 : Math.Max(6, ruler * 0.35)) / scale;
            Pen pen = IsNearZero(g) ? s_axisPen : s_tickPen;
            dc.DrawLine(pen, new Point(vx + rs, y), new Point(vx + rs - tick, y));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13 / scale) { Foreground = s_rulerLabel };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(vx + 3 / scale, y + 2 / scale));
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
