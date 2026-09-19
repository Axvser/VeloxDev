using System.Linq.Expressions;
using VeloxDev.Timing;

namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class StateSnapshotCore<T> : StateSnapshotCore where T : class
{
    internal TimeSpan delay = TimeSpan.Zero;

    /// <summary>
    /// Starts this snapshot on <paramref name="target"/>.
    /// The target type is fixed by <typeparamref name="T"/>, so it is checked at compile time.
    /// </summary>
    /// <exception cref="TransitionPathUnsampleableException">
    /// A declared path can never animate — see <see cref="TransitionCore.RejectUnsampleablePaths"/>.
    /// </exception>
    public void Execute(T target, bool CanMutualTask = true)
    {
        // Validated here rather than inside CoreExecute: that one is async void, so a throw from it would escape to
        // the synchronization context instead of reaching the caller.
        CoreValidate();
        CoreExecute(target, CanMutualTask);
    }

    /// <summary>
    /// Starts this snapshot on <paramref name="target"/> against <paramref name="timeline"/>.
    /// </summary>
    /// <remarks>
    /// Animations sharing a timeline share a transport: pausing, changing the rate or seeking one moves all of them
    /// together, while each keeps its own pass and its own place in it. That is what a choreographed group needs —
    /// several animations staying in lockstep without any of them knowing about the others.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> is null.</exception>
    public void Execute(T target, ITimeSourceControl timeline, bool CanMutualTask = true)
    {
        if (timeline is null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        CoreValidate();
        CoreExecute(target, CanMutualTask, timeline);
    }

    /// <summary>
    /// Stops the animations running on <paramref name="target"/>. Equivalent to <see cref="TransitionCore.Exit{T}"/>.
    /// </summary>
    public void Exit(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.Exit((object)target, IncludeMutual, IncludeNoMutual);

    /// <summary>Freezes the animations running on <paramref name="target"/>. Equivalent to <see cref="TransitionCore.Pause{T}"/>.</summary>
    public void Pause(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.Pause((object)target, IncludeMutual, IncludeNoMutual);

    /// <summary>Lets the animations running on <paramref name="target"/> run again. Equivalent to <see cref="TransitionCore.Resume{T}"/>.</summary>
    public void Resume(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.Resume((object)target, IncludeMutual, IncludeNoMutual);

    /// <summary>Changes the playback rate on <paramref name="target"/>. Equivalent to <see cref="TransitionCore.SetRate{T}"/>.</summary>
    public void SetRate(T target, double rate, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.SetRate((object)target, rate, IncludeMutual, IncludeNoMutual);

    /// <summary>Moves within the running pass on <paramref name="target"/>. Equivalent to <see cref="TransitionCore.Seek{T}(T, TimeSpan, bool, bool)"/>.</summary>
    public void Seek(T target, TimeSpan position, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.Seek((object)target, position, IncludeMutual, IncludeNoMutual);

    /// <summary>Moves to another pass on <paramref name="target"/>. Equivalent to <see cref="TransitionCore.Seek{T}(T, int, TimeSpan, bool, bool)"/>.</summary>
    public void Seek(T target, int cycle, TimeSpan position, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.Seek((object)target, cycle, position, IncludeMutual, IncludeNoMutual);

    /// <summary>Equivalent to <see cref="TransitionCore.IsPaused{T}"/>.</summary>
    public bool IsPaused(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.IsPaused((object)target, IncludeMutual, IncludeNoMutual);

    /// <summary>Equivalent to <see cref="TransitionCore.Position{T}"/>.</summary>
    public TimeSpan Position(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.Position((object)target, IncludeMutual, IncludeNoMutual);

    /// <summary>Equivalent to <see cref="TransitionCore.Cycle{T}"/>.</summary>
    public int Cycle(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.Cycle((object)target, IncludeMutual, IncludeNoMutual);

    /// <summary>Equivalent to <see cref="TransitionCore.Rate{T}"/>.</summary>
    public double Rate(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        => TransitionCore.Rate((object)target, IncludeMutual, IncludeNoMutual);

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
    internal abstract T CoreRepeat<T>(int count)
        where T : StateSnapshotCore, new();
    internal abstract void CoreExecute(object target, bool CanMutualTask = true, ITimeSourceControl? timeline = null);
    internal abstract void CoreValidate();
}
