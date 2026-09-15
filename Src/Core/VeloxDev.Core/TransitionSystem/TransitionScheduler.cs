using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem.Abstractions;

public class TransitionSchedulerCore<
    THost,
    TTransitionInterpreterCore,
    TPriorityCore> : TransitionSchedulerCore, ITransitionScheduler<TPriorityCore>
    where THost : ITransitionHost<TPriorityCore>, new()
    where TTransitionInterpreterCore : class, ITransitionInterpreter<TPriorityCore>, new()
{
    protected static readonly THost host = new();

    public override async Task Execute(
        InterpolatorCore producer,
        IFrameState state,
        ITransitionEffectCore effect,
        CancellationTokenSource? externCts = default)
    {
        if (effect is not ITransitionEffect<TPriorityCore> cvt_effect) return;
        await Execute(producer, state, cvt_effect, externCts);
    }

    public virtual async Task Execute(
        InterpolatorCore producer,
        IFrameState state,
        ITransitionEffect<TPriorityCore> effect,
        CancellationTokenSource? externCts = default)
    {
        if (targetref is null || !targetref.TryGetTarget(out var target))
        {
            targetref = null;
            return;
        }
        var newCts = externCts ?? new CancellationTokenSource();
        TTransitionInterpreterCore newInterpreter = new();
        var generation = Generation;
        await _gate.WaitAsync();
        try
        {
            // Exit() ran while this animation was queued: it was cancelled before it ever started.
            if (generation != Generation) return;

            var diagnostics = new TransitionDiagnostics(effect, target, newInterpreter.Args);

            // Awaited, not fired and forgotten. Awake can veto the animation through Args.Handled and may put the
            // target into the state the animation is meant to start from, so Prepare must not read the target until
            // it has run. Waiting is safe because PostAsync only waits when the action was accepted.
            bool awoken;
            try
            {
                awoken = await host.PostAsync(target, () =>
                {
                    // Re-checked inside the action, not only before queueing it: the dispatch is fire-and-forget, so
                    // this runs on the UI thread whenever the message is pumped — which can be after an Exit has
                    // already cancelled the animation and published its reset.
                    if (newCts.IsCancellationRequested) return;
                    effect.InvokeAwake(target, newInterpreter.Args);
                }, effect.Priority);
            }
            catch (Exception exception)
            {
                // 宿主回调抛出的异常到此为止：动画结束，宿主进程不受影响。
                diagnostics.Error("Awake", exception);
                return;
            }

            // The host's queue is gone: nothing would be dispatched, frames included, so give up rather than start an
            // animation that cannot draw. Leaving here still releases the gate through the finally below.
            if (!awoken)
            {
                diagnostics.Warn("Dropped", "the host's dispatch queue refused the animation's Awake.");
                return;
            }

            SamplerSet<TPriorityCore> frameSet;
            try
            {
                frameSet = producer.Prepare(target, state, effect, host);
            }
            catch (Exception exception)
            {
                // 一个准备不出来的属性不该带走整趟动画，更不该带走宿主进程。
                diagnostics.Error("Prepare", exception);
                return;
            }

            // The run reaches the interpreter through the sampler set, the same way the token source does. It is
            // registered under the very token source this call was handed, so a caller that registered none — a
            // test driving the scheduler directly — keeps the frame set's own run, which nothing controls, rather
            // than getting a null one.
            if (_activeRuns.TryGetValue(newCts, out var run))
            {
                frameSet.SetRun(run);
            }

            if (newCts.IsCancellationRequested || newInterpreter.Args.Handled) return;
            await newInterpreter.Execute(target, frameSet, effect, newCts);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override void Exit()
    {
        CancelDrained(DrainActive());
    }

    public static ITransitionScheduler<TPriorityCore> FindOrCreate<T>(T source, bool CanMutualTask = true) where T : class
    {
        if (CanMutualTask)
        {
            // GetValue installs atomically. A TryGetValue-then-Add pair races: two concurrent animations can both
            // miss and the loser's Add throws.
            var scheduler = MutualSchedulers.GetValue(source, static key => new TransitionSchedulerCore<
                THost,
                TTransitionInterpreterCore,
                TPriorityCore>()
            {
                TargetRef = new WeakReference<object>(key)
            });
            return scheduler as TransitionSchedulerCore<
                THost,
                TTransitionInterpreterCore,
                TPriorityCore> ?? throw new ArgumentException($"The interpolator in the dictionary failed to be converted to the specified type ⌈ TransitionScheduler<{nameof(T)}> ⌋.");
        }
        else
        {
            return new TransitionSchedulerCore<
                   THost,
                   TTransitionInterpreterCore,
                   TPriorityCore>()
            {
                TargetRef = new WeakReference<object>(source)
            };
        }
    }
}

public abstract class TransitionSchedulerCore : ITransitionSchedulerCore
{
    private static readonly ConditionalWeakTable<object, SemaphoreSlim> TargetLocks = new();

    /// <summary>
    /// Serializes the control plane of one animation target: entering (creating the token and registering the
    /// animation) against leaving (cancelling everything alive on it). Without a shared lock an Exit can land
    /// between "the token exists" and "the animation is registered" and miss the one that is just starting, which
    /// then runs unstoppably.
    /// </summary>
    /// <remarks>
    /// Only ever held across synchronous bookkeeping — never across the animation body — so it stays cheap and
    /// cannot deadlock against the UI thread that the frames are marshalled to, and so non-mutual animations on the
    /// same target still run concurrently.
    /// </remarks>
    internal static SemaphoreSlim GetTargetLock(object target)
        => TargetLocks.GetValue(target, static _ => new SemaphoreSlim(1, 1));

    public static ConditionalWeakTable<object, ITransitionSchedulerCore> MutualSchedulers { get; protected set; } = new();

    /// <summary>
    /// Non-mutual schedulers per target. The value is a concurrent set: animations register and unregister
    /// themselves from several threads at once (a background <c>Task.Run</c>, a UI-thread click), and a plain
    /// <see cref="List{T}"/> mutated without synchronization loses entries — which makes
    /// <see cref="TransitionCore.Exit{T}"/> miss schedulers and leave animations running.
    /// </summary>
    public static ConditionalWeakTable<object, ConcurrentDictionary<ITransitionSchedulerCore, byte>> NoMutualSchedulers { get; internal set; } = new();

    public static bool TryGetMutualScheduler(object source, out ITransitionSchedulerCore? scheduler)
    {
        if (MutualSchedulers.TryGetValue(source, out scheduler)) return true;
        scheduler = null;
        return false;
    }
    public static bool RemoveMutualScheduler(object source)
    {
        if (MutualSchedulers.TryGetValue(source, out var scheduler)) scheduler.Exit();
        return MutualSchedulers.Remove(source);
    }

    public static bool TryGetNoMutualScheduler(object source, out ITransitionSchedulerCore[] schedulers)
    {
        if (NoMutualSchedulers.TryGetValue(source, out var values))
        {
            schedulers = [.. values.Keys];
            return true;
        }
        schedulers = [];
        return false;
    }
    public static bool RemoveNoMutualScheduler(object source)
    {
        if (NoMutualSchedulers.TryGetValue(source, out var values))
        {
            foreach (var value in values.Keys)
            {
                value.Exit();
            }
        }
        return NoMutualSchedulers.Remove(source);
    }

    /// <summary>
    /// The animations running on this scheduler, keyed by their own token source.
    /// </summary>
    /// <remarks>
    /// The key is the token source rather than the run because that is what the caller already hands down to
    /// <see cref="Execute"/>: the clock is then reachable from there without widening any signature, and a caller
    /// that never registered a run — a test driving the interpreter or the scheduler directly — simply gets an
    /// uncontrollable clock instead of a null one.
    /// </remarks>
    internal readonly ConcurrentDictionary<CancellationTokenSource, TransitionRun> _activeRuns = new();
    private int _generation;
    protected readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Bumped on every <see cref="Exit"/>. An animation still queued on <see cref="_gate"/> compares the generation
    /// it captured before waiting against this one and gives up if it changed — otherwise it would start running
    /// after the Exit and could no longer be stopped by it.
    /// </summary>
    internal int Generation => Volatile.Read(ref _generation);

    /// <summary>
    /// Registers an animation's token source for its whole lifetime. Tracking only the segment currently executing
    /// is not enough: between segments the animation sits in an <c>Await</c> gap with nothing running, and an
    /// <see cref="Exit"/> during that gap would cancel nothing at all while the animation went on to its next
    /// segment.
    /// </summary>
    internal void Track(TransitionRun run) => _activeRuns.TryAdd(run.Cts, run);

    /// <remarks>
    /// Unregistering and releasing are separate: a run leaves this table while its token source is still being read
    /// by whoever owns it, so the owner disposes it — see <see cref="TransitionRun.Dispose"/>.
    /// </remarks>
    internal void Untrack(TransitionRun run) => _activeRuns.TryRemove(run.Cts, out _);

    /// <summary>Appends the runs registered right now, for a control call that acts on all of them.</summary>
    internal void AddActiveTo(List<TransitionRun> runs)
    {
        foreach (var pair in _activeRuns) runs.Add(pair.Value);
    }

    /// <summary>The first run registered right now, without building a list — for the queries that want one run.</summary>
    internal bool TryGetFirstActive(out TransitionRun? run)
    {
        foreach (var pair in _activeRuns)
        {
            run = pair.Value;
            return true;
        }

        run = null;
        return false;
    }

    /// <summary>
    /// Bumps the generation and takes every registered token out of the active set, returning them to be cancelled
    /// by <see cref="CancelDrained"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately split from the cancellation itself. The bookkeeping is what has to be atomic against an
    /// animation entering, so a caller holding the target lock runs this part inside it; the cancellation runs
    /// after the lock is released, because <c>CancellationTokenSource.Cancel()</c> executes the registered
    /// callbacks synchronously on the calling thread — holding the lock across them would block the dispatcher on a
    /// UI-thread <c>Exit</c>, and a callback that re-enters <see cref="TransitionCore.Exit{T}"/> or
    /// <c>CoreExecute</c> for the same target would deadlock, since <see cref="SemaphoreSlim"/> is not reentrant.
    /// </remarks>
    internal List<TransitionRun> DrainActive()
    {
        Interlocked.Increment(ref _generation);

        List<TransitionRun> drained = [];
        foreach (var source in _activeRuns.Keys)
        {
            if (_activeRuns.TryRemove(source, out var run))
            {
                drained.Add(run);
            }
        }
        return drained;
    }

    /// <summary>
    /// Cancels the tokens taken by <see cref="DrainActive"/>. Call this outside any lock: the callbacks run
    /// synchronously here.
    /// </summary>
    internal static void CancelDrained(List<TransitionRun> drained)
    {
        foreach (var run in drained)
        {
            try
            {
                run.Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // an animation or an adapter's interpreter disposed the token source — nothing left to cancel
            }

            // A paused animation is parked on its timeline's gate, which knows nothing about the token. Without this
            // it would sit there until something else resumed it, and Exit would return while the loop it was meant
            // to stop was still waiting. Waking it lets the loop see the token and stop; a run sharing the timeline
            // with a live animation simply wakes, re-checks, and parks again.
            run.Timeline.Wake();
        }
    }

    /// <summary>
    /// The runs currently registered on <paramref name="target"/>, selected the way <c>Exit</c> selects what to
    /// stop.
    /// </summary>
    internal static List<TransitionRun> CollectRuns(object target, bool includeMutual, bool includeNoMutual)
    {
        List<TransitionRun> runs = [];
        if (includeMutual && MutualSchedulers.TryGetValue(target, out var mutual))
        {
            ((TransitionSchedulerCore)mutual).AddActiveTo(runs);
        }
        if (includeNoMutual && NoMutualSchedulers.TryGetValue(target, out var nomutual))
        {
            foreach (var scheduler in nomutual.Keys)
            {
                ((TransitionSchedulerCore)scheduler).AddActiveTo(runs);
            }
        }
        return runs;
    }

    /// <summary>
    /// The first run on <paramref name="target"/>, without building a list.
    /// </summary>
    /// <remarks>
    /// The queries that only ever look at one run go through here, so asking a target with nothing running — which is
    /// what a per-frame <c>Transition.Position(target)</c> does most of the time — allocates nothing at all.
    /// </remarks>
    internal static bool TryGetFirstRun(object target, bool includeMutual, bool includeNoMutual, out TransitionRun? run)
    {
        if (includeMutual && MutualSchedulers.TryGetValue(target, out var mutual)
            && ((TransitionSchedulerCore)mutual).TryGetFirstActive(out run))
        {
            return true;
        }

        if (includeNoMutual && NoMutualSchedulers.TryGetValue(target, out var nomutual))
        {
            foreach (var scheduler in nomutual.Keys)
            {
                if (((TransitionSchedulerCore)scheduler).TryGetFirstActive(out run)) return true;
            }
        }

        run = null;
        return false;
    }

    internal WeakReference<object>? targetref = null;
    public virtual WeakReference<object>? TargetRef
    {
        get => targetref;
        protected set => targetref = value;
    }

    public abstract Task Execute(
        InterpolatorCore producer,
        IFrameState state,
        ITransitionEffectCore effect,
        CancellationTokenSource? externCts = default);
    public abstract void Exit();
}
