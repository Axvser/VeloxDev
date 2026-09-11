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
                effect.InvokeAwake(target, newInterpreter.Args);
            }, effect.Priority);

            var frameSet = producer.Prepare(target, state, effect, uIThreadInspector);
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
        CancelCurrent();
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

public class TransitionSchedulerCore<
    TUIThreadInspectorCore,
    TTransitionInterpreterCore> : TransitionSchedulerCore, ITransitionScheduler
    where TUIThreadInspectorCore : IUIThreadInspector, new()
    where TTransitionInterpreterCore : class, ITransitionInterpreter, new()
{
    protected static readonly TUIThreadInspectorCore uIThreadInspector = new();

    public override async Task Execute(
        InterpolatorCore producer,
        IFrameState state,
        ITransitionEffectCore effect,
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
                effect.InvokeAwake(target, newInterpreter.Args);
            });
            var frameSet = producer.Prepare(target, state, effect, uIThreadInspector);
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
        CancelCurrent();
    }

    public static ITransitionScheduler FindOrCreate<T>(T source, bool CanMutualTask = true) where T : class
    {
        if (CanMutualTask)
        {
            // GetValue installs atomically — see the generic overload above.
            var scheduler = MutualSchedulers.GetValue(source, static key => new TransitionSchedulerCore<
                TUIThreadInspectorCore,
                TTransitionInterpreterCore>()
            {
                TargetRef = new WeakReference<object>(key)
            });
            return scheduler as TransitionSchedulerCore<
                TUIThreadInspectorCore,
                TTransitionInterpreterCore> ?? throw new ArgumentException($"The interpolator in the dictionary failed to be converted to the specified type ⌈ TransitionScheduler<{nameof(T)}> ⌋.");
        }
        else
        {
            return new TransitionSchedulerCore<
                   TUIThreadInspectorCore,
                   TTransitionInterpreterCore>()
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

    protected void CancelCurrent()
    {
        Interlocked.Increment(ref _generation);

        foreach (var source in _activeCts.Keys)
        {
            _activeCts.TryRemove(source, out _);
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // the animation already finished and released its source — nothing left to cancel
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
