using System;
using System.Collections.Generic;
using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A link's drawn geometry, flattened to a polyline — the single canonical form every adapter
/// hit-tests against.
///
/// Every GUI previously kept its own copy of this: a sample table built from the link's endpoints,
/// a cubic evaluator, an arc-length running total, and a point-to-segment distance walked per query.
/// Nine copies of the distance function, eight of the evaluator and seven of the table existed across
/// the seven adapters and their demos. This type owns the one copy.
///
/// The curve is built from the points the *view* supplies, and Core takes no view on the shape: a
/// cubic with horizontal control points is only the house default (see <see cref="BuildCubic"/>), and
/// a view that draws something else hands over its own polyline through <see cref="FromPoints"/>.
/// Coordinates are the link's own render space, so the caller chooses whether to publish in world or
/// collapsed coordinates — it must only hit-test in the same space it published in.
///
/// Immutable once built, so it can be published once per geometry change and shared by every query.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "连线的扁平化几何：由视图提交点列，Core 负责包围盒、弧长与命中判定")]
[AgentContext(AgentLanguages.English, "A link's flattened geometry: the view supplies the points, Core owns bounds, arc length and containment")]
public sealed class LinkCurve
{
    /// <summary>Default sample count for the house cubic — the density the seven adapters settled on.</summary>
    public const int DefaultSampleCount = 128;

    private readonly double[] _xs;
    private readonly double[] _ys;
    private readonly double[] _cumulative;

    private LinkCurve(double[] xs, double[] ys, double[] cumulative, double length, WorkflowBounds bounds)
    {
        _xs = xs;
        _ys = ys;
        _cumulative = cumulative;
        Length = length;
        Bounds = bounds;
    }

    /// <summary>Number of points on the polyline.</summary>
    public int Count => _xs.Length;

    /// <summary>Axis-aligned bounds of the polyline's points.</summary>
    public WorkflowBounds Bounds { get; }

    /// <summary>Total arc length of the polyline.</summary>
    public double Length { get; }

    /// <summary>X of the point at <paramref name="index"/>. The index is not bounds-checked.</summary>
    public double XAt(int index) => _xs[index];

    /// <summary>Y of the point at <paramref name="index"/>. The index is not bounds-checked.</summary>
    public double YAt(int index) => _ys[index];

    /// <summary>
    /// Arc length from the start of the polyline up to the point at <paramref name="index"/>. Callers that
    /// draw a run of segments between two arc lengths walk this instead of keeping a running total of their own.
    /// </summary>
    public double LengthAt(int index) => _cumulative[index];

    /// <summary>
    /// Builds a curve from an explicit polyline. Use this when the view draws a shape of its own —
    /// Core does not require, and does not assume, a cubic.
    /// </summary>
    /// <param name="xs">X coordinates, in the view's render space.</param>
    /// <param name="ys">Y coordinates, same space, same length as <paramref name="xs"/>.</param>
    /// <exception cref="ArgumentNullException">Either sequence is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The two sequences differ in length.</exception>
    public static LinkCurve FromPoints(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        if (xs is null) throw new ArgumentNullException(nameof(xs));
        if (ys is null) throw new ArgumentNullException(nameof(ys));
        if (xs.Count != ys.Count)
        {
            throw new ArgumentException("xs and ys must have the same length.", nameof(ys));
        }

        var count = xs.Count;
        var px = new double[count];
        var py = new double[count];
        for (var i = 0; i < count; i++)
        {
            px[i] = xs[i];
            py[i] = ys[i];
        }

        return Build(px, py);
    }

    /// <summary>
    /// Builds the house curve: a cubic Bézier whose two control points are pulled horizontally away
    /// from their own endpoints, sampled into a polyline.
    /// </summary>
    /// <param name="startX">Start point x.</param>
    /// <param name="startY">Start point y.</param>
    /// <param name="endX">End point x.</param>
    /// <param name="endY">End point y.</param>
    /// <param name="pullMinimum">
    /// Least horizontal pull, in the view's units. The pull actually used is the larger of this and half the
    /// horizontal gap, so two ports close together do not degenerate the curve into a straight segment.
    /// </param>
    /// <param name="sampleCount">Number of points; <see cref="DefaultSampleCount"/> when not positive.</param>
    public static LinkCurve BuildCubic(
        double startX, double startY, double endX, double endY, double pullMinimum, int sampleCount = DefaultSampleCount)
    {
        var pull = Math.Max(pullMinimum, Math.Abs(endX - startX) * 0.5);
        return Sample(startX, startY, startX + pull, startY, endX - pull, endY, endX, endY, sampleCount);
    }

    /// <summary>
    /// The four control points of the curve between two ports, each one pulled along the <b>outward</b>
    /// direction of its own port — the edge of its node that the port sits on.
    ///
    /// This is the same curve <see cref="BuildCubic"/> draws whenever both ports are on the usual sides
    /// (sender on its node's right edge, receiver on the other's left edge): both outward directions are
    /// horizontal, so the pull reduces to exactly the old one. It differs where the old rule was wrong —
    /// a port on a top/bottom edge, and a link whose ends are the other way round, which used to pull one
    /// control point <i>into</i> its node.
    ///
    /// The two ends are treated symmetrically: swapping them returns the same four points in reverse order,
    /// so the curve does not depend on which end is called the sender. An end with no node contributes no
    /// pull at all, which is what a drag preview needs: its far end is the pointer, not a port.
    /// </summary>
    /// <param name="start">The port the link leaves.</param>
    /// <param name="end">The port it arrives at.</param>
    /// <param name="pullMinimum">Least pull, in the view's units; see <paramref name="pullMinimum"/> on <see cref="BuildCubic"/>.</param>
    /// <returns>Start, first control point, second control point, end.</returns>
    /// <exception cref="ArgumentNullException">Either port is <see langword="null"/>.</exception>
    public static (double X, double Y)[] PortCurvePoints(
        IWorkflowSlotViewModel start, IWorkflowSlotViewModel end, double pullMinimum)
    {
        if (start is null) throw new ArgumentNullException(nameof(start));
        if (end is null) throw new ArgumentNullException(nameof(end));

        var (sx, sy) = (start.Anchor.Horizontal, start.Anchor.Vertical);
        var (ex, ey) = (end.Anchor.Horizontal, end.Anchor.Vertical);
        var (nx1, ny1) = PortOutward(start);
        var (nx2, ny2) = PortOutward(end);

        // 每个控制点沿**自己那个口**的法线拉：距离按该法线轴上的间距算，所以两端互相独立、交换不变。
        var pull1 = Pull(sx, sy, ex, ey, nx1, ny1, pullMinimum);
        var pull2 = Pull(sx, sy, ex, ey, nx2, ny2, pullMinimum);

        return
        [
            (sx, sy),
            (sx + (nx1 * pull1), sy + (ny1 * pull1)),
            (ex + (nx2 * pull2), ey + (ny2 * pull2)),
            (ex, ey),
        ];
    }

    /// <summary>
    /// Builds the curve between two ports with <see cref="PortCurvePoints"/> and samples it, so the drawn
    /// curve and the hit-tested one come from one computation.
    /// </summary>
    /// <param name="start">The port the link leaves.</param>
    /// <param name="end">The port it arrives at.</param>
    /// <param name="pullMinimum">Least pull, in the view's units.</param>
    /// <param name="sampleCount">Number of points; <see cref="DefaultSampleCount"/> when not positive.</param>
    /// <exception cref="ArgumentNullException">Either port is <see langword="null"/>.</exception>
    public static LinkCurve BuildPortCubic(
        IWorkflowSlotViewModel start, IWorkflowSlotViewModel end, double pullMinimum, int sampleCount = DefaultSampleCount)
    {
        var points = PortCurvePoints(start, end, pullMinimum);
        return Sample(
            points[0].X, points[0].Y, points[1].X, points[1].Y, points[2].X, points[2].Y, points[3].X, points[3].Y, sampleCount);
    }

    /// <summary>
    /// The direction a port's line should leave in: the outward normal of the node edge that port sits on,
    /// taken as the edge nearest the port (normalised, so a wide node does not favour its horizontal edges).
    /// <para>
    /// A port with <b>no node</b> answers <c>(0, 0)</c> — no direction. That is the drag preview's free end:
    /// nothing is known about what the pointer will land on, so assuming an edge for it would bend the line
    /// around a target that does not exist yet. With no direction the end's control point collapses onto the
    /// end itself and the curve arrives there <b>straight</b>; once the pointer is over a real port, that port
    /// has a node and this answers its edge, so the preview bends the way the finished link will.
    /// </para>
    /// </summary>
    /// <param name="slot">The port.</param>
    /// <exception cref="ArgumentNullException"><paramref name="slot"/> is <see langword="null"/>.</exception>
    public static (double X, double Y) PortOutward(IWorkflowSlotViewModel slot)
    {
        if (slot is null) throw new ArgumentNullException(nameof(slot));
        if (slot.Parent is not { } node) return (0, 0);

        var halfWidth = Math.Max(1e-6, node.Size.Width * 0.5);
        var halfHeight = Math.Max(1e-6, node.Size.Height * 0.5);
        var dx = (slot.Anchor.Horizontal - (node.Anchor.Horizontal + halfWidth)) / halfWidth;
        var dy = (slot.Anchor.Vertical - (node.Anchor.Vertical + halfHeight)) / halfHeight;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            return (dx >= 0 ? 1 : -1, 0);
        }

        return (0, dy >= 0 ? 1 : -1);
    }

    // 沿这个口的法线轴量两个端点的距离：水平口看 |dx|，垂直口看 |dy|。标准布局下两口都是水平口 ⇒
    // 取的就是 |dx|，与旧公式逐字相同。
    private static double Pull(double startX, double startY, double endX, double endY, double nx, double ny, double minimum)
    {
        // 没有方向（自由端）就不拉：控制点落在端点上，曲线到那里是平的。
        if (nx == 0 && ny == 0)
        {
            return 0;
        }

        var gap = Math.Abs(nx != 0 ? endX - startX : endY - startY);
        return Math.Max(minimum, gap * 0.5);
    }

    // 三次贝塞尔的采样。控制点的纵坐标不再假定等于自己那一端 —— 垂直口时它就是另一回事。
    private static LinkCurve Sample(
        double startX, double startY, double c1X, double c1Y, double c2X, double c2Y, double endX, double endY, int sampleCount)
    {
        if (sampleCount < 2) sampleCount = DefaultSampleCount;

        var px = new double[sampleCount];
        var py = new double[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var t = (double)i / (sampleCount - 1);
            var u = 1 - t;
            var a = u * u * u;
            var b = 3 * u * u * t;
            var c = 3 * u * t * t;
            var d = t * t * t;

            px[i] = (a * startX) + (b * c1X) + (c * c2X) + (d * endX);
            py[i] = (a * startY) + (b * c1Y) + (c * c2Y) + (d * endY);
        }

        return Build(px, py);
    }

    /// <summary>
    /// Whether <paramref name="x"/>/<paramref name="y"/> lies within <paramref name="radius"/> of the
    /// polyline — the containment test the frameworks perform on a stroke, done here on the flattened path.
    /// </summary>
    /// <remarks>
    /// Rejects against the bounds inflated by <paramref name="radius"/> first, so a link the pointer is
    /// nowhere near costs one comparison rather than a walk of every segment. That ordering is what makes
    /// this affordable to run per pointer move over every visible link.
    /// </remarks>
    public bool Contains(double x, double y, double radius)
    {
        var bounds = Bounds;
        if (x < bounds.Left - radius || x > bounds.Right + radius
            || y < bounds.Top - radius || y > bounds.Bottom + radius)
        {
            return false;
        }

        for (var i = 1; i < _xs.Length; i++)
        {
            if (DistanceToSegment(x, y, _xs[i - 1], _ys[i - 1], _xs[i], _ys[i]) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The point at <paramref name="length"/> along the polyline by arc length, clamped to
    /// <c>[0, Length]</c>. This is what lets a travelling highlight move at a constant speed around the bend.
    /// </summary>
    public (double X, double Y) PointAtLength(double length)
    {
        if (_xs.Length == 0) return (0, 0);
        if (_xs.Length == 1 || Length <= 0) return (_xs[0], _ys[0]);

        if (length <= 0) return (_xs[0], _ys[0]);
        if (length >= Length) return (_xs[_xs.Length - 1], _ys[_ys.Length - 1]);

        // 二分找段，再在段内线性插值 —— 取点精度不受采样密度限制。
        var lo = 0;
        var hi = _cumulative.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (_cumulative[mid] <= length) lo = mid;
            else hi = mid;
        }

        var span = _cumulative[hi] - _cumulative[lo];
        var t = span <= 0 ? 0 : (length - _cumulative[lo]) / span;
        return (_xs[lo] + ((_xs[hi] - _xs[lo]) * t), _ys[lo] + ((_ys[hi] - _ys[lo]) * t));
    }

    private static LinkCurve Build(double[] xs, double[] ys)
    {
        var cumulative = new double[xs.Length];
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        for (var i = 0; i < xs.Length; i++)
        {
            if (xs[i] < minX) minX = xs[i];
            if (xs[i] > maxX) maxX = xs[i];
            if (ys[i] < minY) minY = ys[i];
            if (ys[i] > maxY) maxY = ys[i];

            if (i > 0)
            {
                var dx = xs[i] - xs[i - 1];
                var dy = ys[i] - ys[i - 1];
                cumulative[i] = cumulative[i - 1] + Math.Sqrt((dx * dx) + (dy * dy));
            }
        }

        var bounds = xs.Length == 0
            ? default
            : new WorkflowBounds { Left = minX, Top = minY, Width = maxX - minX, Height = maxY - minY };

        return new LinkCurve(xs, ys, cumulative, cumulative.Length == 0 ? 0 : cumulative[cumulative.Length - 1], bounds);
    }

    // 点到线段的距离。九份副本收敛成这一份 —— 退化段（两端同点）直接退回点到点。
    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var lengthSquared = (abx * abx) + (aby * aby);
        if (lengthSquared < 0.0001)
        {
            var dx = px - ax;
            var dy = py - ay;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        var t = ((px - ax) * abx + (py - ay) * aby) / lengthSquared;
        if (t < 0) t = 0;
        else if (t > 1) t = 1;

        var projX = ax + (t * abx);
        var projY = ay + (t * aby);
        var ox = px - projX;
        var oy = py - projY;
        return Math.Sqrt((ox * ox) + (oy * oy));
    }
}
