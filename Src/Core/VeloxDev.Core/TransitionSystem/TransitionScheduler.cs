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
        await _gate.WaitAsync();
        try
        {
            cts = newCts;
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
            if (TryGetMutualScheduler(source, out var item))
            {
                return item as TransitionSchedulerCore<
                    TUIThreadInspectorCore,
                    TTransitionInterpreterCore,
                    TPriorityCore> ?? throw new ArgumentException($"The interpolator in the dictionary failed to be converted to the specified type ⌈ TransitionScheduler<{nameof(T)}> ⌋.");
            }
            else
            {
                var scheduler = new TransitionSchedulerCore<
                    TUIThreadInspectorCore,
                    TTransitionInterpreterCore,
                    TPriorityCore>()
                {
                    TargetRef = new WeakReference<object>(source)
                };
                MutualSchedulers.Add(source, scheduler);
                return scheduler;
            }
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
        await _gate.WaitAsync();
        try
        {
            cts = newCts;
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
            if (TryGetMutualScheduler(source, out var item))
            {
                return item as TransitionSchedulerCore<
                    TUIThreadInspectorCore,
                    TTransitionInterpreterCore> ?? throw new ArgumentException($"The interpolator in the dictionary failed to be converted to the specified type ⌈ TransitionScheduler<{nameof(T)}> ⌋.");
            }
            else
            {
                var scheduler = new TransitionSchedulerCore<
                    TUIThreadInspectorCore,
                    TTransitionInterpreterCore>()
                {
                    TargetRef = new WeakReference<object>(source)
                };
                MutualSchedulers.Add(source, scheduler);
                return scheduler;
            }
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
    public static ConditionalWeakTable<object, ITransitionSchedulerCore> MutualSchedulers { get; protected set; } = new();
    public static ConditionalWeakTable<object, List<ITransitionSchedulerCore>> NoMutualSchedulers { get; internal set; } = new();

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
            schedulers = [.. values];
            return true;
        }
        schedulers = [];
        return false;
    }
    public static bool RemoveNoMutualScheduler(object source)
    {
        if (NoMutualSchedulers.TryGetValue(source, out var values))
        {
            foreach (var value in values)
            {
                value.Exit();
            }
        }
        return NoMutualSchedulers.Remove(source);
    }

    private CancellationTokenSource? _currentCts;
    protected readonly SemaphoreSlim _gate = new(1, 1);

    internal CancellationTokenSource? cts
    {
        get => Volatile.Read(ref _currentCts);
        set => Interlocked.Exchange(ref _currentCts, value);
    }

    protected void CancelCurrent()
    {
        Interlocked.Exchange(ref _currentCts, null)?.Cancel();
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
