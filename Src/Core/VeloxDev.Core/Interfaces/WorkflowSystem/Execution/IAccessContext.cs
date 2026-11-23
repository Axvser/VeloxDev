
namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The dedicated contract for the dataflow access context — the parameter of
/// <see cref="INodeHelper.AccessAsync"/>, describing one dataflow access from "sender → receiver"
/// (a directed data edge and its phase). The payload <see cref="IContext.Data"/> is inherited from the root
/// contract (always null at compile-time). Both the compile phase
/// (<see cref="VeloxDev.Core.WorkflowSystem.CompilerEx.ICompileContext"/>) and the runtime phase
/// (<see cref="VeloxDev.Core.WorkflowSystem.CompilerEx.IRuntimeContext"/>/<see cref="ITaskContext"/>) implement it:
/// <see cref="IsCompilePhase"/> distinguishes the phase, and <see cref="Sender"/>/<see cref="Receiver"/> carry the
/// two end slots (at compile-time the compiler fills in the edges to validate).
/// </summary>
public interface IAccessContext : IContext
{
    /// <summary>true = compile-time static check (no data); false = runtime real-time check (carries Data).</summary>
    bool IsCompilePhase { get; }

    /// <summary>Sender output slot (upstream), nullable.</summary>
    ISlotComponent? Sender { get; }

    /// <summary>Receiver input slot (downstream), nullable.</summary>
    ISlotComponent? Receiver { get; }
}
