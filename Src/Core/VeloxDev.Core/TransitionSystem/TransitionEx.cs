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

    /// <summary>
    /// Runs this segment's loop this many further times. 0 runs it once, <c>int.MaxValue</c> runs it forever.
    /// </summary>
    /// <remarks>
    /// A segment's loop wraps the chain from its first segment through this one, and loops nest by where they end:
    /// three segments each carrying <c>Repeat(1)</c> run <c>1, 1, 2, 1, 1, 2, 3, 1, 1, 2, 1, 1, 2, 3</c>, so only a
    /// count on the last segment repeats the whole chain. Every iteration after a segment's first replays the frame
    /// set that first iteration prepared.
    /// </remarks>
    public static T Repeat<T>(this T snapshot, int count)
        where T : StateSnapshotCore, new()
    {
        return snapshot.CoreRepeat<T>(count);
    }

    public static TSnapshot Interpolator<TSnapshot, TTarget, TValue>(
        this TSnapshot snapshot,
        Expression<Func<TTarget, TValue>> propertyLambda,
        ISampler interpolator)
        where TSnapshot : StateSnapshotCore, new()
    {
        return snapshot.CoreInterpolator<TSnapshot, TTarget, TValue>(propertyLambda, interpolator);
    }
}