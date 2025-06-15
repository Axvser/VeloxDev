using System.Runtime.CompilerServices;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public class TransitionSchedulerCore<
        TTarget,
        TUIThreadInspectorCore,
        TTransitionInterpreterCore,
        TPriorityCore> : TransitionSchedulerCore, ITransitionScheduler<TPriorityCore>
        where TTarget : class
        where TUIThreadInspectorCore : IUIThreadInspector, new()
        where TTransitionInterpreterCore : class, ITransitionInterpreter<TPriorityCore>, new()
    {
        protected WeakReference<TTarget>? targetref = null;
        protected CancellationTokenSource? cts = null;
        protected TTransitionInterpreterCore? interpreter = null;
        protected TUIThreadInspectorCore uIThreadInspector = new();

        public virtual WeakReference<TTarget>? TargetRef
        {
            get => targetref;
            protected set => targetref = value;
        }

        public virtual async void Execute(IFrameInterpolator<TPriorityCore> interpolator, IFrameState state, ITransitionEffect<TPriorityCore> effect)
        {
            Exit();
            if (targetref is null || !targetref.TryGetTarget(out var target))
            {
                targetref = null;
                return;
            }
            var newCts = new CancellationTokenSource();
            TTransitionInterpreterCore newInterpreter = new();
            cts = newCts;
            interpreter = newInterpreter;
            effect.InvokeAwake(target, newInterpreter.Args);
            var frames = interpolator.Interpolate(target, state, effect);
            await newInterpreter.Execute(target, frames, effect, uIThreadInspector.IsUIThread(), newCts);
        }

        public override void Exit()
        {
            Interlocked.Exchange(ref interpreter, null);
            var oldCts = Interlocked.Exchange(ref cts, null);
            oldCts?.Cancel();
        }

        public static TransitionSchedulerCore<
            TTarget,
            TUIThreadInspectorCore,
            TTransitionInterpreterCore,
            TPriorityCore> FindOrCreate(TTarget source, bool CanSTAThread = true)
        {
            if (TryGetScheduler(source, out var item))
            {
                return item as TransitionSchedulerCore<
                    TTarget,
                    TUIThreadInspectorCore,
                    TTransitionInterpreterCore,
                    TPriorityCore> ?? throw new ArgumentException($"The interpolator in the dictionary failed to be converted to the specified type ⌈ TransitionScheduler<{nameof(TTarget)}> ⌋.");
            }
            else
            {
                var scheduler = new TransitionSchedulerCore<
                    TTarget,
                    TUIThreadInspectorCore,
                    TTransitionInterpreterCore,
                    TPriorityCore>()
                {
                    TargetRef = new WeakReference<TTarget>(source)
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
        TTarget,
        TUIThreadInspectorCore,
        TTransitionInterpreterCore> : TransitionSchedulerCore, ITransitionScheduler
        where TTarget : class
        where TUIThreadInspectorCore : IUIThreadInspector, new()
        where TTransitionInterpreterCore : class, ITransitionInterpreter, new()
    {
        protected WeakReference<TTarget>? targetref = null;
        protected CancellationTokenSource? cts = null;
        protected TTransitionInterpreterCore? interpreter = null;
        protected TUIThreadInspectorCore uIThreadInspector = new();

        public virtual WeakReference<TTarget>? TargetRef
        {
            get => targetref;
            protected set => targetref = value;
        }

        public virtual async void Execute(IFrameInterpolator interpolator, IFrameState state, ITransitionEffectCore effect)
        {
            Exit();
            if (targetref is null || !targetref.TryGetTarget(out var target))
            {
                targetref = null;
                return;
            }
            var newCts = new CancellationTokenSource();
            TTransitionInterpreterCore newInterpreter = new();
            cts = newCts;
            interpreter = newInterpreter;
            effect.InvokeAwake(target, newInterpreter.Args);
            var frames = interpolator.Interpolate(target, state, effect);
            await newInterpreter.Execute(target, frames, effect, uIThreadInspector.IsUIThread(), newCts);
        }

        public override void Exit()
        {
            Interlocked.Exchange(ref interpreter, null);
            var oldCts = Interlocked.Exchange(ref cts, null);
            oldCts?.Cancel();
        }

        public static TransitionSchedulerCore<
            TTarget,
            TUIThreadInspectorCore,
            TTransitionInterpreterCore> FindOrCreate(TTarget source, bool CanSTAThread = true)
        {
            if (TryGetScheduler(source, out var item))
            {
                return item as TransitionSchedulerCore<
                    TTarget,
                    TUIThreadInspectorCore,
                    TTransitionInterpreterCore> ?? throw new ArgumentException($"The interpolator in the dictionary failed to be converted to the specified type ⌈ TransitionScheduler<{nameof(TTarget)}> ⌋.");
            }
            else
            {
                var scheduler = new TransitionSchedulerCore<
                    TTarget,
                    TUIThreadInspectorCore,
                    TTransitionInterpreterCore>()
                {
                    TargetRef = new WeakReference<TTarget>(source)
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

        public abstract void Exit();
    }
}
