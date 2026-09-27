using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Where a run's place is kept, so a later run can pick it up. Configured as <see cref="RuntimeContext.CheckpointStore"/>;
/// with none configured the engine never writes one.
/// </summary>
/// <remarks>
/// <para>
/// <b>One store, one run.</b> The engine writes the run's <i>current</i> state — not a history — so a store holds a
/// single latest <see cref="ExecutionCheckpoint"/>; a host that keeps several runs' places keeps several stores (or
/// keys its own implementation by <see cref="IRuntimeContext.Uid"/>).
/// </para>
/// <para>
/// <b>Written after each node succeeds, from inside the drive.</b> A fan-out's branches are interleaved, so two
/// saves can be in flight at once even though nothing here runs on a second thread; an implementation that writes a
/// file must serialise its own writes. Saving is also best effort: a store that throws gets one log line and the run
/// carries on — a place you cannot write down is not a reason to stop working.
/// </para>
/// <para>
/// <see cref="LoadAsync"/> is the host's call, not the engine's: <see cref="RuntimeEngine.RunAsync"/> takes the
/// checkpoint it should resume from, and it is the host that decides when there is one worth resuming.
/// </para>
/// </remarks>
/// <seealso cref="InMemoryCheckpointStore"/>
/// <seealso cref="ExecutionCheckpoint"/>
public interface IExecutionCheckpointStore
{
    /// <summary>Replaces what was saved with the run's current state.</summary>
    /// <param name="checkpoint">The state to keep. Immutable as far as the engine is concerned — it is a snapshot.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task SaveAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>Reads the last saved state.</summary>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The checkpoint, or <c>null</c> when nothing was ever saved.</returns>
    Task<ExecutionCheckpoint?> LoadAsync(CancellationToken cancellationToken);
}
