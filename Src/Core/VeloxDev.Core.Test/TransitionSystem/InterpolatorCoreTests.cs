using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeInterpolators;

namespace VeloxDev.Core.Test.TransitionSystem;

// These tests exercise the process-wide static registry InterpolatorCore.NativeInterpolators.
// Each test writes only its own private Type key and always removes it in finally, so tests
// are order-independent and never clobber each other or the native defaults — the identity
// assertions hold regardless of parallelization. [DoNotParallelize] is kept purely as
// defense-in-depth for the shared static registry (matching MonoBehaviourManagerTests),
// not because the tests require serial execution.
[TestClass]
[DoNotParallelize]
public class InterpolatorCoreTests
{
    // Private marker types serving as unique, test-owned registration keys.
    private sealed class RegistrationKey { }
    private sealed class OverwriteKey { }
    private sealed class RemovalKey { }
    private sealed class CustomKey { }

    [TestMethod]
    public void RegisterInterpolator_And_TryGet_Succeeds()
    {
        var interp = new DoubleInterpolator();
        InterpolatorCore.RegisterInterpolator(typeof(RegistrationKey), interp);
        try
        {
            Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(RegistrationKey), out var result));
            Assert.AreSame(interp, result);
        }
        finally
        {
            InterpolatorCore.UnregisterInterpolator(typeof(RegistrationKey), out _);
        }
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
        InterpolatorCore.RegisterInterpolator(typeof(RemovalKey), interp);
        var removed = InterpolatorCore.UnregisterInterpolator(typeof(RemovalKey), out var old);

        Assert.IsTrue(removed);
        Assert.AreSame(interp, old);
        Assert.IsFalse(InterpolatorCore.TryGetInterpolator(typeof(RemovalKey), out _));
    }

    [TestMethod]
    public void RegisterInterpolator_OverwritesExisting()
    {
        var old = new DoubleInterpolator();
        var replacement = new DoubleInterpolator();
        InterpolatorCore.RegisterInterpolator(typeof(OverwriteKey), old);
        InterpolatorCore.RegisterInterpolator(typeof(OverwriteKey), replacement);
        try
        {
            Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(OverwriteKey), out var result));
            Assert.AreSame(replacement, result);
        }
        finally
        {
            InterpolatorCore.UnregisterInterpolator(typeof(OverwriteKey), out _);
        }
    }

    [TestMethod]
    public void NativeInterpolators_ContainsDefaults()
    {
        Assert.IsTrue(InterpolatorCore.NativeInterpolators.ContainsKey(typeof(double)));
        Assert.IsTrue(InterpolatorCore.NativeInterpolators.ContainsKey(typeof(long)));
    }

    [TestMethod]
    public void RegisterInterpolator_ForCustomType_Succeeds()
    {
        var interpolator = new FloatInterpolator();
        InterpolatorCore.RegisterInterpolator(typeof(CustomKey), interpolator);
        try
        {
            Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(CustomKey), out var result));
            Assert.AreSame(interpolator, result);
        }
        finally
        {
            InterpolatorCore.UnregisterInterpolator(typeof(CustomKey), out _);
        }
    }
}
