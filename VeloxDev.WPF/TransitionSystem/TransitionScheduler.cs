using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public static class TransitionScheduler
    {
        public static ConditionalWeakTable<object, ITransitionScheduler> Schedulers { get; internal set; } = new();

        public static bool TryGetScheduler(object source, out ITransitionScheduler? scheduler)
        {
            if (Schedulers.TryGetValue(source, out scheduler)) return true;
            scheduler = null;
            return false;
        }
        public static bool RemoveScheduler(object source)
        {
            return Schedulers.Remove(source);
        }
        public static TransitionScheduler<TTarget> FindOrCreate<TTarget>(TTarget source, bool CanSTAThread = true) where TTarget : class
        {
            if (TryGetScheduler(source, out var item))
            {
                return item as TransitionScheduler<TTarget> ?? throw new ArgumentException($"The interpolator in the dictionary failed to be converted to the specified type ⌈ TransitionScheduler<{nameof(TTarget)}> ⌋.");
            }
            else
            {
                var scheduler = new TransitionScheduler<TTarget>()
                {
                    targetref = new WeakReference<TTarget>(source)
                };
                if (CanSTAThread)
                {
                    Schedulers.Add(source, scheduler);
                }
                return scheduler;
            }
        }
    }

    public class TransitionScheduler<TTarget> : TransitionSchedulerBase<TTarget, InterpolatorOutput, DispatcherPriority>
        where TTarget : class
    {
        internal TransitionScheduler() { }

        internal WeakReference<TTarget>? targetref = null;
        internal CancellationTokenSource? cts = null;
        internal ITransitionInterpreter? interpreter = null;

        public override async void Execute(IFrameInterpolator<InterpolatorOutput, DispatcherPriority> interpolator, IFrameState<InterpolatorOutput, DispatcherPriority> state, ITransitionEffect<DispatcherPriority> effect)
        {
            Exit();
            if (targetref is null || !targetref.TryGetTarget(out var target))
            {
                targetref = null;
                return;
            }
            var newCts = new CancellationTokenSource();
            var newInterpreter = new TransitionInterpreter();
            cts = newCts;
            interpreter = newInterpreter;
            effect.InvokeAwake(target, newInterpreter.Args);
            var frames = interpolator.Interpolate(target, state, effect);
            await newInterpreter.Execute(target, frames, effect, Application.Current.Dispatcher.CheckAccess(), newCts);
        }

        public override void Exit()
        {
            Interlocked.Exchange(ref interpreter, null);
            var oldCts = Interlocked.Exchange(ref cts, null);
            oldCts?.Cancel();
        }
    }
}
