using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// What one frame of the sampling loop costs, measured rather than read off the signatures.
/// </summary>
/// <remarks>
/// The write path is the one place this subsystem claims to be allocation-free, and a signature cannot show whether
/// a lambda at the call site closed over the frame's locals. The property here is a reference type with a scratch
/// sampler on purpose: a value-type one boxes on its way into <c>ITransitionProperty.SetValue</c>, which would hide
/// everything else behind it.
/// </remarks>
[TestClass]
public class FramePathAllocationTests
{
    private sealed class Box
    {
        public double Value;
    }

    private sealed class Target
    {
        public Box Value { get; set; } = new();
    }

    /// <summary>Interpolates in place, so the sampler itself contributes no allocation.</summary>
    private sealed class BoxSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (start is not Box from || end is not Box to) return;

            var scratch = working as Box;
            if (scratch is null || ReferenceEquals(scratch, from) || ReferenceEquals(scratch, to))
            {
                scratch = new Box();
                working = scratch;
            }

            scratch.Value = from.Value + (to.Value - from.Value) * t;
            property.SetValue(target, scratch);
        }
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    /// <summary>Hands out a pacer the test ticks itself, so a frame is a call and not a wait.</summary>
    private sealed class TickingInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
        public ManualPacer? Pacer;

        protected override FramePacerCore? CreateFramePacer(object target, IThreadAffinity affinity)
            => Pacer = new ManualPacer();
    }

    private sealed class ManualPacer : FramePacerCore
    {
        protected override void Arm(TimeSpan interval) { }

        protected override void Disarm() { }

        /// <summary>Runs the pending continuation, the way a host timer's tick would.</summary>
        public void Tick() => Fire();
    }

    [TestMethod]
    public void OneFrameOfTheSamplingLoopDoesNotAllocate()
    {
        var target = new Target { Value = new Box { Value = 0d } };
        var end = new Box { Value = 100d };

        var state = new StateCore();
        state.SetValue<Target, Box>(t => t.Value, end);
        state.SetInterpolator<Target, Box>(t => t.Value, new BoxSampler());

        // Long enough that no frame in the measured window is the last one, which takes a different branch.
        var effect = new TransitionEffectCore { Duration = TimeSpan.FromSeconds(30), FPS = 60 };
        var host = new ImmediateHost();
        var frameSet = new TestInterpolator().Prepare(target, state, effect, host);
        var interpreter = new TickingInterpreter();
        using var cts = new CancellationTokenSource();

        var loop = interpreter.Execute(target, frameSet, effect, cts);
        var pacer = interpreter.Pacer;
        Assert.IsNotNull(pacer, "the interpreter must have been asked for a pacer");

        // Warm-up: the interpreter's first frames resolve the pacer and build whatever it reuses.
        for (var index = 0; index < 5; index++) pacer.Tick();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++) pacer.Tick();
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.IsTrue(target.Value.Value > 0d, "the frames must have been drawn, or this measures nothing");
        Assert.AreEqual(before, after, $"one frame allocated {(after - before) / 100.0:F1} bytes");

        cts.Cancel();
        pacer.Tick();
        _ = loop;
    }

    /// <summary>
    /// A query on a target with nothing running is the common case for a per-frame readout, and it used to build two
    /// lists to answer "nothing".
    /// </summary>
    [TestMethod]
    public void QueryingATargetWithNothingRunningDoesNotAllocate()
    {
        var target = new Target();

        // Warm up: the weak tables and the first lookups.
        _ = TransitionCore.Position(target);
        _ = TransitionCore.Cycle(target);
        _ = TransitionCore.Rate(target);
        _ = TransitionCore.IsPaused(target);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++)
        {
            _ = TransitionCore.Position(target);
            _ = TransitionCore.Cycle(target);
            _ = TransitionCore.Rate(target);
            _ = TransitionCore.IsPaused(target);
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(before, after, $"four idle queries allocated {(after - before) / 100.0:F1} bytes");
    }
}
