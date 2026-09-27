using System;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>Where in a run a failure came from.</summary>
public enum ExecutionFailurePhase
{
    /// <summary>A node threw, or asked to redirect through <see cref="IRuntimeContext.Error"/>.</summary>
    Node = 0,

    /// <summary>A router threw while resolving its route key at run time.</summary>
    Router,

    /// <summary>A node threw while resolving where to redirect to.</summary>
    Redirect,

    /// <summary>The run itself failed — the redirect cap, or a cancellation that ended it.</summary>
    Run,
}

/// <summary>
/// One failure, as a record rather than a log line. The point is the fields: which node, which phase, what
/// exception, on which pass.
/// </summary>
/// <param name="Phase">Where it came from.</param>
/// <param name="Node">The node involved, or <c>null</c> for a run-level failure.</param>
/// <param name="Message">The message the engine recorded.</param>
/// <param name="Error">The exception, when there was one.</param>
/// <param name="Attempt">The run's attempt number, as in <see cref="IRuntimeContext.Attempt"/>.</param>
/// <param name="Order">The node's compile order, or <c>-1</c> when it has none.</param>
public readonly record struct ExecutionError(
    ExecutionFailurePhase Phase,
    IWorkflowNodeViewModel? Node,
    string Message,
    Exception? Error,
    int Attempt,
    int Order);

/// <summary>
/// Receives every failure a compiled run records. Configured as <see cref="RuntimeContext.ErrorSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// The run already writes each of these as a log line; a sink is for a host that wants them as data — to count
/// them, store them, or show them in a panel. The engine deliberately does not classify: what a failure means is
/// the host's business, so it gets the fields and decides.
/// </para>
/// <para>
/// A sink that throws is swallowed and written to the run's log: reporting a failure must not add one.
/// </para>
/// </remarks>
/// <seealso cref="DelegateExecutionErrorSink"/>
public interface IExecutionErrorSink
{
    /// <summary>Called once per recorded failure.</summary>
    /// <param name="error">The failure.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task OnErrorAsync(ExecutionError error, CancellationToken cancellationToken);
}
