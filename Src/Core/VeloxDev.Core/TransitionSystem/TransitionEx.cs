using System.Linq.Expressions;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.TransitionSystem;

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

    public static TSnapshot Interpolator<TSnapshot, TTarget, TValue>(
        this TSnapshot snapshot,
        Expression<Func<TTarget, TValue>> propertyLambda,
        IValueInterpolator interpolator)
        where TSnapshot : StateSnapshotCore, new()
    {
        return snapshot.CoreInterpolator<TSnapshot, TTarget, TValue>(propertyLambda, interpolator);
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