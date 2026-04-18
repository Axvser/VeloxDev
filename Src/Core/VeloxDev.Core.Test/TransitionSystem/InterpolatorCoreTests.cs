using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeInterpolators;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class InterpolatorCoreTests
{
    [TestMethod]
    public void RegisterInterpolator_And_TryGet_Succeeds()
    {
        var interp = new DoubleInterpolator();
        InterpolatorCore.RegisterInterpolator(typeof(double), interp);

        Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(double), out var result));
        Assert.AreSame(interp, result);
    }

    [TestMethod]
    public void TryGetInterpolator_UnregisteredType_ReturnsFalse()
    {
        Assert.IsFalse(InterpolatorCore.TryGetInterpolator(typeof(Guid), out var result));
        Assert.IsNull(result);
    }

    [TestMethod]
    public void UnregisterInterpolator_RemovesEntry()
    {
        var interp = new FloatInterpolator();
        InterpolatorCore.RegisterInterpolator(typeof(float), interp);
        var removed = InterpolatorCore.UnregisterInterpolator(typeof(float), out var old);

        Assert.IsTrue(removed);
        Assert.AreSame(interp, old);
        Assert.IsFalse(InterpolatorCore.TryGetInterpolator(typeof(float), out _));

        // Re-register for other tests
        InterpolatorCore.RegisterInterpolator(typeof(float), interp);
    }

    [TestMethod]
    public void RegisterInterpolator_OverwritesExisting()
    {
        var old = new DoubleInterpolator();
        var replacement = new DoubleInterpolator();
        InterpolatorCore.RegisterInterpolator(typeof(double), old);
        InterpolatorCore.RegisterInterpolator(typeof(double), replacement);

        Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(double), out var result));
        Assert.AreSame(replacement, result);
    }

    [TestMethod]
    public void NativeInterpolators_ContainsDefaults()
    {
        Assert.IsTrue(InterpolatorCore.NativeInterpolators.ContainsKey(typeof(double)));
        Assert.IsTrue(InterpolatorCore.NativeInterpolators.ContainsKey(typeof(long)));
    }
}
