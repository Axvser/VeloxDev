using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Pipelines;

namespace VeloxDev.AI;

/// <summary>
/// Wraps an <see cref="AIFunction"/> so that a call to it is subject to the owning layer's
/// <see cref="ToolPipeline"/>: marshalled onto the host's thread, gated, and reported into the pipeline.
/// <para>
/// <b>The tool bodies deliberately contain no <c>ConfigureAwait(false)</c>.</b> A posted callback runs
/// with the dispatcher installed as <see cref="SynchronizationContext.Current"/>, so a bare
/// <c>await</c> resumes back on the UI thread — which is what keeps the parts of a tool that run
/// <i>after</i> an await (connection verification, the second iteration of a loop, the execution engine
/// driving a compiled chain) on the thread the components are bound to. Adding
/// <c>ConfigureAwait(false)</c> to a tool body silently moves exactly that work onto a thread-pool thread
/// and breaks the contract this wrapper exists to provide. The only awaits that keep it are the enclosing
/// waits on this wrapper's own task, which run on the caller's thread, not the UI's.
/// </para>
/// <para>
/// A tool that is not an <see cref="AIFunction"/> cannot be wrapped and is passed through unchanged; see
/// the callers' factory methods.
/// </para>
/// </summary>
internal sealed class TrackedAIFunction(
    AIFunction inner,
    ToolPipeline? tools = null,
    AgentPipeline? pipeline = null) : DelegatingAIFunction(inner)
{
    private readonly ToolPipeline _tools = tools ?? new ToolPipeline();

    // Null when the owner composes no chain — a wrapped tool still marshals and still refuses, it just has
    // nowhere to report to.
    private readonly AgentPipeline? _pipeline = pipeline;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // Components are UI-bound, so when the host configured a UI SynchronizationContext and we are not
        // already on it, marshal the entire call (body + reporting) onto it. Resolved per call, since the
        // host may register the context after this wrapper was built.
        var uiContext = _tools.ResolveContext();
        if (uiContext is not null && !ReferenceEquals(uiContext, SynchronizationContext.Current))
        {
            return await RunOnContextAsync(uiContext, cancellationToken,
                () => InvokeCoreInnerAsync(arguments, cancellationToken)).ConfigureAwait(false);
        }
        return await InvokeCoreInnerAsync(arguments, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<object?> InvokeCoreInnerAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // ── Pre-flight: the owner may refuse the call outright (budgets are enforced here) ──
        if (_tools.CheckRefusal(Name) is { } refusal)
        {
            // Reported as completed-without-started: the call never ran, so announcing it first would make
            // the pair a lie, and a host counting events would count something that did not happen.
            await ReportAsync(refusal, AgentToolOutcome.Refused, TimeSpan.Zero, cancellationToken);
            return Error(refusal);
        }

        // ── Pre-flight: a call the owner wants a person to approve ──
        // Awaited without ConfigureAwait(false) on purpose: this runs inside the block InvokeCoreAsync already
        // marshalled onto the host's context, and the work after the await (reporting, and the tool body when
        // it is approved) must stay on that same thread. A confirmation handler that shows a dialog depends
        // on it. Reported as Refused, like the budget gate: the call never ran.
        if (await _tools.CheckConfirmationAsync(Name, cancellationToken) is { } denial)
        {
            await ReportAsync(denial, AgentToolOutcome.Refused, TimeSpan.Zero, cancellationToken);
            return Error(denial);
        }

        var elapsed = Stopwatch.StartNew();
        if (_pipeline is not null)
            await _pipeline.PublishAsync(new AgentToolCallStarted(Name), cancellationToken);

        try
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken);
            elapsed.Stop();
            await ReportAsync(result?.ToString() ?? string.Empty, AgentToolOutcome.Succeeded, elapsed.Elapsed, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            elapsed.Stop();
            var message = $"Tool '{Name}' threw an unhandled exception: {ex.Message}";
            // The run continues: a tool that throws is reported, not fatal, which is why the outcome is
            // carried on the event rather than left to be inferred from a missing one.
            await ReportAsync(message, AgentToolOutcome.Failed, elapsed.Elapsed, cancellationToken);
            return Error(message);
        }
    }

    /// <summary>Publishes what this invocation produced, when the owner composed a chain.</summary>
    private ValueTask ReportAsync(string result, AgentToolOutcome outcome, TimeSpan elapsed, CancellationToken cancellationToken)
        => _pipeline is null
            ? default
            : _pipeline.PublishAsync(new AgentToolCallCompleted(Name, result, outcome, elapsed), cancellationToken);

    /// <summary>
    /// Runs <paramref name="body"/> on <paramref name="context"/> and awaits the outcome.
    /// <para>
    /// Awaiting rather than <c>Send</c>-ing is deliberate: the caller here is off the target thread and
    /// that thread may itself be awaiting this call, so a blocking send would deadlock. Cancellation is
    /// registered on the waiting side only — the posted body is not interrupted mid-flight, because a
    /// tool half-way through mutating the graph must not be abandoned.
    /// </para>
    /// </summary>
    private static async ValueTask<T> RunOnContextAsync<T>(
        SynchronizationContext context, CancellationToken ct, Func<ValueTask<T>> body)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            context.Post(async _ =>
            {
                try
                {
                    var result = await body();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The error envelope every wrapped tool returns when it is refused or throws. Compact, and the same
    /// shape the tool bodies use, so a caller cannot tell a wrapper failure from a tool failure by format.
    /// </summary>
    private static string Error(string message)
        => JsonConvert.SerializeObject(new { status = "error", message }, Formatting.None);
}
