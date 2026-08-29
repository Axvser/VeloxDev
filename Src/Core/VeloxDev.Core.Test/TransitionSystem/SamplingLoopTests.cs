using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class SamplingLoopTests
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
        public override void ProtectedInvoke(object target, Action action) => action();
    }

    // Duration=0 makes each pass sample exactly once (no real-time wait), so these tests are deterministic.
    private static async Task<(Target Target, int UpdateCount, bool Completed, bool Canceled, bool Finally)> RunAsync(
        double current,
        double end,
        Action<TransitionEffectCore> configure,
        bool handledBeforeStart = false)
    {
        var target = new Target { Value = current };
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, end);
        var effect = new TransitionEffectCore();
        configure(effect);

        int updateCount = 0;
        bool completed = false, canceled = false, finallyFired = false;
        effect.Update += (_, _) => updateCount++;
        effect.Completed += (_, _) => completed = true;
        effect.Canceled += (_, _) => canceled = true;
        effect.Finally += (_, _) => finallyFired = true;

        var frameSet = new TestInterpolator().Prepare(target, state, effect, new ImmediateInspector());
        var interpreter = new TestInterpreter();
        if (handledBeforeStart) interpreter.Args.Handled = true;
        using var cts = new CancellationTokenSource();
        await interpreter.Execute(target, frameSet, effect, cts);

        return (target, updateCount, completed, canceled, finallyFired);
    }

    [TestMethod]
    public async Task DurationZero_JumpsToEnd_AndCompletes()
    {
        var result = await RunAsync(0d, 100d, e => e.Duration = TimeSpan.Zero);
        Assert.AreEqual(100d, result.Target.Value);
        Assert.IsTrue(result.Completed);
        Assert.IsFalse(result.Canceled);
        Assert.IsTrue(result.Finally);
        Assert.AreEqual(1, result.UpdateCount);
    }

    [TestMethod]
    public async Task DurationZero_AutoReverse_EndsAtStart()
    {
        var result = await RunAsync(0d, 100d, e =>
        {
            e.Duration = TimeSpan.Zero;
            e.IsAutoReverse = true;
        });
        // forward pass samples end, reverse pass samples start → final is start
        Assert.AreEqual(0d, result.Target.Value);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(2, result.UpdateCount);
    }

    [TestMethod]
    public async Task LoopTime_ZeroDuration_RunsOneSamplePerPass()
    {
        var result = await RunAsync(0d, 100d, e =>
        {
            e.Duration = TimeSpan.Zero;
            e.LoopTime = 2;
        });
        // LoopTime=2 → 3 forward passes (cycle 0,1,2), one sample each
        Assert.AreEqual(100d, result.Target.Value);
        Assert.AreEqual(3, result.UpdateCount);
    }

    [TestMethod]
    public async Task HandledBeforeStart_CancelsAndFiresFinally()
    {
        var result = await RunAsync(0d, 100d, e => e.Duration = TimeSpan.Zero, handledBeforeStart: true);
        Assert.IsFalse(result.Completed);
        Assert.IsTrue(result.Canceled);
        Assert.IsTrue(result.Finally);
        Assert.AreEqual(0, result.UpdateCount);
    }
}
