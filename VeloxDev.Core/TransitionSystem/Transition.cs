using System.Linq.Expressions;
using System.Runtime.CompilerServices;
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

        public static T Effect<T, T1>(this T snapshot, T1 effect)
            where T : StateSnapshotCore, new()
            where T1 : ITransitionEffectCore
        {
            snapshot.CoreEffect<T, T1>(effect);
            return snapshot;
        }
        public static T Effect<T, T1>(this T snapshot, Action<T1> effectSetter)
            where T : StateSnapshotCore, new()
            where T1 : ITransitionEffectCore, new()
        {
            snapshot.CoreEffect<T, T1>(effectSetter);
            return snapshot;
        }

        public static T Property<T, T1, T2>(this T snapshot, Expression<Func<T1, T2>> expression, T2 value)
            where T : StateSnapshotCore
            where T1 : class
            where T2 : IInterpolable
        {
            snapshot.CoreProperty<T1, T2>(expression, value);
            return snapshot;
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
        public static ConditionalWeakTable<object, List<ITransitionSchedulerCore>> NoSerialSchedulers { get; internal set; } = new();
        
        public static void Exit<T>(T target, bool IncludeUnMutual = false)
            where T : class
        {
            if (TransitionSchedulerCore.TryGetScheduler(target, out var scheduler))
            {
                scheduler?.Exit();
            }
            if (IncludeUnMutual)
            {
                if (NoSerialSchedulers.TryGetValue(target, out var noSTASchedulers))
                {
                    foreach (var noSTAScheduler in noSTASchedulers)
                    {
                        noSTAScheduler.Exit();
                    }
                }
            }
        }

        internal static void AddNoMutual(object target, IEnumerable<ITransitionSchedulerCore> schedulerCores)
        {
            if (NoSerialSchedulers.TryGetValue(target, out var noSTASchedulers))
            {
                foreach (var scheduler in schedulerCores)
                {
                    noSTASchedulers.Add(scheduler);
                }
            }
            else
            {
                NoSerialSchedulers.Add(target, [.. schedulerCores]);
            }
        }
        internal static void RemoveNoMutual(object target, IEnumerable<ITransitionSchedulerCore> schedulerCores)
        {
            if (NoSerialSchedulers.TryGetValue(target, out var noSTASchedulers))
            {
                foreach (var scheduler in schedulerCores)
                {
                    noSTASchedulers.Remove(scheduler);
                }
                if (noSTASchedulers.Count == 0)
                {
                    NoSerialSchedulers.Remove(target);
                }
            }
        }
    }
}
