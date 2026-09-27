using System.Threading;
using System.Threading.Tasks;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>One node that succeeded during a run that is now ending badly, and what it produced.</summary>
/// <param name="Node">The node that ran.</param>
/// <param name="Output">What it returned, as it was registered for downstream nodes.</param>
/// <param name="Order">The node's compile order, or <c>-1</c> when it has none.</param>
public readonly record struct NodeCompensation(
    IWorkflowNodeViewModel Node,
    object? Output,
    int Order);

/// <summary>
/// Told about the nodes a failed or cancelled run already drove, so the host can undo what it cares about.
/// Configured as <see cref="RuntimeContext.Compensation"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The engine does not roll anything back, and cannot.</b> A node's effects are its own — a property it wrote, a
/// process it started, a file it left behind — and only the host knows which of those are reversible. So the engine
/// hands over the successes and stops there.
/// </para>
/// <para>
/// <b>The tree's undo stack is not a substitute.</b> It records model-structure changes only (a node's own property
/// writes and a script's file effects were never submitted), it is one flat stack with no run boundary — so a
/// failure could not unwind this run's entries without eating the user's — and it does not guarantee the chain
/// completes. A host is free to drive its own undo from a compensation; it just cannot expect the engine to.
/// </para>
/// <para>
/// Called <b>most recent first</b>, once per node that succeeded this pass, and only when the run ends with
/// <see cref="RunOutcome.Failed"/> or <see cref="RunOutcome.Cancelled"/> — a completed run compensates nothing.
/// A compensation that throws is logged and the rest still run: the original failure stays the headline.
/// </para>
/// </remarks>
/// <seealso cref="DelegateExecutionCompensation"/>
public interface IExecutionCompensation
{
    /// <summary>Called once per successfully driven node, in reverse drive order.</summary>
    /// <param name="compensation">The node and its output.</param>
    /// <param name="cancellationToken">A token for the cleanup; it is <b>not</b> the run's — that one is already
    /// cancelled when this is called after a cancellation.</param>
    Task CompensateAsync(NodeCompensation compensation, CancellationToken cancellationToken);
}
