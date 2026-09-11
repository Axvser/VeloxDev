using System.Linq.Expressions;

namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class StateSnapshotCore<T> : StateSnapshotCore where T : class
{
    internal TimeSpan delay = TimeSpan.Zero;

    /// <summary>
    /// Starts this snapshot on <paramref name="target"/>.
    /// The target type is fixed by <typeparamref name="T"/>, so it is checked at compile time.
    /// </summary>
    public void Execute(T target, bool CanMutualTask = true)
    {
        CoreExecute(target, CanMutualTask);
    }

    /// <summary>
    /// Stops the animations running on <paramref name="target"/>. Equivalent to <see cref="TransitionCore.Exit{T}"/>.
    /// </summary>
    public void Exit(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.Exit((object)target, IncludeMutual, IncludeNoMutual);

    internal override T1 CoreAwait<T1>(TimeSpan timeSpan)
    {
        if (this is not T1 snapshot)
        {
            throw new InvalidOperationException($"The current StateSnapshotCore is not of type {typeof(T1).Name}.");
        }
        delay = timeSpan;
        return snapshot;
    }
}

public abstract class StateSnapshotCore
{
    internal abstract void AsRoot();
    internal abstract T1 CoreInterpolator<T1, TTarget, TValue>(Expression<Func<TTarget, TValue>> propertyLambda, ISampler interpolator)
        where T1 : StateSnapshotCore;
    protected abstract T1 CoreEffect<T1, T2>(T2 effect) where T2 : ITransitionEffectCore;
    protected abstract T1 CoreEffect<T1, T2>(Action<T2> effectSetter) where T2 : ITransitionEffectCore, new();
    internal abstract IFrameState CoreRecordState();
    internal abstract T CoreAwait<T>(TimeSpan timeSpan)
        where T : StateSnapshotCore, new();
    internal abstract T CoreThen<T>()
        where T : StateSnapshotCore, new();
    internal abstract T CoreAwaitThen<T>(TimeSpan timeSpan)
        where T : StateSnapshotCore, new();
    internal abstract void CoreExecute(object target, bool CanMutualTask = true);
}
