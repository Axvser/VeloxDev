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
        protected TUIThreadInspectorCore uIThreadInspector = new();

        public virtual async Task Execute(IFrameInterpolator<TPriorityCore> interpolator, IFrameState state, ITransitionEffect<TPriorityCore> effect)
        {
            if (targetref is null || !targetref.TryGetTarget(out var target))
            {
                targetref = null;
                return;
            }
            var newCts = new CancellationTokenSource();
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

        public static ITransitionScheduler<TPriorityCore> FindOrCreate<T>(T source, bool CanSTAThread = true) where T : class
        {
            if (TryGetScheduler(source, out var item))
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
                if (CanSTAThread)
                {
                    Schedulers.Add(source, scheduler);
                }
                return scheduler;
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
        protected TUIThreadInspectorCore uIThreadInspector = new();

        public virtual async Task Execute(IFrameInterpolator interpolator, IFrameState state, ITransitionEffectCore effect)
        {
            if (targetref is null || !targetref.TryGetTarget(out var target))
            {
                targetref = null;
                return;
            }
            var newCts = new CancellationTokenSource();
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

        public static ITransitionScheduler FindOrCreate<T>(T source, bool CanSTAThread = true) where T : class
        {
            if (TryGetScheduler(source, out var item))
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
                if (CanSTAThread)
                {
                    Schedulers.Add(source, scheduler);
                }
                return scheduler;
            }
        }
    }

    public abstract class TransitionSchedulerCore : ITransitionSchedulerCore
    {
        public static ConditionalWeakTable<object, ITransitionSchedulerCore> Schedulers { get; protected set; } = new();

        public static bool TryGetScheduler(object source, out ITransitionSchedulerCore? scheduler)
        {
            if (Schedulers.TryGetValue(source, out scheduler)) return true;
            scheduler = null;
            return false;
        }
        public static bool RemoveScheduler(object source)
        {
            return Schedulers.Remove(source);
        }

        protected CancellationTokenSource? cts = null;
        protected WeakReference<object>? targetref = null;
        public virtual WeakReference<object>? TargetRef
        {
            get => targetref;
            protected set => targetref = value;
        }

        public abstract void Exit();
    }
}
