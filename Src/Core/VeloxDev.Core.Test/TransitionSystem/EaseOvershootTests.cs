using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// The eased time is handed to the samplers unclamped, so a Back or Elastic curve can drive a property past its end
/// value and back. The final frame of a pass is still the exact endpoint — that is decided from the raw progress,
/// not from the eased value, so it is unaffected by the easing curve.
/// </summary>
[TestClass]
public class EaseOvershootTests
{
    private sealed class Target
    {
        public double Value { get; set; }
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    private sealed class ImmediateInspector : UIThreadInspectorCore
    {
        public override bool IsAppAlive() => true;
        public override bool IsUIThread() => true;
        public override object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
        public override bool ProtectedInvoke(object target, Action action) { action(); return true; }
    }

    /// <summary>Returns a fixed eased value, so the test does not depend on frame timing to hit the overshoot.</summary>
    private sealed class FixedEase(double value) : IEaseCalculator
    {
        public double Ease(double t) => value;
    }

    private static ITransitionProperty Property => TransitionProperty.FromProperty(typeof(Target).GetProperty(nameof(Target.Value))!);

    /// <summary>Runs one pass and returns the value applied on every frame, in order.</summary>
    private static async Task<List<double>> SampleAsync(double end, double eased)
    {
        var target = new Target { Value = 0d };
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, end);
        var effect = new TransitionEffectCore
        {
            Duration = TimeSpan.FromMilliseconds(120),
            FPS = 240,
            Ease = new FixedEase(eased),
        };

        var samples = new List<double>();
        effect.LateUpdate += (_, _) => samples.Add(target.Value); // fires after the frame has been applied

        var frameSet = new TestInterpolator().Prepare<NonPriority>(target, state, effect, new ImmediateInspector());
        using var cts = new CancellationTokenSource();
        await new TestInterpreter().Execute(target, frameSet, effect, cts);

        return samples;
    }

    [TestMethod]
    public void Sampler_Extrapolates_BeyondEitherEndpoint()
    {
        var target = new Target();
        var sampler = new DoubleSampler();
        object? working = null;

        sampler.InsertFrame(target, Property, ref working, 0d, 100d, null, 1.5);   // exact in binary
        Assert.AreEqual(150d, target.Value);

        sampler.InsertFrame(target, Property, ref working, 0d, 100d, null, -0.5);
        Assert.AreEqual(-50d, target.Value);
    }

    [TestMethod]
    public void Sampler_AtEitherEndpoint_IsStillExact()
    {
        // Dropping the out-of-range guards must not change t == 0 / t == 1.
        var target = new Target();
        var sampler = new DoubleSampler();
        object? working = null;

        sampler.InsertFrame(target, Property, ref working, 10d, 100d, null, 0d);
        Assert.AreEqual(10d, target.Value);

        sampler.InsertFrame(target, Property, ref working, 10d, 100d, null, 1d);
        Assert.AreEqual(100d, target.Value);
    }

    [TestMethod]
    public async Task Ease_Overshoot_ReachesPastTheEndValue()
    {
        var samples = await SampleAsync(end: 100d, eased: 1.5d);

        Assert.IsTrue(samples.Count >= 2, "the pass must produce more than the endpoint frame");
        Assert.AreEqual(150d, samples.Max(), "the eased value must reach the property unclamped");
    }

    [TestMethod]
    public async Task Ease_Anticipation_ReachesBelowTheStartValue()
    {
        var samples = await SampleAsync(end: 100d, eased: -0.5d);

        Assert.IsTrue(samples.Count >= 2, "the pass must produce more than the endpoint frame");
        Assert.AreEqual(-50d, samples.Min(), "the eased value must reach the property unclamped");
    }

    [TestMethod]
    public async Task Ease_Overshoot_StillLandsExactlyOnTheEndValue()
    {
        var samples = await SampleAsync(end: 100d, eased: 1.5d);

        Assert.AreEqual(100d, samples[^1], "the final frame of a pass is the exact endpoint");
    }

    [TestMethod]
    public void Sampler_WithARangeInvariant_PinsToItsEndpointForNow()
    {
        // A sampler that has not been adapted yet keeps its guard, so during an overshoot it holds the endpoint
        // instead of extrapolating into a value its type cannot represent. Colour is the clearest case: its channels
        // are bytes, and (byte)(200 + 110 * 1.0) wraps to 54 rather than saturating. This test marks the interim
        // behaviour — it should be replaced, not deleted, when the special cases are adapted.
        var target = new ColorTarget();
        var sampler = new ColorSampler();
        var property = TransitionProperty.FromProperty(typeof(ColorTarget).GetProperty(nameof(ColorTarget.Color))!);
        object? working = null;

        var end = System.Drawing.Color.FromArgb(255, 255, 0, 0);
        sampler.InsertFrame(target, property, ref working, System.Drawing.Color.Black, end, null, 1.1);

        Assert.AreEqual(end, target.Color);
    }

    private sealed class ColorTarget
    {
        public System.Drawing.Color Color { get; set; }
    }
}
