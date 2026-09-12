using System.Linq.Expressions;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// Runtime control of a running animation. Pause, resume, rate and seek are all the same rebase of one timeline,
/// so what these pin is that each moves the animation without disturbing the others — and that a paused animation
/// is still cancellable, since its sampling loop is parked on a signal rather than on a timer.
/// </summary>
/// <remarks>
/// Not parallelized: these observe an animation running in real time, and the assertions are ratios rather than
/// absolute timings, so a loaded machine would still pass — but there is no reason to invite it.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class TimelineControlTests
{
    private sealed class Target
    {
        public double Value { get; set; }
    }

    private sealed class ImmediateInspector : UIThreadInspectorCore
    {
        public override bool IsAppAlive() => true;
        public override bool IsUIThread() => true;
        public override object? ProtectedGetValue(object target, ITransitionProperty property) => property.GetValue(target);
        public override bool ProtectedInvoke(object target, Action action) { action(); return true; }
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    /// <summary>The whole core wired up the way an adapter wires it, minus the platform.</summary>
    private sealed class TestTransition : TransitionCore<
        Target, StateCore, TransitionEffectCore, TestInterpolator, ImmediateInspector, TestInterpreter, NonPriority>
    {
        public static TestTransition Create() => TransitionCore.Create<TestTransition>();

        public TestTransition Property<TValue>(Expression<Func<Target, TValue>> lambda, TValue value)
        {
            state.SetValue(lambda, value);
            return this;
        }

        public TestTransition Effect(TransitionEffectCore effect)
            => CoreEffect<TestTransition, TransitionEffectCore>(effect);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    private static void Stop(Target target)
        => TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

    private static double Ms(TimeSpan value) => value.TotalMilliseconds;

    [TestMethod]
    public async Task Timeline_PauseThenResume_ExcludesThePausedInterval()
    {
        var timeline = new TransitionTimeline();

        await Task.Delay(150);
        var beforePause = timeline.Position;
        Assert.IsTrue(Ms(beforePause) >= 100d, $"the timeline must have advanced, was {beforePause}");

        timeline.Pause();
        Assert.IsTrue(timeline.IsPaused);

        // Four times the elapsed so far: a paused timeline must not move through any of it.
        await Task.Delay(600);
        var whilePaused = timeline.Position;
        Assert.IsTrue(Math.Abs(Ms(whilePaused - beforePause)) <= 1d,
            $"the position moved while paused: {beforePause} -> {whilePaused}");

        timeline.Resume();
        Assert.IsFalse(timeline.IsPaused);

        await Task.Delay(150);
        var afterResume = timeline.Position;
        Assert.IsTrue(Ms(afterResume - whilePaused) >= 100d,
            $"the timeline must advance again once resumed, went {whilePaused} -> {afterResume}");

        // And the paused stretch is excluded rather than merely hidden: the total is far short of the wall clock.
        Assert.IsTrue(Ms(afterResume) < 500d,
            $"the paused interval must not be counted, the total was {afterResume}");
    }

    [TestMethod]
    public async Task Timeline_SeekLandsOnThePosition_AndKeepsTheRate()
    {
        var timeline = new TransitionTimeline();
        timeline.SetRate(2d);
        Assert.AreEqual(2d, timeline.Rate, 0.001d);

        timeline.Seek(TimeSpan.FromSeconds(5));
        Assert.IsTrue(Math.Abs(Ms(timeline.Position - TimeSpan.FromSeconds(5))) <= 20d,
            $"a seek must land on the requested position, was {timeline.Position}");
        Assert.AreEqual(2d, timeline.Rate, 0.001d, "a seek must not disturb the rate");

        // Seeking while paused has to land too — that is the whole point of drawing a frame on the way into the
        // pause gate rather than only when the animation resumes.
        timeline.Pause();
        timeline.Seek(TimeSpan.FromSeconds(1));
        Assert.IsTrue(Math.Abs(Ms(timeline.Position - TimeSpan.FromSeconds(1))) <= 20d,
            $"a seek while paused must land as well, was {timeline.Position}");

        // A rate given while paused is remembered, not applied: only Resume starts it moving again.
        timeline.SetRate(3d);
        await Task.Delay(120);
        Assert.IsTrue(Math.Abs(Ms(timeline.Position - TimeSpan.FromSeconds(1))) <= 20d,
            $"a rate set while paused must not start playback, was {timeline.Position}");

        timeline.Resume();
        Assert.AreEqual(3d, timeline.Rate, 0.001d);
    }

    [TestMethod]
    public void Timeline_NegativeRate_IsRejected()
    {
        var timeline = new TransitionTimeline();
        timeline.Seek(TimeSpan.FromSeconds(2));

        // Time only moves forwards. A negative rate is refused rather than clamped: clamping to zero would leave the
        // animation silently not running, and clamping to a forward speed would ignore what was asked for.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => timeline.SetRate(-1d));

        // And the refusal leaves the timeline exactly as it was.
        Assert.AreEqual(1d, timeline.Rate, 0.001d);
        Assert.IsTrue(Math.Abs(Ms(timeline.Position - TimeSpan.FromSeconds(2))) <= 20d,
            $"a rejected rate must not disturb the position, was {timeline.Position}");
    }

    [TestMethod]
    public async Task SetRate_Negative_IsRejectedAtTheTarget()
    {
        var target = new Target();
        var effect = new TransitionEffectCore { Duration = TimeSpan.FromSeconds(4), FPS = 60 };

        TestTransition.Create().Property(t => t.Value, 100d).Effect(effect).Execute(target);
        await Task.Delay(60);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SetRate(target, -1d));
        Assert.AreEqual(1d, Rate(target), 0.001d);

        Stop(target);
    }

    [TestMethod]
    public async Task Pause_FreezesTheAnimation_AndItDoesNotComplete()
    {
        var target = new Target();
        var effect = new TransitionEffectCore { Duration = TimeSpan.FromMilliseconds(400), FPS = 60 };
        bool completed = false;
        effect.Completed += (_, _) => completed = true;

        TestTransition.Create()
            .Property(t => t.Value, 100d)
            .Effect(effect)
            .Execute(target);

        await Task.Delay(100);
        Pause(target);
        var frozen = Position(target).TotalMilliseconds;

        // Longer than the whole remaining duration: a paused animation must not advance through it.
        await Task.Delay(500);

        Assert.IsFalse(completed, "a paused animation must not run to completion");
        Assert.IsTrue(IsPaused(target));
        var stillFrozen = Position(target).TotalMilliseconds;
        Assert.IsTrue(Math.Abs(stillFrozen - frozen) <= 1d, $"the position moved while paused: {frozen} -> {stillFrozen}");

        Resume(target);
        Assert.IsTrue(await WaitUntilAsync(() => completed, 5000), "the animation must finish once resumed");
        Assert.AreEqual(100d, target.Value, 0.001d);

        Stop(target);
    }

    [TestMethod]
    public async Task Exit_WhilePaused_StillEndsTheAnimation()
    {
        var target = new Target();
        var effect = new TransitionEffectCore { Duration = TimeSpan.FromSeconds(5), FPS = 60 };
        bool canceled = false, completed = false;
        effect.Canceled += (_, _) => canceled = true;
        effect.Completed += (_, _) => completed = true;

        TestTransition.Create()
            .Property(t => t.Value, 100d)
            .Effect(effect)
            .Execute(target);

        await Task.Delay(60);
        Pause(target);

        // The loop is now parked on the pause signal, not on a timer — this is the window where a cancel has to
        // reach it through the token rather than through the next frame.
        await Task.Delay(60);
        Stop(target);

        Assert.IsTrue(await WaitUntilAsync(() => canceled || completed, 5000), "a paused animation must still be cancellable");
        Assert.IsTrue(canceled, "the animation was stopped, so it must report cancellation");
        Assert.IsFalse(completed);
    }

    [TestMethod]
    public async Task SetRate_FinishesWellBeforeTheNominalDuration()
    {
        var target = new Target();
        var effect = new TransitionEffectCore { Duration = TimeSpan.FromMilliseconds(1500), FPS = 60 };
        bool completed = false;
        effect.Completed += (_, _) => completed = true;

        var started = Environment.TickCount64;
        TestTransition.Create()
            .Property(t => t.Value, 100d)
            .Effect(effect)
            .Execute(target);

        await Task.Delay(100);
        SetRate(target, 6d);

        Assert.IsTrue(await WaitUntilAsync(() => completed, 8000), "the animation must complete");
        var elapsed = Environment.TickCount64 - started;
        Assert.IsTrue(elapsed < 900, $"a 1500ms animation at 6x must finish well inside 900ms, took {elapsed}ms");
        Assert.AreEqual(100d, target.Value, 0.001d);
    }

    [TestMethod]
    public async Task Seek_MovesTheAnimationToTheRequestedPosition()
    {
        var target = new Target();
        var effect = new TransitionEffectCore { Duration = TimeSpan.FromSeconds(10), FPS = 60 };

        TestTransition.Create()
            .Property(t => t.Value, 100d)
            .Effect(effect)
            .Execute(target);

        await Task.Delay(60);
        Assert.IsTrue(target.Value < 5d, $"the animation should still be near its start, was {target.Value}");

        Seek(target, TimeSpan.FromSeconds(9));
        await Task.Delay(60);

        Assert.IsTrue(target.Value > 70d, $"a seek to 9 of 10 seconds should be near the end, was {target.Value}");

        Stop(target);
    }

    [TestMethod]
    public async Task Loop_TakesTheDeclaredDuration()
    {
        var target = new Target();
        var effect = new TransitionEffectCore { Duration = TimeSpan.FromMilliseconds(400), FPS = 60 };
        bool completed = false;
        effect.Completed += (_, _) => completed = true;

        var started = Environment.TickCount64;
        TestTransition.Create()
            .Property(t => t.Value, 100d)
            .Effect(effect)
            .Execute(target);

        Assert.IsTrue(await WaitUntilAsync(() => completed, 5000), "the animation must complete");
        var elapsed = Environment.TickCount64 - started;

        // The clock is read through the timeline now, so both bounds matter: finishing early would mean the loop
        // jumped straight to the endpoint, and the slack on the far side absorbs a machine under load.
        Assert.IsTrue(elapsed >= 350, $"a 400ms animation finished after only {elapsed}ms");
        Assert.IsTrue(elapsed <= 1500, $"a 400ms animation took {elapsed}ms");
        Assert.AreEqual(100d, target.Value, 0.001d);
    }

    [TestMethod]
    public async Task Seek_ToAnotherPass_MovesThePassCounter()
    {
        var target = new Target();
        var effect = new TransitionEffectCore
        {
            Duration = TimeSpan.FromMilliseconds(300),
            FPS = 60,
            LoopTime = 5,
        };

        TestTransition.Create().Property(t => t.Value, 100d).Effect(effect).Execute(target);
        await Task.Delay(60);
        Assert.AreEqual(0, Cycle(target), "the animation starts in pass zero");

        // Naming another pass is what the integer counter buys: the timeline itself only knows elapsed time, and a
        // zero-duration pass consumes none of it, so time alone could never address one.
        Seek(target, cycle: 3, position: TimeSpan.FromMilliseconds(150));
        await Task.Delay(50);

        Assert.AreEqual(3, Cycle(target));
        Assert.IsTrue(target.Value > 40d && target.Value < 95d,
            $"roughly halfway into a 300ms pass, was {target.Value}");

        Stop(target);
    }

    [TestMethod]
    public async Task SharedTimeline_PausesBothAnimationsAtOnce()
    {
        var first = new Target();
        var second = new Target();
        var timeline = new TransitionTimeline();

        TestTransition.Create().Property(t => t.Value, 100d)
            .Effect(new TransitionEffectCore { Duration = TimeSpan.FromSeconds(4), FPS = 60 })
            .Execute(first, timeline);
        TestTransition.Create().Property(t => t.Value, 100d)
            .Effect(new TransitionEffectCore { Duration = TimeSpan.FromSeconds(4), FPS = 60 })
            .Execute(second, timeline);

        await Task.Delay(80);
        Assert.IsTrue(first.Value > 0d, "the shared timeline must be running");

        // Pausing through one target stops the other as well: they are anchored to the same clock, which is the
        // whole point of handing one in.
        Pause(first);

        // Read the frozen values only once both loops have reached the gate: on the way in, each draws one last
        // frame at the instant it paused, which is slightly ahead of the last frame it had drawn.
        await Task.Delay(120);
        var frozenFirst = first.Value;
        var frozenSecond = second.Value;

        await Task.Delay(300);

        Assert.IsTrue(IsPaused(second), "an animation sharing the timeline must be paused with it");
        Assert.AreEqual(frozenFirst, first.Value, 0.001d);
        Assert.AreEqual(frozenSecond, second.Value, 0.001d);

        Resume(first);
        await Task.Delay(150);
        Assert.IsTrue(second.Value > frozenSecond, "both must run again once the shared timeline resumes");

        Stop(first);
        Stop(second);
    }

    [TestMethod]
    public void Queries_OnATargetWithNoAnimation_ReportNothingRunning()
    {
        var target = new Target();

        Assert.IsFalse(IsPaused(target));
        Assert.AreEqual(0d, Position(target).TotalMilliseconds);
    }

    private static void Pause(Target target) => TransitionCore.Pause(target);
    private static void Resume(Target target) => TransitionCore.Resume(target);
    private static void SetRate(Target target, double rate) => TransitionCore.SetRate(target, rate);
    private static double Rate(Target target) => TransitionCore.Rate(target);
    private static void Seek(Target target, TimeSpan position) => TransitionCore.Seek(target, position);
    private static void Seek(Target target, int cycle, TimeSpan position) => TransitionCore.Seek(target, cycle, position);
    private static bool IsPaused(Target target) => TransitionCore.IsPaused(target);
    private static TimeSpan Position(Target target) => TransitionCore.Position(target);
    private static int Cycle(Target target) => TransitionCore.Cycle(target);
}
