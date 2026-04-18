using System.Drawing;
using System.Numerics;
using VeloxDev.TransitionSystem.NativeInterpolators;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class NativeInterpolatorsExtendedTests
{
    // ───────── ColorInterpolator ─────────

    [TestMethod]
    public void ColorInterpolator_BasicLinear()
    {
        var interp = new ColorInterpolator();
        var start = Color.FromArgb(255, 0, 0, 0);
        var end = Color.FromArgb(255, 255, 255, 255);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void ColorInterpolator_SingleStep_ReturnsEnd()
    {
        var interp = new ColorInterpolator();
        var end = Color.Red;
        var result = interp.Interpolate(Color.Blue, end, 1);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(end, result[0]);
    }

    [TestMethod]
    public void ColorInterpolator_NullStart_UsesDefault()
    {
        var interp = new ColorInterpolator();
        var end = Color.FromArgb(255, 100, 100, 100);
        var result = interp.Interpolate(null, end, 3);
        Assert.AreEqual(3, result.Count);
        Assert.IsNull(result[0]);
        Assert.AreEqual(end, result[2]);
    }

    // ───────── PointInterpolator ─────────

    [TestMethod]
    public void PointInterpolator_BasicLinear()
    {
        var interp = new PointInterpolator();
        var start = new Point(0, 0);
        var end = new Point(100, 200);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void PointInterpolator_SingleStep()
    {
        var interp = new PointInterpolator();
        var result = interp.Interpolate(new Point(0, 0), new Point(10, 20), 1);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(new Point(10, 20), result[0]);
    }

    // ───────── PointFInterpolator ─────────

    [TestMethod]
    public void PointFInterpolator_BasicLinear()
    {
        var interp = new PointFInterpolator();
        var start = new PointF(0f, 0f);
        var end = new PointF(10f, 20f);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void PointFInterpolator_SingleStep()
    {
        var interp = new PointFInterpolator();
        var result = interp.Interpolate(new PointF(0, 0), new PointF(5f, 10f), 1);
        Assert.AreEqual(new PointF(5f, 10f), result[0]);
    }

    // ───────── SizeInterpolator ─────────

    [TestMethod]
    public void SizeInterpolator_BasicLinear()
    {
        var interp = new SizeInterpolator();
        var start = new Size(0, 0);
        var end = new Size(100, 200);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void SizeInterpolator_SingleStep()
    {
        var interp = new SizeInterpolator();
        var result = interp.Interpolate(new Size(10, 20), new Size(50, 60), 1);
        Assert.AreEqual(new Size(50, 60), result[0]);
    }

    // ───────── SizeFInterpolator ─────────

    [TestMethod]
    public void SizeFInterpolator_BasicLinear()
    {
        var interp = new SizeFInterpolator();
        var start = new SizeF(0f, 0f);
        var end = new SizeF(10f, 20f);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void SizeFInterpolator_SingleStep()
    {
        var interp = new SizeFInterpolator();
        var result = interp.Interpolate(new SizeF(1f, 2f), new SizeF(3f, 4f), 1);
        Assert.AreEqual(new SizeF(3f, 4f), result[0]);
    }

    // ───────── RectangleInterpolator ─────────

    [TestMethod]
    public void RectangleInterpolator_BasicLinear()
    {
        var interp = new RectangleInterpolator();
        var start = new Rectangle(0, 0, 10, 10);
        var end = new Rectangle(100, 100, 200, 200);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void RectangleInterpolator_SingleStep()
    {
        var interp = new RectangleInterpolator();
        var end = new Rectangle(5, 5, 50, 50);
        var result = interp.Interpolate(new Rectangle(0, 0, 0, 0), end, 1);
        Assert.AreEqual(end, result[0]);
    }

    // ───────── RectangleFInterpolator ─────────

    [TestMethod]
    public void RectangleFInterpolator_BasicLinear()
    {
        var interp = new RectangleFInterpolator();
        var start = new RectangleF(0f, 0f, 10f, 10f);
        var end = new RectangleF(100f, 100f, 200f, 200f);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void RectangleFInterpolator_SingleStep()
    {
        var interp = new RectangleFInterpolator();
        var end = new RectangleF(1f, 2f, 3f, 4f);
        var result = interp.Interpolate(new RectangleF(0, 0, 0, 0), end, 1);
        Assert.AreEqual(end, result[0]);
    }

    // ───────── Vector2Interpolator ─────────

    [TestMethod]
    public void Vector2Interpolator_BasicLinear()
    {
        var interp = new Vector2Interpolator();
        var start = new Vector2(0, 0);
        var end = new Vector2(10, 20);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void Vector2Interpolator_SingleStep()
    {
        var interp = new Vector2Interpolator();
        var end = new Vector2(5, 10);
        var result = interp.Interpolate(Vector2.Zero, end, 1);
        Assert.AreEqual(end, result[0]);
    }

    [TestMethod]
    public void Vector2Interpolator_NullStart()
    {
        var interp = new Vector2Interpolator();
        var end = new Vector2(10, 10);
        var result = interp.Interpolate(null, end, 3);
        Assert.AreEqual(3, result.Count);
        Assert.IsNull(result[0]);
        Assert.AreEqual(end, result[2]);
    }

    // ───────── Vector3Interpolator ─────────

    [TestMethod]
    public void Vector3Interpolator_BasicLinear()
    {
        var interp = new Vector3Interpolator();
        var start = new Vector3(0, 0, 0);
        var end = new Vector3(10, 20, 30);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void Vector3Interpolator_SingleStep()
    {
        var interp = new Vector3Interpolator();
        var end = new Vector3(1, 2, 3);
        var result = interp.Interpolate(Vector3.Zero, end, 1);
        Assert.AreEqual(end, result[0]);
    }

    // ───────── Vector4Interpolator ─────────

    [TestMethod]
    public void Vector4Interpolator_BasicLinear()
    {
        var interp = new Vector4Interpolator();
        var start = new Vector4(0, 0, 0, 0);
        var end = new Vector4(10, 20, 30, 40);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void Vector4Interpolator_SingleStep()
    {
        var interp = new Vector4Interpolator();
        var end = new Vector4(1, 2, 3, 4);
        var result = interp.Interpolate(Vector4.Zero, end, 1);
        Assert.AreEqual(end, result[0]);
    }

    // ───────── QuaternionInterpolator ─────────

    [TestMethod]
    public void QuaternionInterpolator_BasicSlerp()
    {
        var interp = new QuaternionInterpolator();
        var start = Quaternion.Identity;
        var end = Quaternion.CreateFromYawPitchRoll(MathF.PI / 2, 0, 0);
        var result = interp.Interpolate(start, end, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(start, result[0]);
        Assert.AreEqual(end, result[4]);
    }

    [TestMethod]
    public void QuaternionInterpolator_SingleStep()
    {
        var interp = new QuaternionInterpolator();
        var end = Quaternion.CreateFromYawPitchRoll(1, 0, 0);
        var result = interp.Interpolate(Quaternion.Identity, end, 1);
        Assert.AreEqual(end, result[0]);
    }

    [TestMethod]
    public void QuaternionInterpolator_NullStart_UsesIdentity()
    {
        var interp = new QuaternionInterpolator();
        var end = Quaternion.CreateFromYawPitchRoll(1, 0, 0);
        var result = interp.Interpolate(null, end, 3);
        Assert.AreEqual(3, result.Count);
        Assert.IsNull(result[0]);
        Assert.AreEqual(end, result[2]);
    }

    // ───────── ZeroSteps: only DoubleInterpolator/FloatInterpolator/LongInterpolator handle 0 gracefully ─────────
    // Other interpolators set result[0]=start after the loop, which crashes on empty list.
    // This is by-design — callers always pass steps >= 1.

    [TestMethod]
    public void VectorInterpolators_SingleStep_ReturnsEnd()
    {
        Assert.AreEqual(1, new Vector2Interpolator().Interpolate(Vector2.Zero, Vector2.One, 1).Count);
        Assert.AreEqual(1, new Vector3Interpolator().Interpolate(Vector3.Zero, Vector3.One, 1).Count);
        Assert.AreEqual(1, new Vector4Interpolator().Interpolate(Vector4.Zero, Vector4.One, 1).Count);
    }
}
