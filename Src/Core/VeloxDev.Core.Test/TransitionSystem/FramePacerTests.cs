using System.Linq.Expressions;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// The seam that lets a host decide which thread the sampling loop runs on.
/// </summary>
/// <remarks>
/// A pacer that is supplied replaces the default timer outright, so these drive the loop by hand: a tick runs the
/// continuation inline on the calling thread, which makes every assertion exact and thread-free. The property
/// being pinned is that the loop advances <em>only</em> when the pacer fires — that is what makes waiting on the
/// host's own thread possible at all.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class FramePacerTests
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

    /// <summary>Drives frames from the test instead of from a timer.</summary>
    private sealed class ManualFramePacer : FramePacerCore
    {
        public int Arms { get; private set; }

        public TimeSpan LastInterval { get; private set; }

        protected override void Arm(TimeSpan interval)
        {
            Arms++;
            LastInterval = interval;
        }

        protected override void Disarm() { }

        /// <summary>Runs the pending continuation, the way a host timer's tick would.</summary>
        public void Tick() => Fire();
    }

    /// <summary>An interpreter that hands out a hand-driven pacer and counts how often it is asked for one.</summary>
    private sealed class PacerInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
        public static ManualFramePacer? Pacer;
        public static int Requests;

        /// <summary>Makes <see cref="CreateFramePacer"/> decline, to exercise the default timer path.</summary>
        public static bool SuppressPacer;

        public static void Reset()
        {
            Pacer = null;
            Requests = 0;
            SuppressPacer = false;
        }

        protected override FramePacerCore? CreateFramePacer()
        {
            Interlocked.Increment(ref Requests);
            return SuppressPacer ? null : Pacer = new ManualFramePacer();
        }
    }

    private sealed class TestTransition : TransitionCore<
        Target, StateCore, TransitionEffectCore, TestInterpolator, ImmediateInspector, PacerInterpreter, NonPriority>
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

    [TestInitialize]
    public void Setup() => PacerInterpreter.Reset();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var target in _targets) TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        _targets.Clear();
    }

    private readonly List<Target> _targets = [];

    private Target StartAnimation(int fps)
    {
        var target = new Target();
        _targets.Add(target);
        TestTransition.Create()
            .Property(t => t.Value, 1d)
            .Effect(new TransitionEffectCore { Duration = TimeSpan.FromSeconds(30), FPS = fps })
            .Execute(target);
        return target;
    }

    [TestMethod]
    public async Task TheLoopAdvancesOnlyWhenThePacerFires()
    {
        var target = StartAnimation(fps: 60);
        var pacer = PacerInterpreter.Pacer;
        Assert.IsNotNull(pacer, "the interpreter must have been asked for a pacer");

        // 第一帧在挂起之前同步跑完，所以这里已经有一帧了。
        Assert.AreEqual(1, pacer.Arms, "one arm per frame, and the first frame ran synchronously");

        var updatesAfterFirstFrame = pacer.Arms;
        var valueAfterFirstFrame = target.Value;

        // 关键断言：注入了 pacer 之后，默认的线程池定时器不再参与——真实时间流逝不会带来任何一帧。
        await Task.Delay(200);

        Assert.AreEqual(updatesAfterFirstFrame, pacer.Arms, "nothing may run while the pacer has not fired");
        Assert.AreEqual(valueAfterFirstFrame, target.Value);

        pacer.Tick();
        Assert.AreEqual(updatesAfterFirstFrame + 1, pacer.Arms, "a tick must produce exactly one frame");

        // 走几步之后动画确实推进了——pacer 就是这条循环的时钟。
        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(30);
            pacer.Tick();
        }

        Assert.IsTrue(target.Value > valueAfterFirstFrame, "ticked frames must advance the animation");
    }

    [TestMethod]
    public void ThePacerIsAskedForAtMostOnce()
    {
        var target = StartAnimation(fps: 60);
        var pacer = PacerInterpreter.Pacer!;

        for (var i = 0; i < 8; i++) pacer.Tick();

        // 每帧问一次就会每帧造一个定时器——一个动画挂 8 个平台定时器。
        Assert.AreEqual(1, PacerInterpreter.Requests, "one pacer per interpreter, not one per frame");
        Assert.IsTrue(pacer.Arms >= 8);
        Assert.IsTrue(target.Value > 0d);
    }

    [TestMethod]
    public void TheFrameIntervalIsTheEffectsFpsCap()
    {
        StartAnimation(fps: 25);
        var pacer = PacerInterpreter.Pacer!;

        // 帧间隔就是 FPS 上限的倒数，而且是每帧重新给的（effect 的 FPS 可以在动画中途变）。
        Assert.AreEqual(TimeSpan.FromMilliseconds(40), pacer.LastInterval);

        pacer.Tick();
        Assert.AreEqual(TimeSpan.FromMilliseconds(40), pacer.LastInterval);
    }

    [TestMethod]
    public async Task WithoutAPacerTheDefaultTimerStaysInPlace()
    {
        // CreateFramePacer 返回 null 时必须继续工作，而不是把循环挂死——平台在某些情形下（WinForms 不在 UI 线程
        // 上、WinUI 取不到队列、Blazor 根本没有 UI 线程定时器）刻意走这条退路。
        PacerInterpreter.SuppressPacer = true;
        var target = StartAnimation(fps: 60);

        Assert.IsNull(PacerInterpreter.Pacer);

        var deadline = Environment.TickCount64 + 3000;
        while (target.Value <= 0.05d && Environment.TickCount64 < deadline) await Task.Delay(10);

        Assert.IsTrue(target.Value > 0.05d, "the thread-pool pacer must still drive the loop");
    }

    [TestMethod]
    public void AnAlreadyCancelledTokenRunsTheContinuationImmediately()
    {
        using var pacer = new ManualFramePacer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ran = 0;
        pacer.Schedule(() => ran++, TimeSpan.FromSeconds(30), cts.Token);

        Assert.AreEqual(1, ran, "a stop must take effect now, not one frame later");
        Assert.AreEqual(0, pacer.Arms, "and nothing may be armed for it");
    }

    [TestMethod]
    public void APendingContinuationRunsOnceHoweverManyTicksArrive()
    {
        using var pacer = new ManualFramePacer();
        var ran = 0;

        pacer.Schedule(() => ran++, TimeSpan.FromMilliseconds(16), CancellationToken.None);
        Assert.AreEqual(1, pacer.Arms);

        pacer.Tick();
        Assert.AreEqual(1, ran);

        // 重复 tick（一个重复定时器、一个迟到的排队 tick）不得再放行一次：重复采样会重复写一帧。
        pacer.Tick();
        pacer.Tick();
        Assert.AreEqual(1, ran, "one continuation, however many ticks");
    }

    [TestMethod]
    public void DisposingReleasesAPendingContinuation()
    {
        var pacer = new ManualFramePacer();
        var ran = 0;

        pacer.Schedule(() => ran++, TimeSpan.FromMilliseconds(16), CancellationToken.None);
        Assert.AreEqual(0, ran);

        // 不放行的话，停在这次等待上的循环就永远停在那里——没有异常，也没有帧。
        pacer.Dispose();

        Assert.AreEqual(1, ran, "disposing a pacer must not strand the loop waiting on it");
    }

    [TestMethod]
    public void ASupersedingScheduleReplacesThePendingOne()
    {
        using var pacer = new ManualFramePacer();
        var first = 0;
        var second = 0;

        pacer.Schedule(() => first++, TimeSpan.FromMilliseconds(16), CancellationToken.None);
        pacer.Schedule(() => second++, TimeSpan.FromMilliseconds(16), CancellationToken.None);

        // 一条循环一次只挂一个续体，所以替换是唯一正确的语义；排队会让一个已经过期的续体在一帧之后补跑。
        pacer.Tick();

        Assert.AreEqual(0, first);
        Assert.AreEqual(1, second);
    }
}
