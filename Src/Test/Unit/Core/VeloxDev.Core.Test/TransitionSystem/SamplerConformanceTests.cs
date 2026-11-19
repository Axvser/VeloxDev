using System.Numerics;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// Every sampler, at chosen times, checked against the closed form of its own rule rather than against "it moved".
/// </summary>
/// <remarks>
/// The assertions are exact equality. The times and endpoints are picked to be exactly representable in binary, so
/// no tolerance ever stands in for a mismatch: 0.5, 1.5 and -0.5 stand in for the real overshoot and anticipation
/// peaks (1.1000041 and -0.3730980), which would need a tolerance and would therefore prove less. What each sampler
/// does with an out-of-range time is the whole point of the suite, so every rule is exercised on both sides of the
/// unit interval.
/// </remarks>
[TestClass]
public class SamplerConformanceTests
{
    private const double Mid = 0.5;
    private const double Overshoot = 1.5;
    private const double Anticipate = -0.5;
    private static readonly double[] Times = [Mid, Overshoot, Anticipate];

    private sealed class Target
    {
        public double D { get; set; }
        public float F { get; set; }
        public int I { get; set; }
        public long L { get; set; }
        public System.Drawing.Point P { get; set; }
        public System.Drawing.PointF PF { get; set; }
        public System.Drawing.Size S { get; set; }
        public System.Drawing.SizeF SF { get; set; }
        public Vector2 V2 { get; set; }
        public Vector3 V3 { get; set; }
        public Vector4 V4 { get; set; }
        public System.Drawing.Rectangle R { get; set; }
        public System.Drawing.RectangleF RF { get; set; }
    }

    private static ITransitionProperty Property<T>(string name)
        => TransitionProperty.FromProperty(typeof(Target).GetProperty(name)!);

    /// <summary>Runs one frame and reads the value back through the very path a transition uses.</summary>
    private static TValue Sample<TValue>(ISampler sampler, ITransitionProperty property, Target target,
                                         object start, object end, double t)
    {
        object? working = null;
        sampler.InsertFrame(target, property, ref working, start, end, null, t);
        return (TValue)property.GetValue(target)!;
    }

    // ---- 外推：值等于 start + (end - start) * t，正负两侧都一样 ------------------------------------------

    [TestMethod]
    public void Double_MatchesItsClosedForm()
    {
        var property = Property<double>(nameof(Target.D));
        var target = new Target();

        foreach (var t in Times)
        {
            var value = Sample<double>(new DoubleSampler(), property, target, 10d, 110d, t);
            Assert.AreEqual(10d + 100d * t, value, $"t={t}");
        }
    }

    [TestMethod]
    public void Float_MatchesItsClosedForm()
    {
        // Float arithmetic has to be done in float, not double, or the expectation drifts by a bit or two.
        var property = Property<float>(nameof(Target.F));
        var target = new Target();

        foreach (var t in Times)
        {
            var value = Sample<float>(new FloatSampler(), property, target, 10f, 110f, t);
            Assert.AreEqual(10f + 100f * (float)t, value, $"t={t}");
        }
    }

    [TestMethod]
    public void Int_And_Long_MatchTheirClosedForm()
    {
        var intProperty = Property<int>(nameof(Target.I));
        var longProperty = Property<long>(nameof(Target.L));
        var target = new Target();

        foreach (var t in Times)
        {
            Assert.AreEqual((int)Math.Round(10 + t * 100), Sample<int>(new IntSampler(), intProperty, target, 10, 110, t), $"int t={t}");
            Assert.AreEqual((long)Math.Round(10 + t * 100), Sample<long>(new LongSampler(), longProperty, target, 10L, 110L, t), $"long t={t}");
        }
    }

    [TestMethod]
    public void Point_And_PointF_MatchTheirClosedForm_Componentwise()
    {
        var p = Property<System.Drawing.Point>(nameof(Target.P));
        var pf = Property<System.Drawing.PointF>(nameof(Target.PF));
        var target = new Target();

        foreach (var t in Times)
        {
            var point = Sample<System.Drawing.Point>(new PointSampler(), p, target,
                new System.Drawing.Point(10, 20), new System.Drawing.Point(110, 220), t);
            Assert.AreEqual(new System.Drawing.Point(
                10 + (int)Math.Round(100d * t),
                20 + (int)Math.Round(200d * t)), point, $"t={t}");

            var pointF = Sample<System.Drawing.PointF>(new PointFSampler(), pf, target,
                new System.Drawing.PointF(10, 20), new System.Drawing.PointF(110, 220), t);
            Assert.AreEqual(new System.Drawing.PointF(10f + 100f * (float)t, 20f + 200f * (float)t), pointF, $"t={t}");
        }
    }

    [TestMethod]
    public void Vectors_MatchTheirClosedForm_Componentwise()
    {
        var v2 = Property<Vector2>(nameof(Target.V2));
        var v3 = Property<Vector3>(nameof(Target.V3));
        var v4 = Property<Vector4>(nameof(Target.V4));
        var target = new Target();

        foreach (var t in Times)
        {
            var f = (float)t;
            Assert.AreEqual(new Vector2(10f + 100f * f, 20f + 200f * f),
                Sample<Vector2>(new Vector2Sampler(), v2, target, new Vector2(10, 20), new Vector2(110, 220), t), $"v2 t={t}");
            Assert.AreEqual(new Vector3(10f + 100f * f, 20f + 200f * f, 30f + 300f * f),
                Sample<Vector3>(new Vector3Sampler(), v3, target, new Vector3(10, 20, 30), new Vector3(110, 220, 330), t), $"v3 t={t}");
            Assert.AreEqual(new Vector4(10f + 100f * f, 20f + 200f * f, 30f + 300f * f, 40f + 400f * f),
                Sample<Vector4>(new Vector4Sampler(), v4, target, new Vector4(10, 20, 30, 40), new Vector4(110, 220, 330, 440), t), $"v4 t={t}");
        }
    }

    // ---- 尺寸：宽高共用一个进度、在 0 处停止；位置不受该进度约束 -------------------------------------------

    [TestMethod]
    public void Size_ExtrapolatesWhenItCan_AndStopsAtZeroWhenItCannot()
    {
        var property = Property<System.Drawing.Size>(nameof(Target.S));
        var target = new Target();

        // Growing: no channel reaches a limit, so both take the full time.
        Assert.AreEqual(new System.Drawing.Size(160, 320),
            Sample<System.Drawing.Size>(new SizeSampler(), property, target,
                new System.Drawing.Size(10, 20), new System.Drawing.Size(110, 220), Overshoot));

        // Shrinking: width would reach zero at progress 1.0, so the group stops there — height stops with it,
        // which is what keeps the aspect ratio on its own trajectory instead of letting one axis run on.
        Assert.AreEqual(new System.Drawing.Size(0, 150),
            Sample<System.Drawing.Size>(new SizeSampler(), property, target,
                new System.Drawing.Size(100, 50), new System.Drawing.Size(0, 150), Overshoot));

        // Anticipation is the same rule mirrored, and it only binds when the *interpolated* size would go below
        // zero: extrapolating a growing size backwards from 10 reaches zero at progress -1/9, so the group stops
        // there instead of taking the whole -0.5.
        Assert.AreEqual(new System.Drawing.Size(0, 50),
            Sample<System.Drawing.Size>(new SizeSampler(), property, target,
                new System.Drawing.Size(10, 50), new System.Drawing.Size(100, 50), Anticipate));
    }

    [TestMethod]
    public void Rectangle_PositionExtrapolates_WhileSizeStopsAtZero()
    {
        var property = Property<System.Drawing.Rectangle>(nameof(Target.R));
        var target = new Target();

        // x/y carry the full eased time even when the size group has already stopped: a position is unbounded.
        Assert.AreEqual(new System.Drawing.Rectangle(150, 300, 0, 150),
            Sample<System.Drawing.Rectangle>(new RectangleSampler(), property, target,
                new System.Drawing.Rectangle(0, 0, 100, 50), new System.Drawing.Rectangle(100, 200, 0, 150), Overshoot));
    }

    [TestMethod]
    public void RectangleF_PositionExtrapolates_WhileSizeStopsAtZero()
    {
        var property = Property<System.Drawing.RectangleF>(nameof(Target.RF));
        var target = new Target();

        var value = Sample<System.Drawing.RectangleF>(new RectangleFSampler(), property, target,
            new System.Drawing.RectangleF(0, 0, 100, 50), new System.Drawing.RectangleF(100, 200, 0, 150), Overshoot);

        Assert.AreEqual(150f, value.X);
        Assert.AreEqual(300f, value.Y);
        Assert.AreEqual(0f, value.Width);
        Assert.AreEqual(150f, value.Height);
    }
}
