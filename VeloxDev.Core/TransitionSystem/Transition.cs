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

        public static T Property<T, T1>(this T snapshot, Expression<Func<T, T1>> expression, T1 value)
            where T : StateSnapshotCore
            where T1 : IInterpolable
        {
            snapshot.CoreProperty<T, T1>(expression, value);
            return snapshot;
        }

        public static T Execute<T>(this T snapshot, object target, bool CanSTAThread = true)
            where T : StateSnapshotCore
        {
            snapshot.CoreExecute(target, CanSTAThread);
            return snapshot;
        }
        public static T Execute<T>(this T snapshot, bool CanSTAThread = true)
            where T : StateSnapshotCore
        {
            snapshot.CoreExecute(CanSTAThread);
            return snapshot;
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
        public static TStateSnapshotCore Create<T>(T target)
            where T : class, TTarget
        {
            var value = new TStateSnapshotCore();
            if (value is StateSnapshotCore<TTarget> snapshot)
            {
                snapshot.AsRoot();
                snapshot.SetTarget(target);
            }
            return value;
        }

        public static void Execute<T>(T target, StateSnapshotCore value, bool CanSTAThread = true)
            where T : class, TTarget
        {
            value.CoreExecute(target, CanSTAThread);
        }
        public static void Execute(StateSnapshotCore values, bool CanSTAThread = true)
        {
            values.CoreExecute(CanSTAThread);
        }
        public static void Execute<T>(T target, IEnumerable<StateSnapshotCore> values, bool CanSTAThread = false)
            where T : class, TTarget
        {
            foreach (var snapshot in values)
            {
                snapshot.CoreExecute(target, CanSTAThread);
            }
        }
        public static void Execute(IEnumerable<StateSnapshotCore> values, bool CanSTAThread = false)
        {
            foreach (var snapshot in values)
            {
                snapshot.CoreExecute(CanSTAThread);
            }
        }
    }

    public abstract class TransitionCore
    {
        public static ConditionalWeakTable<object, List<ITransitionSchedulerCore>> NoSTASchedulers { get; internal set; } = new();

        public static void Exit<T>(T target, bool IncludeUnSTA = false)
            where T : class
        {
            if (TransitionSchedulerCore.TryGetScheduler(target, out var scheduler))
            {
                scheduler?.Exit();
            }
            if (IncludeUnSTA)
            {
                if (NoSTASchedulers.TryGetValue(target, out var noSTASchedulers))
                {
                    foreach (var noSTAScheduler in noSTASchedulers)
                    {
                        noSTAScheduler.Exit();
                    }
                }
            }
        }
        public static void Exit<T>(IEnumerable<T> targets, bool IncludeUnSTA = false)
            where T : class
        {
            foreach (var target in targets)
            {
                if (TransitionSchedulerCore.TryGetScheduler(target, out var scheduler))
                {
                    scheduler?.Exit();
                }
                if (IncludeUnSTA && NoSTASchedulers.TryGetValue(target, out var noSTASchedulers))
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
