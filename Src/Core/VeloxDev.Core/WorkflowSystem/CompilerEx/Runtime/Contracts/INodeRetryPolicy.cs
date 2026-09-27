using System;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>One node's failure, as the retry policy sees it.</summary>
/// <param name="Node">The node that failed.</param>
/// <param name="Error">What it threw.</param>
/// <param name="RetryNumber">Which retry this decision is about — <c>1</c> for the first.</param>
/// <param name="Elapsed">How long the failed attempt took.</param>
public readonly record struct NodeFailure(
    IWorkflowNodeViewModel Node,
    Exception Error,
    int RetryNumber,
    TimeSpan Elapsed);

/// <summary>
/// Decides whether a node that threw gets another go, and after how long. Configured as
/// <see cref="RuntimeContext.RetryPolicy"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only thrown exceptions are retried.</b> A node that calls <see cref="IRuntimeContext.Error"/> or
/// <see cref="IRuntimeContext.Warn"/> is making a deliberate redirect request — that is control flow the node asked
/// for, not a failure to try again.
/// </para>
/// <para>
/// <b>A retry is not an attempt.</b> <see cref="IRuntimeContext.Attempt"/> counts passes over the graph (it is also
/// the output registry's stamp, so the join aggregation depends on it); retry numbering is the policy's own and
/// reaches the log as <c>[Retry n/m]</c>.
/// </para>
/// <para>
/// Returning <c>null</c> — or a policy that is not configured — leaves the failure on the engine's ordinary path:
/// a redirect for an <see cref="IRedirectable"/> node, otherwise the flow ends. Cancellation is never retried.
/// </para>
/// </remarks>
/// <seealso cref="ExponentialBackoffRetry"/>
public interface INodeRetryPolicy
{
    /// <summary>
    /// Called after a node threw. Use <see cref="NodeFailure.RetryNumber"/> to stop eventually — a policy that
    /// always returns a delay retries forever, and the run has no other cap.
    /// </summary>
    /// <param name="failure">The failure being considered.</param>
    /// <param name="cancellationToken">The run's token.</param>
    /// <returns>The delay before the next attempt, or <c>null</c> to give the failure to the engine.</returns>
    Task<TimeSpan?> NextRetryAsync(NodeFailure failure, CancellationToken cancellationToken);
}
