using VeloxDev.TransitionSystem;
using VeloxDev.Timing;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// The two windows around a cancelled animation: the Awake that is already queued to the UI thread when the
/// animation is cancelled, and the cancellation callbacks that run while Exit still holds the target lock.
/// </summary>
[TestClass]
public class TransitionSchedulerExitTests
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

    /// <summary>
    /// Models the fire-and-forget adapters: a write is queued and runs when the message is pumped — a different
    /// moment from the one that queued it — while a read is answered inline.
    /// </summary>
    private sealed class DeferredInspector : TransitionHostBase<NonPriority>
    {
        public static readonly List<Action> Pending = [];

        public override ThreadRef ThreadFor(object target) => ThreadRef.None;

        protected override bool IsCurrentThread(ThreadRef thread) => false;

        protected override bool PostCore(object target, Action action, NonPriority priority) { Pending.Add(action); return true; }

        public override T Run<T>(object target, Func<T> body) => body();

        public static void Pump()
        {
            var pending = Pending.ToArray();
            Pending.Clear();
            foreach (var action in pending)
            {
                action();
            }
        }
    }

    private sealed class ImmediateInspector : ImmediateHost
    {
    }

    [TestMethod]
    public async Task Execute_CancelledBeforeTheUIThreadPumps_DoesNotAwake()
    {
        DeferredInspector.Pending.Clear();

        var target = new Target { Value = 0d };
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, 100d);
        var effect = new TransitionEffectCore { Duration = TimeSpan.Zero };

        int awakeCount = 0;
        effect.Awaked += (_, _) => awakeCount++;

        var scheduler = TransitionSchedulerCore<DeferredInspector, TestInterpreter, NonPriority>.FindOrCreate(target, CanMutualTask: false);
        using var cts = new CancellationTokenSource();

        var running = scheduler.Execute(new TestInterpolator(), state, effect, cts);

        // The Awake went into the queue rather than running inline. That queue is the window the guard has to
        // cover: cancelling after it is queued but before it is pumped is exactly what a reset does.
        Assert.IsTrue(DeferredInspector.Pending.Count >= 1, "the Awake must be queued, not run inline");

        cts.Cancel();
        DeferredInspector.Pump();

        await running;

        // A cancelled animation does not awake — an Awake that reinitialises state would undo the reset that the
        // Exit already published.
        Assert.AreEqual(0, awakeCount);
    }

    [TestMethod]
    public void Exit_RunsCancellationCallbacksOutsideTheTargetLock()
    {
        var target = new Target();
        var scheduler = (TransitionSchedulerCore)TransitionSchedulerCore<ImmediateInspector, TestInterpreter, NonPriority>
            .FindOrCreate(target, CanMutualTask: false);
        TransitionCore.AddNoMutual(target, [scheduler]);

        var run = new TransitionRun(new TimeSourceCore());
        scheduler.Track(run);

        bool? lockWasFree = null;
        run.Cts.Token.Register(() =>
        {
            // Cancellation callbacks run synchronously on the thread that calls Exit. If Exit still held the target
            // lock here, a callback that re-enters Exit — or CoreExecute — for the same target would deadlock,
            // since SemaphoreSlim is not reentrant; the wait below would time out instead.
            lockWasFree = TransitionSchedulerCore.GetTargetLock(target).Wait(TimeSpan.FromSeconds(5));
        });

        TransitionCore.Exit(target, IncludeMutual: false, IncludeNoMutual: true);

        Assert.IsTrue(lockWasFree, "the target lock must be released before the cancellation callbacks run");
    }
}
