using VeloxDev.Lifetime;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>
/// The base an adapter's host derives from: the dispatcher's derived surface plus a liveness flag.
/// </summary>
public abstract class TransitionHostBase<TPriorityCore> : ThreadDispatcherBase<TPriorityCore>, ITransitionHost<TPriorityCore>
{
    /// <summary>What a host reports its own exit into. Override <see cref="IsAlive"/> instead when it can only ask.</summary>
    protected ApplicationState Lifetime { get; } = new();

    public virtual bool IsAlive => Lifetime.IsAlive;
}
