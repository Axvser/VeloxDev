using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI;

/// <summary>
/// Wraps an <see cref="AIFunction"/> so that a call to it is subject to the owning layer's
/// <see cref="AgentToolPolicy"/>: marshalled onto the host's thread, gated, and reported afterwards.
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
internal sealed class TrackedAIFunction(AIFunction inner, AgentToolPolicy? policy = null) : DelegatingAIFunction(inner)
{
    private readonly AgentToolPolicy _policy = policy ?? new AgentToolPolicy();

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // Components are UI-bound, so when the host configured a UI SynchronizationContext and we are not
        // already on it, marshal the entire call (body + policy hooks) onto it. Resolved per call, since
        // the host may register the context after this wrapper was built.
        var uiContext = _policy.MarshalTo?.Invoke();
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
        if (_policy.Refuse?.Invoke(Name) is { } refusal)
            return Error(refusal);

        try
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken);
            if (_policy.AfterCall is { } afterCall)
                await afterCall(Name, result?.ToString() ?? string.Empty);
            return result;
        }
        catch (Exception ex)
        {
            return Error($"Tool '{Name}' threw an unhandled exception: {ex.Message}");
        }
    }

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
