using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Pure, GUI-agnostic curve/connection geometry shared by every workflow demo's link view
/// (WPF, WinUI, Avalonia, MAUI, WinForms, Jalium, Blazor). The golden-ratio polyline stub,
/// the arrowhead triangle, the cubic Bezier point evaluator and the point-to-segment
/// hit-test previously lived as 6–16 near-identical copies in the demo projects; these
/// helpers own the single canonical copy. All methods operate on plain doubles/tuples so
/// Core carries no GUI-framework dependency.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "工作流连接线纯几何数学：折线黄金比 stub、箭头头、三次贝塞尔求值、点到线段距离、贝塞尔控制点")]
[AgentContext(AgentLanguages.English, "Pure workflow-link geometry: polyline golden-ratio stub, arrowhead, cubic Bezier evaluation, point-to-segment distance, Bezier control points")]
public static class WorkflowCurveMath
{
    /// <summary>The golden-ratio conjugate used for the polyline stub: <c>dx/2 · (1 − φ)</c>.</summary>
    public const double GoldenRatio = 0.6180339887;

    /// <summary>
    /// Builds the 4-point orthogonal polyline for a connection: the classic golden-ratio stub
    /// <c>stub = dx/2 · (1 − φ)</c> (floored to <paramref name="minStub"/>, as Jalium does with 8)
    /// yields <c>[start, (sx+stub, sy), (ex−stub, ey), end]</c>.
    /// </summary>
    public static (double X, double Y)[] PolylineStubPoints(
        double sx, double sy, double ex, double ey,
        double phi = GoldenRatio, double minStub = 0)
    {
        double stub = (ex - sx) / 2.0 * (1.0 - phi);
        if (stub < minStub) stub = minStub;
        return new[] { (sx, sy), (sx + stub, sy), (ex - stub, ey), (ex, ey) };
    }

    /// <summary>
    /// Evaluates a cubic Bezier at <paramref name="t"/> ∈ [0,1]:
    /// <c>B(t) = mt³·p0 + 3mt²t·cp1 + 3mtt²·cp2 + t³·p3</c> with <c>mt = 1−t</c>.
    /// </summary>
    public static (double X, double Y) CubicBezierPoint(
        double t,
        double p0x, double p0y,
        double cp1x, double cp1y,
        double cp2x, double cp2y,
        double p3x, double p3y)
    {
        double mt = 1 - t;
        double x = mt * mt * mt * p0x + 3 * mt * mt * t * cp1x + 3 * mt * t * t * cp2x + t * t * t * p3x;
        double y = mt * mt * mt * p0y + 3 * mt * mt * t * cp1y + 3 * mt * t * t * cp2y + t * t * t * p3y;
        return (x, y);
    }

    /// <summary>
    /// Computes the axis-aligned Bezier control points for a connection using the shared
    /// <c>bend</c> shape constants. WPF/WinUI use (0.618, 0.1); Avalonia uses (0.3, 0) — the
    /// caller picks its own look, the formula is unified: <c>cp1 = (sx + dx·bx, sy + dy·by)</c>,
    /// <c>cp2 = (ex − dx·bx, ey − dy·by)</c>.
    /// </summary>
    public static (double Cp1X, double Cp1Y, double Cp2X, double Cp2Y) BezierControlPoints(
        double sx, double sy, double ex, double ey, double bendX, double bendY)
    {
        double dx = ex - sx, dy = ey - sy;
        return (sx + dx * bendX, sy + dy * bendY, ex - dx * bendX, ey - dy * bendY);
    }

    /// <summary>
    /// Computes the arrowhead base and perpendicular for a triangle pointing from the segment's
    /// <c>from</c> point toward its <c>tip</c>. Callers place <c>base ± perp·(width/2)</c> as the
    /// two wings and <c>tip</c> as the apex. Returns a degenerate (base = tip, perp = 0) when the
    /// segment is too short to normalize.
    /// </summary>
    public static (double BaseX, double BaseY, double PerpX, double PerpY) ArrowHead(
        double tipX, double tipY, double fromX, double fromY, double length)
    {
        double tx = tipX - fromX, ty = tipY - fromY;
        double len = Math.Sqrt(tx * tx + ty * ty);
        if (len <= 0.0001) return (tipX, tipY, 0, 0);
        tx /= len;
        ty /= len;
        return (tipX - tx * length, tipY - ty * length, -ty, tx);
    }

    /// <summary>
    /// Distance from point <c>p</c> to the segment <c>a→b</c> (for link hover hit-testing).
    /// </summary>
    public static double PointSegmentDistance(
        double px, double py, double ax, double ay, double bx, double by)
    {
        double abx = bx - ax, aby = by - ay;
        double len2 = abx * abx + aby * aby;
        if (len2 < 0.0001)
        {
            return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        }
        double t = ((px - ax) * abx + (py - ay) * aby) / len2;
        if (t < 0) t = 0;
        else if (t > 1) t = 1;
        double cx = ax + t * abx, cy = ay + t * aby;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }
}
