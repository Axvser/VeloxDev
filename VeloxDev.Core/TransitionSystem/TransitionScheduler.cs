using System.Runtime.CompilerServices;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public class TransitionSchedulerCore<
        TUIThreadInspectorCore,
        TTransitionInterpreterCore,
        TPriorityCore> : TransitionSchedulerCore, ITransitionScheduler<TPriorityCore>
        where TUIThreadInspectorCore : IUIThreadInspector<TPriorityCore>, new()
        where TTransitionInterpreterCore : class, ITransitionInterpreter<TPriorityCore>, new()
    {
        protected TTransitionInterpreterCore? interpreter = null;
        protected static readonly TUIThreadInspectorCore uIThreadInspector = new();

        public override async Task Execute(
            IFrameInterpolatorCore interpolator,
            IFrameState state,
            ITransitionEffectCore effect,
            CancellationTokenSource? externCts = default)
        {
            if (interpolator is not IFrameInterpolator<TPriorityCore> cvt_interpolator) return;
            if (effect is not ITransitionEffect<TPriorityCore> cvt_effect) return;
            await Execute(cvt_interpolator, state, cvt_effect, externCts);
        }

        public virtual async Task Execute(
            IFrameInterpolator<TPriorityCore> interpolator,
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
            cts = newCts;
            interpreter = newInterpreter;
            var isUIThread = uIThreadInspector.IsUIThread();
            uIThreadInspector.ProtectedInvoke(isUIThread, () =>
            {
                effect.InvokeAwake(target, newInterpreter.Args);
            }, effect.Priority);

            var frames = interpolator.Interpolate(target, state, effect, isUIThread, uIThreadInspector);
            if (newCts.IsCancellationRequested || newInterpreter.Args.Handled) return;
            await newInterpreter.Execute(target, frames, effect, isUIThread, newCts);
        }

        public override void Exit()
        {
            Interlocked.Exchange(ref interpreter, null);
            var oldCts = Interlocked.Exchange(ref cts, null);
            oldCts?.Cancel();
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
        protected TTransitionInterpreterCore? interpreter = null;
        protected static readonly TUIThreadInspectorCore uIThreadInspector = new();

        public override async Task Execute(
            IFrameInterpolatorCore interpolator,
            IFrameState state,
            ITransitionEffectCore effect,
            CancellationTokenSource? externCts = default)
        {
            if (interpolator is not IFrameInterpolator cvt_interpolator) return;
            await Execute(cvt_interpolator, state, effect, externCts);
        }

        public virtual async Task Execute(
            IFrameInterpolator interpolator,
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
            cts = newCts;
            interpreter = newInterpreter;
            var isUIThread = uIThreadInspector.IsUIThread();
            uIThreadInspector.ProtectedInvoke(isUIThread, () =>
            {
                effect.InvokeAwake(target, newInterpreter.Args);
            });
            var frames = interpolator.Interpolate(target, state, effect, isUIThread, uIThreadInspector);
            if (newCts.IsCancellationRequested || newInterpreter.Args.Handled) return;
            await newInterpreter.Execute(target, frames, effect, isUIThread, newCts);
        }

        public override void Exit()
        {
            Interlocked.Exchange(ref interpreter, null);
            var oldCts = Interlocked.Exchange(ref cts, null);
            oldCts?.Cancel();
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

        internal CancellationTokenSource? cts = null;
        internal WeakReference<object>? targetref = null;
        public virtual WeakReference<object>? TargetRef
        {
            get => targetref;
            protected set => targetref = value;
        }

        public abstract Task Execute(
            IFrameInterpolatorCore interpolator,
            IFrameState state,
            ITransitionEffectCore effect,
            CancellationTokenSource? externCts = default);
        public abstract void Exit();
    }
}
