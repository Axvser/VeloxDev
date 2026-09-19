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
    /// Runs the whole chain this many further times. 0 runs it once, <c>int.MaxValue</c> runs it forever.
    /// </summary>
    /// <remarks>
    /// The chain-level counterpart of the effect's <c>LoopTime</c>, following the same rule: the count is the
    /// number of additional cycles, so <c>Repeat(2)</c> runs the chain three times in all. Every cycle replays the
    /// frame sets the first one prepared, so a segment starts from the value captured when the chain started rather
    /// than from wherever the previous cycle left the target.
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