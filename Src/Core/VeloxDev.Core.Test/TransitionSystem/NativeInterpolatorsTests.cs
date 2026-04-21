using VeloxDev.TransitionSystem.NativeInterpolators;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class NativeInterpolatorsTests
{
    [TestMethod]
    public void DoubleInterpolator_BasicLinear()
    {
        var interp = new DoubleInterpolator();
        var result = interp.Interpolate(0d, 10d, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(0d, (double)result[0]!);
        Assert.AreEqual(10d, (double)result[4]!);
    }

    [TestMethod]
    public void DoubleInterpolator_SingleStep_ReturnsEnd()
    {
        var interp = new DoubleInterpolator();
        var result = interp.Interpolate(0d, 100d, 1);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(100d, (double)result[0]!);
    }

    [TestMethod]
    public void DoubleInterpolator_ZeroSteps_ReturnsEmpty()
    {
        var interp = new DoubleInterpolator();
        var result = interp.Interpolate(0d, 100d, 0);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void DoubleInterpolator_NullStart_TreatsAsZero()
    {
        var interp = new DoubleInterpolator();
        var result = interp.Interpolate(null, 10d, 3);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(0d, (double)result[0]!);
        Assert.AreEqual(10d, (double)result[2]!);
    }

    [TestMethod]
    public void FloatInterpolator_BasicLinear()
    {
        var interp = new FloatInterpolator();
        var result = interp.Interpolate(0f, 10f, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(0f, (float)result[0]!);
        Assert.AreEqual(10f, (float)result[4]!);
    }

    [TestMethod]
    public void FloatInterpolator_SingleStep_ReturnsEnd()
    {
        var interp = new FloatInterpolator();
        var result = interp.Interpolate(0f, 50f, 1);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(50f, (float)result[0]!);
    }

    [TestMethod]
    public void LongInterpolator_BasicLinear()
    {
        var interp = new LongInterpolator();
        var result = interp.Interpolate(0L, 100L, 5);
        Assert.AreEqual(5, result.Count);
        Assert.AreEqual(0L, (long)result[0]!);
        Assert.AreEqual(100L, (long)result[4]!);
    }

    [TestMethod]
    public void LongInterpolator_SameStartEnd_AllSame()
    {
        var interp = new LongInterpolator();
        var result = interp.Interpolate(42L, 42L, 4);
        Assert.AreEqual(4, result.Count);
        foreach (var v in result)
            Assert.AreEqual(42L, (long)v!);
    }

    [TestMethod]
    public void DoubleInterpolator_Midpoint_IsCorrect()
    {
        var interp = new DoubleInterpolator();
        var result = interp.Interpolate(0d, 100d, 3);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(0d, (double)result[0]!);
        Assert.AreEqual(50d, (double)result[1]!);
        Assert.AreEqual(100d, (double)result[2]!);
    }
}
