using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

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
        var interp = new DoubleSampler();
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
        var interp = new FloatSampler();
        InterpolatorCore.RegisterInterpolator(typeof(RemovalKey), interp);
        var removed = InterpolatorCore.UnregisterInterpolator(typeof(RemovalKey), out var old);

        Assert.IsTrue(removed);
        Assert.AreSame(interp, old);
        Assert.IsFalse(InterpolatorCore.TryGetInterpolator(typeof(RemovalKey), out _));
    }

    [TestMethod]
    public void RegisterInterpolator_OverwritesExisting()
    {
        var old = new DoubleSampler();
        var replacement = new DoubleSampler();
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
        var interpolator = new FloatSampler();
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

    // A small hierarchy to exercise the fallback: a framework property is very often declared as a subclass of the
    // type an adapter registered, and matching the exact type alone leaves those paths silently unanimated.

    private class Shape { }

    private class Circle : Shape { }

    private sealed class Dot : Circle { }

    private interface ITinted { }

    private interface ITagged { }

    private class TintedCircle : Shape, ITinted { }

    private sealed class TintedAndTagged : TintedCircle, ITagged { }

    [TestMethod]
    public void TryGetInterpolator_FallsBackToABaseClass()
    {
        var registered = new DoubleSampler();
        InterpolatorCore.RegisterInterpolator(typeof(Shape), registered);
        try
        {
            Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(Circle), out var result));
            Assert.AreSame(registered, result);
        }
        finally
        {
            InterpolatorCore.UnregisterInterpolator(typeof(Shape), out _);
        }
    }

    [TestMethod]
    public void TryGetInterpolator_PrefersTheNearestBaseClass()
    {
        var near = new DoubleSampler();
        var far = new FloatSampler();
        InterpolatorCore.RegisterInterpolator(typeof(Shape), far);
        InterpolatorCore.RegisterInterpolator(typeof(Circle), near);
        try
        {
            Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(Dot), out var result));
            Assert.AreSame(near, result);
        }
        finally
        {
            InterpolatorCore.UnregisterInterpolator(typeof(Circle), out _);
            InterpolatorCore.UnregisterInterpolator(typeof(Shape), out _);
        }
    }

    [TestMethod]
    public void TryGetInterpolator_FallsBackToAnInterface()
    {
        var registered = new DoubleSampler();
        InterpolatorCore.RegisterInterpolator(typeof(ITinted), registered);
        try
        {
            // Avalonia registers IBrush and ITransform, so a property declared as the concrete brush has to reach the
            // interface's sampler — a base-class walk alone would never find it.
            Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(TintedCircle), out var result));
            Assert.AreSame(registered, result);
        }
        finally
        {
            InterpolatorCore.UnregisterInterpolator(typeof(ITinted), out _);
        }
    }

    [TestMethod]
    public void TryGetInterpolator_PrefersABaseClassOverAnInterface()
    {
        var fromClass = new DoubleSampler();
        var fromContract = new FloatSampler();
        InterpolatorCore.RegisterInterpolator(typeof(Shape), fromClass);
        InterpolatorCore.RegisterInterpolator(typeof(ITinted), fromContract);
        try
        {
            Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(TintedCircle), out var result));
            Assert.AreSame(fromClass, result);
        }
        finally
        {
            InterpolatorCore.UnregisterInterpolator(typeof(ITinted), out _);
            InterpolatorCore.UnregisterInterpolator(typeof(Shape), out _);
        }
    }

    [TestMethod]
    public void TryGetInterpolator_WithTwoMatchingInterfaces_IsDeterministic()
    {
        var tagged = new DoubleSampler();
        var tinted = new FloatSampler();
        InterpolatorCore.RegisterInterpolator(typeof(ITagged), tagged);
        InterpolatorCore.RegisterInterpolator(typeof(ITinted), tinted);
        try
        {
            // Which one wins is arbitrary; that the same one wins every time is not. Reflection's own order is not
            // specified, so the tie-break has to be explicit.
            Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(TintedAndTagged), out var first));
            Assert.IsTrue(InterpolatorCore.TryGetInterpolator(typeof(TintedAndTagged), out var second));
            Assert.AreSame(first, second);

            // The rule is name order, so ITagged < ITinted.
            Assert.AreSame(tagged, first);
        }
        finally
        {
            InterpolatorCore.UnregisterInterpolator(typeof(ITinted), out _);
            InterpolatorCore.UnregisterInterpolator(typeof(ITagged), out _);
        }
    }
}
