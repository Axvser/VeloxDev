using System.Drawing;
using System.Numerics;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class NativeSamplersExtendedTests
{
    private object? working = null;

    // A boxed-object property accepts any sampled value, so a single target serves every sampler below.
    private sealed class BoxTarget { public object? Value { get; set; } }
    private static ITransitionProperty Prop => TransitionProperty.FromProperty(typeof(BoxTarget).GetProperty(nameof(BoxTarget.Value))!);

    // ───────── ColorSampler ─────────

    [TestMethod]
    public void ColorSampler_BasicLinear()
    {
        var sampler = new ColorSampler();
        var target = new BoxTarget();
        var start = Color.FromArgb(255, 0, 0, 0);
        var end = Color.FromArgb(255, 255, 255, 255);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        // (byte) cast truncates, not rounds: 0 + 255*0.5 == 127.5 → 127
        Assert.AreEqual(Color.FromArgb(255, 127, 127, 127), target.Value);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 1.0);
        Assert.AreEqual(end, target.Value);
    }

    [TestMethod]
    public void ColorSampler_NullStart_UsesDefault()
    {
        var sampler = new ColorSampler();
        var target = new BoxTarget();
        var end = Color.FromArgb(255, 100, 100, 100);
        sampler.InsertFrame(target, Prop, ref working, null, end, null, 0.5);
        Assert.AreEqual(Color.FromArgb(127, 50, 50, 50), target.Value);
        sampler.InsertFrame(target, Prop, ref working, null, end, null, 1.0);
        Assert.AreEqual(end, target.Value);
    }

    // ───────── String color sampling ─────────

    [TestMethod]
    public void StringColorSampling_InterpolatesHexColor()
    {
        var sampler = new TestStringSampler();
        var target = new BoxTarget();
        sampler.InsertFrame(target, Prop, ref working, "#000000", "#ffffff", null, 0.5);
        Assert.AreEqual("rgba(128, 128, 128, 1)", target.Value);
    }

    [TestMethod]
    public void StringColorSampling_ForNonColorString_DiscreteUntilEnd()
    {
        var sampler = new TestStringSampler();
        var target = new BoxTarget();
        sampler.InsertFrame(target, Prop, ref working, "translateX(0px)", "translateX(100px)", null, 0.5);
        Assert.AreEqual("translateX(0px)", target.Value);
        sampler.InsertFrame(target, Prop, ref working, "translateX(0px)", "translateX(100px)", null, 1.0);
        Assert.AreEqual("translateX(100px)", target.Value);
    }

    // ───────── PointSampler ─────────

    [TestMethod]
    public void PointSampler_BasicLinear()
    {
        var sampler = new PointSampler();
        var target = new BoxTarget();
        var start = new Point(0, 0);
        var end = new Point(100, 200);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        Assert.AreEqual(new Point(50, 100), target.Value);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 1.0);
        Assert.AreEqual(end, target.Value);
    }

    // ───────── PointFSampler ─────────

    [TestMethod]
    public void PointFSampler_BasicLinear()
    {
        var sampler = new PointFSampler();
        var target = new BoxTarget();
        var start = new PointF(0f, 0f);
        var end = new PointF(10f, 20f);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        Assert.AreEqual(new PointF(5f, 10f), target.Value);
    }

    // ───────── SizeSampler ─────────

    [TestMethod]
    public void SizeSampler_BasicLinear()
    {
        var sampler = new SizeSampler();
        var target = new BoxTarget();
        var start = new Size(0, 0);
        var end = new Size(100, 200);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        Assert.AreEqual(new Size(50, 100), target.Value);
    }

    // ───────── SizeFSampler ─────────

    [TestMethod]
    public void SizeFSampler_BasicLinear()
    {
        var sampler = new SizeFSampler();
        var target = new BoxTarget();
        var start = new SizeF(0f, 0f);
        var end = new SizeF(10f, 20f);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        Assert.AreEqual(new SizeF(5f, 10f), target.Value);
    }

    // ───────── RectangleSampler ─────────

    [TestMethod]
    public void RectangleSampler_BasicLinear()
    {
        var sampler = new RectangleSampler();
        var target = new BoxTarget();
        var start = new Rectangle(0, 0, 10, 10);
        var end = new Rectangle(100, 100, 200, 200);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        Assert.AreEqual(new Rectangle(50, 50, 105, 105), target.Value);
    }

    // ───────── RectangleFSampler ─────────

    [TestMethod]
    public void RectangleFSampler_BasicLinear()
    {
        var sampler = new RectangleFSampler();
        var target = new BoxTarget();
        var start = new RectangleF(0f, 0f, 10f, 10f);
        var end = new RectangleF(100f, 100f, 200f, 200f);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        Assert.AreEqual(new RectangleF(50f, 50f, 105f, 105f), target.Value);
    }

    // ───────── Vector2Sampler ─────────

    [TestMethod]
    public void Vector2Sampler_BasicLinear()
    {
        var sampler = new Vector2Sampler();
        var target = new BoxTarget();
        var start = new Vector2(0, 0);
        var end = new Vector2(10, 20);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        Assert.AreEqual(new Vector2(5, 10), target.Value);
        sampler.InsertFrame(target, Prop, ref working, null, end, null, 1.0);
        Assert.AreEqual(end, target.Value);
    }

    // ───────── Vector3Sampler ─────────

    [TestMethod]
    public void Vector3Sampler_BasicLinear()
    {
        var sampler = new Vector3Sampler();
        var target = new BoxTarget();
        var start = new Vector3(0, 0, 0);
        var end = new Vector3(10, 20, 30);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        Assert.AreEqual(new Vector3(5, 10, 15), target.Value);
    }

    // ───────── Vector4Sampler ─────────

    [TestMethod]
    public void Vector4Sampler_BasicLinear()
    {
        var sampler = new Vector4Sampler();
        var target = new BoxTarget();
        var start = new Vector4(0, 0, 0, 0);
        var end = new Vector4(10, 20, 30, 40);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.5);
        Assert.AreEqual(new Vector4(5, 10, 15, 20), target.Value);
    }

    // ───────── QuaternionSampler ─────────

    [TestMethod]
    public void QuaternionSampler_BasicSlerp()
    {
        var sampler = new QuaternionSampler();
        var target = new BoxTarget();
        var start = Quaternion.Identity;
        var end = Quaternion.CreateFromYawPitchRoll(MathF.PI / 2, 0, 0);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 0.0);
        Assert.AreEqual(start, target.Value);
        sampler.InsertFrame(target, Prop, ref working, start, end, null, 1.0);
        Assert.AreEqual(end, target.Value);
    }

    [TestMethod]
    public void QuaternionSampler_NullStart_UsesIdentity()
    {
        var sampler = new QuaternionSampler();
        var target = new BoxTarget();
        var end = Quaternion.CreateFromYawPitchRoll(1, 0, 0);
        sampler.InsertFrame(target, Prop, ref working, null, end, null, 1.0);
        Assert.AreEqual(end, target.Value);
    }

    private static void AssertQuaternionClose(Quaternion expected, object? actual, float epsilon = 1e-4f)
    {
        var q = (Quaternion)actual!;
        Assert.AreEqual(expected.X, q.X, epsilon);
        Assert.AreEqual(expected.Y, q.Y, epsilon);
        Assert.AreEqual(expected.Z, q.Z, epsilon);
        Assert.AreEqual(expected.W, q.W, epsilon);
    }

    private sealed class TestStringSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var startValue = start as string;
            var endValue = end as string;

            if (TryParseHexColor(startValue, out var startColor) && TryParseHexColor(endValue, out var endColor))
            {
                property.SetValue(target, $"rgba({InterpolateChannel(startColor.R, endColor.R, t)}, {InterpolateChannel(startColor.G, endColor.G, t)}, {InterpolateChannel(startColor.B, endColor.B, t)}, 1)");
                return;
            }

            property.SetValue(target, startValue);
        }

        private static bool TryParseHexColor(string? value, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#')
            {
                return false;
            }

            if (!byte.TryParse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var red)
                || !byte.TryParse(value.Substring(3, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var green)
                || !byte.TryParse(value.Substring(5, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var blue))
            {
                return false;
            }

            color = Color.FromArgb(255, red, green, blue);
            return true;
        }

        private static byte InterpolateChannel(byte start, byte end, double progress)
        {
            return (byte)Math.Round(start + ((end - start) * progress));
        }
    }
}
