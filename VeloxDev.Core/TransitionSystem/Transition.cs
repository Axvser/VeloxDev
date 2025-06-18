using System.Runtime.CompilerServices;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public class TransitionCore<TStateSnapshotCore> : TransitionCore
        where TStateSnapshotCore : new()
    {
        public static TStateSnapshotCore Create<T>()
            where T : class
        {
            var value = new TStateSnapshotCore();
            if (value is StateSnapshotCore<T> snapshot)
            {
                snapshot.AsRoot();
            }
            return value;
        }
        public static TStateSnapshotCore Create<T>(T target)
            where T : class
        {
            var value = new TStateSnapshotCore();
            if (value is StateSnapshotCore<T> snapshot)
            {
                snapshot.AsRoot();
                snapshot.SetTarget(target);
            }
            return value;
        }

        public static void Excute<T>(T target, IEnumerable<TStateSnapshotCore> values, bool CanSTAThread = true)
            where T : class
        {
            foreach (var snapshot in values.OfType<StateSnapshotCore<T>>())
            {
                snapshot.Excute(target, CanSTAThread);
            }
        }
        public static void Excute<T>(IEnumerable<TStateSnapshotCore> values, bool CanSTAThread = true)
            where T : class
        {
            foreach (var snapshot in values.OfType<StateSnapshotCore<T>>())
            {
                snapshot.Excute(CanSTAThread);
            }
        }
    }

    public abstract class TransitionCore
    {
        public static ConditionalWeakTable<object, List<ITransitionSchedulerCore>> NoSTASchedulers { get; internal set; } = new();

        public static void Exit<T>(IEnumerable<T> targets)
            where T : class
        {
            foreach (var target in targets)
            {
                if (TransitionSchedulerCore.TryGetScheduler(target, out var scheduler))
                {
                    scheduler?.Exit();
                }
                if (NoSTASchedulers.TryGetValue(target, out var noSTASchedulers))
                {
                    foreach (var noSTAScheduler in noSTASchedulers)
                    {
                        noSTAScheduler.Exit();
                    }
                }
            }
        }

        internal static void AddNoSTA(object target, IEnumerable<ITransitionSchedulerCore> schedulerCores)
        {
            if (NoSTASchedulers.TryGetValue(target, out var noSTASchedulers))
            {
                foreach (var scheduler in schedulerCores)
                {
                    noSTASchedulers.Add(scheduler);
                }
            }
            else
            {
                NoSTASchedulers.Add(target, [.. schedulerCores]);
            }
        }
        internal static void RemoveNoSTA(object target, IEnumerable<ITransitionSchedulerCore> schedulerCores)
        {
            if (NoSTASchedulers.TryGetValue(target, out var noSTASchedulers))
            {
                foreach (var scheduler in schedulerCores)
                {
                    noSTASchedulers.Remove(scheduler);
                }
                if (noSTASchedulers.Count == 0)
                {
                    NoSTASchedulers.Remove(target);
                }
            }
        }
    }
}
