using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public static class TransitionCoreEx
    {
        public static T Await<T>(this T snapshot, TimeSpan timeSpan)
            where T : StateSnapshotCore, new()
        {
            snapshot.CoreAwait<T>(timeSpan);
            return snapshot;
        }

        public static T Then<T>(this T snapshot)
            where T : StateSnapshotCore, new()
        {
            return snapshot.CoreThen<T>();
        }
        public static T AwaitThen<T>(this T snapshot, TimeSpan timeSpan)
            where T : StateSnapshotCore, new()
        {
            return snapshot.CoreAwaitThen<T>(timeSpan);
        }

        public static void Execute<T>(this T snapshot, object target, bool CanMutualTask = true)
            where T : StateSnapshotCore
        {
            snapshot.CoreExecute(target, CanMutualTask);
        }
        public static void Execute<T>(this T snapshot, bool CanMutualTask = true)
            where T : StateSnapshotCore
        {
            snapshot.CoreExecute(CanMutualTask);
        }
    }

    public class TransitionCore<TTarget, TStateSnapshotCore> : TransitionCore
        where TTarget : class
        where TStateSnapshotCore : new()
    {
        public static TStateSnapshotCore Create()
        {
            var value = new TStateSnapshotCore();
            if (value is StateSnapshotCore<TTarget> snapshot)
            {
                snapshot.AsRoot();
            }
            return value;
        }

        public static void Execute<T>(T target, StateSnapshotCore value, bool CanMutualTask = true)
            where T : class, TTarget
        {
            value.CoreExecute(target, CanMutualTask);
        }
        public static void Execute(StateSnapshotCore values, bool CanMutualTask = true)
        {
            values.CoreExecute(CanMutualTask);
        }
        public static void Execute<T>(T target, IEnumerable<StateSnapshotCore> values, bool CanMutualTask = false)
            where T : class, TTarget
        {
            foreach (var snapshot in values)
            {
                snapshot.CoreExecute(target, CanMutualTask);
            }
        }
        public static void Execute(IEnumerable<StateSnapshotCore> values, bool CanMutualTask = false)
        {
            foreach (var snapshot in values)
            {
                snapshot.CoreExecute(CanMutualTask);
            }
        }
    }

    public abstract class TransitionCore
    {
        public static void Exit<T>(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
            where T : class
        {
            List<ITransitionSchedulerCore> schedulers = [];
            if (IncludeMutual && TransitionSchedulerCore.TryGetMutualScheduler(target, out var mutualScheduler))
            {
                schedulers.Add(mutualScheduler!);
            }
            if (IncludeNoMutual && TransitionSchedulerCore.TryGetNoMutualScheduler(target, out var nomutualSchedulers))
            {
                schedulers.AddRange(nomutualSchedulers);
            }
            foreach (var scheduler in schedulers)
            {
                scheduler.Exit();
            }
        }

        internal static void AddNoMutual(object target, IEnumerable<ITransitionSchedulerCore> schedulerCores)
        {
            if (TransitionSchedulerCore.NoMutualSchedulers.TryGetValue(target, out var noSTASchedulers))
            {
                foreach (var scheduler in schedulerCores)
                {
                    noSTASchedulers.Add(scheduler);
                }
            }
            else
            {
                TransitionSchedulerCore.NoMutualSchedulers.Add(target, [.. schedulerCores]);
            }
        }
        internal static void RemoveNoMutual(object target, IEnumerable<ITransitionSchedulerCore> schedulerCores)
        {
            if (TransitionSchedulerCore.NoMutualSchedulers.TryGetValue(target, out var noSTASchedulers))
            {
                foreach (var scheduler in schedulerCores)
                {
                    noSTASchedulers.Remove(scheduler);
                }
                if (noSTASchedulers.Count == 0)
                {
                    TransitionSchedulerCore.NoMutualSchedulers.Remove(target);
                }
            }
        }
    }
}
