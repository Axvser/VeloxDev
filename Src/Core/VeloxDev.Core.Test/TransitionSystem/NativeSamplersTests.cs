using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class NativeSamplersTests
{
    private sealed class DoubleTarget { public double Value { get; set; } }
    private static ITransitionProperty DoubleProp => TransitionProperty.FromProperty(typeof(DoubleTarget).GetProperty(nameof(DoubleTarget.Value))!);

    private sealed class LongTarget { public long Value { get; set; } }
    private static ITransitionProperty LongProp => TransitionProperty.FromProperty(typeof(LongTarget).GetProperty(nameof(LongTarget.Value))!);

    private sealed class FloatTarget { public float Value { get; set; } }
    private static ITransitionProperty FloatProp => TransitionProperty.FromProperty(typeof(FloatTarget).GetProperty(nameof(FloatTarget.Value))!);

    [TestMethod]
    public void DoubleSampler_BasicLinear()
    {
        var sampler = new DoubleSampler();
        var target = new DoubleTarget();
        sampler.Update(target, DoubleProp, 0d, 10d, null, 0.5);
        Assert.AreEqual(5d, target.Value);
    }

    [TestMethod]
    public void DoubleSampler_Endpoints_AreExact()
    {
        var sampler = new DoubleSampler();
        var target = new DoubleTarget();
        sampler.Update(target, DoubleProp, 0d, 10d, null, 0.0);
        Assert.AreEqual(0d, target.Value);
        sampler.Update(target, DoubleProp, 0d, 10d, null, 1.0);
        Assert.AreEqual(10d, target.Value);
    }

    [TestMethod]
    public void DoubleSampler_NullStart_TreatsAsZero()
    {
        var sampler = new DoubleSampler();
        var target = new DoubleTarget();
        sampler.Update(target, DoubleProp, null, 10d, null, 0.5);
        Assert.AreEqual(5d, target.Value);
    }

    [TestMethod]
    public void DoubleSampler_Quarters_AreCorrect()
    {
        var sampler = new DoubleSampler();
        var target = new DoubleTarget();
        sampler.Update(target, DoubleProp, 0d, 100d, null, 0.25);
        Assert.AreEqual(25d, target.Value);
        sampler.Update(target, DoubleProp, 0d, 100d, null, 0.75);
        Assert.AreEqual(75d, target.Value);
    }

    [TestMethod]
    public void FloatSampler_BasicLinear()
    {
        var sampler = new FloatSampler();
        var target = new FloatTarget();
        sampler.Update(target, FloatProp, 0f, 10f, null, 0.5);
        Assert.AreEqual(5f, target.Value);
    }

    [TestMethod]
    public void LongSampler_BasicLinear()
    {
        var sampler = new LongSampler();
        var target = new LongTarget();
        sampler.Update(target, LongProp, 0L, 100L, null, 0.5);
        Assert.AreEqual(50L, target.Value);
    }

    [TestMethod]
    public void LongSampler_SameStartEnd_AllSame()
    {
        var sampler = new LongSampler();
        var target = new LongTarget();
        sampler.Update(target, LongProp, 42L, 42L, null, 0.25);
        Assert.AreEqual(42L, target.Value);
        sampler.Update(target, LongProp, 42L, 42L, null, 0.75);
        Assert.AreEqual(42L, target.Value);
    }

    [TestMethod]
    public void Normalize_ReturnsSelf_ForStatelessSampler()
    {
        var sampler = new DoubleSampler();
        Assert.AreSame(sampler, sampler.Normalize(0d, 10d, null));
    }
}
