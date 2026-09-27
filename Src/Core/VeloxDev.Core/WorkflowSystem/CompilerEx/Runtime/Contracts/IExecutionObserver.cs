using System;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>One thing a compiled run did, in the order it did it.</summary>
public enum ExecutionObservationKind
{
    /// <summary>The run began. <see cref="ExecutionObservation.Node"/> is <c>null</c>.</summary>
    RunStarted = 0,

    /// <summary>A fan-out branch began. <see cref="ExecutionObservation.Node"/> is <c>null</c>.</summary>
    BranchStarted,

    /// <summary>A node is about to be driven.</summary>
    NodeStarted,

    /// <summary>A node returned without throwing.</summary>
    NodeSucceeded,

    /// <summary>A node threw, or requested a redirect through <see cref="IRuntimeContext.Error"/>.</summary>
    NodeFailed,

    /// <summary>A node is being driven again after a failure; the retry number is in the observation's detail.</summary>
    NodeRetried,

    /// <summary>The run ended. <see cref="ExecutionObservation.Node"/> is <c>null</c>.</summary>
    RunEnded,
}

/// <summary>
/// One observation, in the shape the run's own events already take in this library: a kind, what it was about, and
/// a short detail — rather than one method per kind, so a new kind does not break an existing observer.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Node">The node this is about, or <c>null</c> for a run- or branch-level observation.</param>
/// <param name="Detail">Free-form context: a failure message, a retry counter, a branch key.</param>
/// <param name="Attempt">
/// The run's attempt number — the same value <see cref="IRuntimeContext.Attempt"/> carries, i.e. which pass over
/// the graph this is. A retry is <b>not</b> a new attempt and does not move it.
/// </param>
/// <param name="Elapsed">How long the step took, where that is meaningful.</param>
public readonly record struct ExecutionObservation(
    ExecutionObservationKind Kind,
    IWorkflowNodeViewModel? Node,
    string? Detail,
    int Attempt,
    TimeSpan Elapsed);

/// <summary>
/// Watches a compiled run: who was driven, in what order, how it went. The seam to hang OpenTelemetry (or anything
/// else that wants a run's timeline) on.
/// </summary>
/// <remarks>
/// <para>
/// <b>A broken observer never changes a run.</b> A throw is swallowed and written to the run's own log, because an
/// observer is a diagnostic: the log writer reports its failures through
/// <see cref="RuntimeContext.LogWriteFailed"/> only because losing a line is losing evidence, and the same is not
/// true of an observation.
/// </para>
/// <para>
/// Configured as <see cref="RuntimeContext.Observer"/>; nothing is observed while it is <c>null</c>.
/// </para>
/// </remarks>
/// <seealso cref="DelegateExecutionObserver"/>
public interface IExecutionObserver
{
    /// <summary>Called once per observation. Must not block: the run is waiting on it.</summary>
    /// <param name="observation">What happened.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task OnObservedAsync(ExecutionObservation observation, CancellationToken cancellationToken);
}
