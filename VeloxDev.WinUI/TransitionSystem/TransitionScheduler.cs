using System;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.TransitionSystem
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

    public class TransitionScheduler<TTarget> : TransitionSchedulerCore<TTarget, InterpolatorOutput, DispatcherPriority, UIThreadInspector, TransitionInterpreter>
        where TTarget : class
    {
        public WeakReference<TTarget>? TargetRef
        {
            get => targetref;
            internal set => targetref = value;
        }
    }
}
