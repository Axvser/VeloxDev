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
    public void BoundedProgress_WithinTheUnitInterval_IsTheEasedTimeItself()
    {
        // Interpolating between two in-range endpoints stays in range, so no channel can exit early and the group
        // keeps the full eased time. Every overshoot test above depends on this being exact.
        var progress = new BoundedProgress(0.42, 0d, 255d);
        progress.Add(200d, 240d);
        progress.Add(100d, 180d);
        progress.Add(50d, 120d);

        Assert.AreEqual(0.42, progress.Progress);
    }

    [TestMethod]
    public void BoundedProgress_OnlyEverTightens_WhateverOrderTheChannelsComeIn()
    {
        // The channels of one value move together until the first of them would leave the range: red exits at 1.375,
        // so the group stops there no matter which channel was added last.
        var forward = new BoundedProgress(1.5, 0d, 255d);
        forward.Add(200d, 240d);   // exits at 1.375
        forward.Add(100d, 180d);   // would exit at 1.9375

        var reversed = new BoundedProgress(1.5, 0d, 255d);
        reversed.Add(100d, 180d);
        reversed.Add(200d, 240d);

        Assert.AreEqual(1.375, forward.Progress);
        Assert.AreEqual(forward.Progress, reversed.Progress);
    }

    private static ITransitionProperty ColorProperty
        => TransitionProperty.FromProperty(typeof(ColorTarget).GetProperty(nameof(ColorTarget.Color))!);

    [TestMethod]
    public void ColorSampler_AtOvershoot_StopsAtTheFirstChannelToReachItsLimit()
    {
        // (200,100,50) -> (240,180,120) overshooting to t == 1.5. Red is the first channel to reach 255, and because
        // the channels share one progress the other two stop with it: red 200+40*1.375 = 255, green 100+80*1.375 =
        // 210, blue 50+70*1.375 = 146.25. Per-channel clamping would have let green and blue run on to 175 / 125 and
        // shifted the hue.
        var target = new ColorTarget();
        var sampler = new ColorSampler();
        object? working = null;

        sampler.InsertFrame(target, ColorProperty, ref working,
            System.Drawing.Color.FromArgb(255, 200, 100, 50),
            System.Drawing.Color.FromArgb(255, 240, 180, 120), null, 1.5);

        Assert.AreEqual(System.Drawing.Color.FromArgb(255, 255, 210, 146), target.Color);
    }

    [TestMethod]
    public void ColorSampler_AlphaSaturatesInsteadOfWrapping()
    {
        // Alpha is its own range, so it still takes the full eased time: 200 + 50*1.5 = 275, which a bare byte cast
        // would wrap to 19.
        var target = new ColorTarget();
        var sampler = new ColorSampler();
        object? working = null;

        sampler.InsertFrame(target, ColorProperty, ref working,
            System.Drawing.Color.FromArgb(200, 0, 0, 0),
            System.Drawing.Color.FromArgb(250, 0, 0, 0), null, 1.5);

        Assert.AreEqual(255, target.Color.A);
    }

    [TestMethod]
    public void ColorSampler_OpaqueTarget_DoesNotTruncateTheColourOvershoot()
    {
        // Alpha reaches its limit exactly at t == 1. If alpha were part of the shared group it would cap the progress
        // at 1 and the colour would never overshoot at all — the common case of fading to an opaque colour.
        var target = new ColorTarget();
        var sampler = new ColorSampler();
        object? working = null;

        sampler.InsertFrame(target, ColorProperty, ref working,
            System.Drawing.Color.FromArgb(0, 100, 100, 100),
            System.Drawing.Color.FromArgb(255, 200, 100, 100), null, 1.5);

        Assert.AreEqual(250, target.Color.R, "red keeps overshooting while alpha saturates");
        Assert.AreEqual(255, target.Color.A);
    }

    private sealed class ColorTarget
    {
        public System.Drawing.Color Color { get; set; }
    }
}
