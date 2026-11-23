
namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The dataflow task context: the only payload shape a node receives via
/// <see cref="INodeComponent.ReceiveCommand"/>. Access members (Data/Sender/Receiver/IsCompilePhase) all
/// inherit from <see cref="IAccessContext"/> — broadcast along a concrete link carries
/// <see cref="IAccessContext.Sender"/>/<see cref="IAccessContext.Receiver"/>; the compiler driving in order or a
/// manual Run carries only <see cref="IContext.Data"/>.
/// </summary>
public interface ITaskContext : IAccessContext
{
    // Data / Sender / Receiver / IsCompilePhase are all inherited from IAccessContext.
}
