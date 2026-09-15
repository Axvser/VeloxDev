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

    private sealed class ImmediateInspector : ImmediateHost
    {
    }

    /// <summary>An inspector that names a UI thread for a target — the only thing a host's pacer may be derived from.</summary>
    private sealed class AffineInspector : ImmediateHost
    {
        /// <summary>Stands in for the host's UI-thread handle — a dispatcher, a queue, a message loop.</summary>
        public static readonly object Handle = new();

        public override ThreadRef ThreadFor(object target) => ThreadRef.From(Handle);
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    /// <summary>Drives frames from the test instead of from a timer.</summary>
    private sealed class ManualFramePacer : FramePacerCore
    {
        public int Arms { get; private set; }

        public TimeSpan LastInterval { get; private set; }

        /// <summary>Whether the loop released this pacer on its way out.</summary>
        public bool IsDisposed { get; private set; }

        protected override void Arm(TimeSpan interval)
        {
            Arms++;
            LastInterval = interval;
        }

        protected override void Disarm() { }

        /// <summary>Runs the pending continuation, the way a host timer's tick would.</summary>
        public void Tick() => Fire();

        public override void Dispose()
        {
            IsDisposed = true;
            base.Dispose();
        }
    }

    /// <summary>An interpreter that hands out a hand-driven pacer and counts how often it is asked for one.</summary>
    private sealed class PacerInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
        public static ManualFramePacer? Pacer;
        public static int Requests;

        /// <summary>Whether the inspector Core handed over declared affinity at all.</summary>
        /// <summary>Makes <see cref="CreateFramePacer"/> decline, to exercise the default timer path.</summary>
        public static bool SuppressPacer;

        public static void Reset()
        {
            Pacer = null;
            Requests = 0;
            SuppressPacer = false;
        }

        protected override FramePacerCore? CreateFramePacer(object target, IThreadAffinity affinity)
        {
            Interlocked.Increment(ref Requests);
            return SuppressPacer ? null : Pacer = new ManualFramePacer();
        }
    }

    /// <summary>
    /// An interpreter that records what the seam hands it, and then declines a pacer so the default timer drives the
    /// loop and frames keep arriving.
    /// </summary>
    private sealed class SeamInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
        public static object? SeenTarget;
        public static IThreadAffinity? SeenInspector;
        public static ThreadRef SeenHandle;

        /// <summary>The effect's Update callbacks seen so far — one per frame drawn.</summary>
        public static int FramesSeen;

        /// <summary>How many frames had been drawn when the pacer was asked for.</summary>
        public static int FramesWhenAsked = -1;

        public static void Reset()
        {
            SeenTarget = null;
            SeenInspector = null;
            SeenHandle = ThreadRef.None;
            FramesSeen = 0;
            FramesWhenAsked = -1;
        }

        protected override FramePacerCore? CreateFramePacer(object target, IThreadAffinity affinity)
        {
            SeenTarget = target;
            SeenInspector = affinity;
            FramesWhenAsked = FramesSeen;

            // The adapters' shape, verbatim: derived from the inspector's own answer for this target, never
            // re-derived from the platform.
            SeenHandle = affinity.ThreadFor(target);
            return null;
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
    public void Setup()
    {
        PacerInterpreter.Reset();
        SeamInterpreter.Reset();
    }

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

    /// <summary>
    /// The seam itself: the interpreter is asked about the animation's own target, and about the very inspector the
    /// frame set writes through — so the pacer cannot end up on a different thread than the writes.
    /// </summary>
    [TestMethod]
    public async Task ThePacerIsDerivedFromTheAnimatedTargetAndTheFrameSetsOwnInspector()
    {
        var target = new Target();
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, 1d);
        var effect = new TransitionEffectCore { Duration = TimeSpan.FromSeconds(30), FPS = 60 };
        effect.Update += (_, _) => Interlocked.Increment(ref SeamInterpreter.FramesSeen);
        var inspector = new AffineInspector();

        var frameSet = new TestInterpolator().Prepare(target, state, effect, inspector);
        var interpreter = new SeamInterpreter();
        using var cts = new CancellationTokenSource();

        // Fired and forgotten on purpose: everything up to the loop's first await runs synchronously, so by the time
        // this returns the pacer has already been asked for.
        var loop = interpreter.Execute(target, frameSet, effect, cts);

        try
        {
            Assert.IsTrue(ReferenceEquals(target, SeamInterpreter.SeenTarget),
                "the pacer must be asked about the target being animated, not some other object");
            Assert.IsTrue(ReferenceEquals(inspector, SeamInterpreter.SeenInspector),
                "and about the very inspector the frame set holds, or the two answers can disagree");
            Assert.IsTrue(SeamInterpreter.SeenHandle.TryGet<object>(out var handle)
                          && ReferenceEquals(AffineInspector.Handle, handle),
                "the pacer is derived from the inspector's own answer, never re-derived from the platform");

            // 这就是「及早解析」：惰性解析发生在第一次装帧之后，那时首帧已经画过了，这里就不会是 0。对 Avalonia/
            // WinForms 这种「定时器只能建在它将 tick 的那个线程上」的框架，那一个停滞的首帧会让整条动画无声地丢掉 pacer。
            Assert.AreEqual(0, SeamInterpreter.FramesWhenAsked,
                "the pacer must be resolved before the loop draws its first frame");

            var deadline = Environment.TickCount64 + 3000;
            while (Volatile.Read(ref SeamInterpreter.FramesSeen) == 0 && Environment.TickCount64 < deadline) await Task.Delay(10);

            // EmitFrame 每帧无条件调一次 Update，所以这一条一定成立——上面那条断言因此不是空转。
            Assert.IsTrue(SeamInterpreter.FramesSeen > 0,
                "frames must actually be drawn, or the assertion above proves nothing");
        }
        finally
        {
            // 断言失败时也要把这条循环停下来，否则它会带着线程池定时器一直跑到动画结束。
            cts.Cancel();
        }

        await loop;
    }

    /// <summary>
    /// A host callback that throws must not cost the loop its own resources, and must not reach the caller.
    /// </summary>
    /// <remarks>
    /// The pacer is host-supplied and may own a live timer whose only release point is its own <c>Dispose</c>, so a
    /// skipped release leaks one timer per animation — and unlike the exception, the leak lasts for the rest of the
    /// process.
    /// </remarks>
    [TestMethod]
    public async Task AThrowingFinalCallbackIsReportedAndStillReleasesThePacer()
    {
        var target = new Target();
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, 1d);

        // 零时长：整趟 pass 一步走完，不装表，于是这一段循环同步跑到底——上报与释放都已经发生。
        var effect = new TransitionEffectCore { Duration = TimeSpan.Zero, FPS = 60 };
        effect.Finally += (_, _) => throw new InvalidOperationException("a host's own callback");

        TransitionEventArgs? reported = null;
        effect.Error += (_, e) => reported = e;

        var frameSet = new TestInterpolator().Prepare(target, state, effect, new ImmediateInspector());
        var interpreter = new PacerInterpreter();
        using var cts = new CancellationTokenSource();

        await interpreter.Execute(target, frameSet, effect, cts);
        var pacer = PacerInterpreter.Pacer;
        Assert.IsNotNull(pacer, "the interpreter must have been asked for a pacer");

        Assert.IsNotNull(reported, "the callback's exception must reach the Error channel");
        Assert.AreEqual("Finally", reported!.Stage);
        Assert.IsInstanceOfType<InvalidOperationException>(reported.Exception);

        Assert.IsTrue(pacer.IsDisposed,
            "the callback is host code; the resources are the loop's, and one must not be able to take the other");
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
