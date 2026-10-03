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
        if (sampleCount < 2) sampleCount = DefaultSampleCount;

        var pull = Math.Max(pullMinimum, Math.Abs(endX - startX) * 0.5);
        var c1X = startX + pull;
        var c2X = endX - pull;

        var px = new double[sampleCount];
        var py = new double[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            // 两个控制点的纵坐标各自跟着自己那一端，所以两端出线方向是水平的。
            var t = (double)i / (sampleCount - 1);
            var u = 1 - t;
            var a = u * u * u;
            var b = 3 * u * u * t;
            var c = 3 * u * t * t;
            var d = t * t * t;

            px[i] = (a * startX) + (b * c1X) + (c * c2X) + (d * endX);
            py[i] = ((a + b) * startY) + ((c + d) * endY);
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
