using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The pause point of a compiled run: <see cref="RuntimeEngine"/> awaits it before driving each node, so a host can
/// hold a long run and let it go again.
/// </summary>
/// <remarks>
/// <para>
/// <b>It pauses between nodes, never inside one.</b> A node already being driven runs to completion — the same
/// rule the tool layer states for half-finished mutations, and the reason a pause cannot corrupt a graph.
/// </para>
/// <para>
/// <b><see cref="WaitAsync"/> must honour the token, and cancellation must <i>throw</i>.</b> Returning normally on
/// cancellation would leave the caller looping, and a run stopped by the host while paused has to end rather than
/// hang: <see cref="RuntimeEngine.RunAsync"/> already turns <see cref="System.OperationCanceledException"/> into
/// <c>Status = "Stopped"</c>.
/// </para>
/// <para>
/// Configured as <see cref="RuntimeContext.ExecutionGate"/>. While it holds, the engine writes
/// <c>Status = "Paused"</c> and puts it back to <c>"Running"</c> on release — a paused run has no other bindable
/// state, since <c>IsRunning</c> only distinguishes running from not.
/// </para>
/// </remarks>
/// <seealso cref="ManualExecutionGate"/>
public interface IExecutionGate
{
    /// <summary>
    /// Awaited before each node is driven. Return a completed task to let the run proceed; hold the returned task
    /// to pause. The same gate is asked again before the next node, so releasing it resumes the run.
    /// </summary>
    /// <param name="cancellationToken">
    /// The run's token. Honour it: a paused run that the host stops must not wait forever.
    /// </param>
    Task WaitAsync(CancellationToken cancellationToken);
}
