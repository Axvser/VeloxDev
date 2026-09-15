using VeloxDev.Lifetime;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem;

/// <summary>
/// Everything the animation system asks of a host: which thread each target belongs to, how to carry work there, and
/// whether the host is still running.
/// </summary>
/// <remarks>
/// A composition, not a new contract — it adds no member, and exists so an adapter declares one interface while the
/// two subsystems underneath stay independently replaceable.
/// </remarks>
public interface ITransitionHost<TPriorityCore> : IThreadDispatcher<TPriorityCore>, IApplicationState
{
}
