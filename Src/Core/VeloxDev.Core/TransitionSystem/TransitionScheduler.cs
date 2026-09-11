using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace VeloxDev.TransitionSystem.Abstractions;

public class TransitionSchedulerCore<
    TUIThreadInspectorCore,
    TTransitionInterpreterCore,
    TPriorityCore> : TransitionSchedulerCore, ITransitionScheduler<TPriorityCore>
    where TUIThreadInspectorCore : IUIThreadInspector<TPriorityCore>, new()
    where TTransitionInterpreterCore : class, ITransitionInterpreter<TPriorityCore>, new()
{
    protected static readonly TUIThreadInspectorCore uIThreadInspector = new();

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

            uIThreadInspector.ProtectedInvoke(target, () =>
            {
                // Re-checked inside the action, not only before queueing it: on WPF, Avalonia, Jalium, WinForms and
                // WinUI ProtectedInvoke is fire-and-forget (InvokeAsync/BeginInvoke/TryEnqueue), so this runs on the
                // UI thread whenever the message is pumped — which can be after an Exit has already cancelled the
                // animation and published its reset. A cancelled animation does not awake; an Awake that
                // reinitialises state would otherwise undo that reset.
                if (newCts.IsCancellationRequested) return;
                effect.InvokeAwake(target, newInterpreter.Args);
            }, effect.Priority);

            var frameSet = producer.Prepare<TPriorityCore>(target, state, effect, uIThreadInspector);
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
                TUIThreadInspectorCore,
                TTransitionInterpreterCore,
                TPriorityCore>()
            {
                TargetRef = new WeakReference<object>(key)
            });
            return scheduler as TransitionSchedulerCore<
                TUIThreadInspectorCore,
                TTransitionInterpreterCore,
                TPriorityCore> ?? throw new ArgumentException($"The interpolator in the dictionary failed to be converted to the specified type ⌈ TransitionScheduler<{nameof(T)}> ⌋.");
        }
        else
        {
            return new TransitionSchedulerCore<
                   TUIThreadInspectorCore,
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

    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _activeCts = new();
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
    internal void Track(CancellationTokenSource source) => _activeCts.TryAdd(source, 0);

    internal void Untrack(CancellationTokenSource source) => _activeCts.TryRemove(source, out _);

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
    internal List<CancellationTokenSource> DrainActive()
    {
        Interlocked.Increment(ref _generation);

        List<CancellationTokenSource> drained = [];
        foreach (var source in _activeCts.Keys)
        {
            if (_activeCts.TryRemove(source, out _))
            {
                drained.Add(source);
            }
        }
        return drained;
    }

    /// <summary>
    /// Cancels the tokens taken by <see cref="DrainActive"/>. Call this outside any lock: the callbacks run
    /// synchronously here.
    /// </summary>
    internal static void CancelDrained(List<CancellationTokenSource> drained)
    {
        foreach (var source in drained)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // an animation or an adapter's interpreter disposed the token source — nothing left to cancel
            }
        }
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
