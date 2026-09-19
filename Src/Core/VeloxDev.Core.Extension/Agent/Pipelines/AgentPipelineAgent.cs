using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.Pipelines;

/// <summary>
/// Sits in MAF's official agent-middleware slot and publishes what the run did into an
/// <see cref="AgentPipeline"/>.
/// <para>
/// Agent middleware is the outermost layer of the official pipeline — it wraps the whole run, context
/// resolution and chat-client calls included — and the framework documents it as being restricted to
/// "common functionality", which is exactly what observing a run is. Because it sits there, a host keeps
/// calling <c>RunAsync</c> / <c>RunStreamingAsync</c> exactly as before.
/// </para>
/// <para>
/// <b>This is where reasoning is recovered.</b> A run's output arrives as <see cref="AgentResponseUpdate"/>
/// carrying a list of <see cref="AIContent"/>; the framework's own <c>Text</c> shortcut concatenates the
/// text-bearing content of the answer, and the thinking the model emitted lives beside it as
/// <see cref="TextReasoningContent"/>. Read <c>Text</c> alone — as every demo did — and the reasoning is
/// gone before anything downstream can see it.
/// </para>
/// <para>
/// Observation only: the updates are yielded on unchanged and unbuffered, so the stream keeps whatever
/// timing it had.
/// </para>
/// </summary>
public sealed class AgentPipelineAgent(AIAgent innerAgent, AgentPipeline pipeline) : DelegatingAIAgent(innerAgent)
{
    private readonly AgentPipeline _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Materialized once: the prompt is read from these messages and then the same sequence is handed to
        // the inner agent, so a lazy or single-shot enumerable would be consumed twice.
        var request = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        var prompt = LastUserText(request);
        var started = Stopwatch.StartNew();
        await PublishAsync(new AgentTurnStarted(AgentRunKind.Complete, prompt), cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await base.RunCoreAsync(request, session, options, cancellationToken).ConfigureAwait(false);

            // The non-streaming path hands back whole messages, so the reasoning is read from their content
            // rather than from updates.
            foreach (var message in response.Messages)
                await PublishContentsAsync(message.Contents, cancellationToken).ConfigureAwait(false);

            started.Stop();
            await PublishAsync(new AgentTurnCompleted(AgentRunKind.Complete, response.Text, started.Elapsed), cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PublishAsync(new AgentTurnFaulted(AgentRunKind.Complete, null, cancelled: true), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await PublishAsync(new AgentTurnFaulted(AgentRunKind.Complete, ex, cancelled: false), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Materialized once, for the same reason as the non-streaming path.
        var request = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        var prompt = LastUserText(request);
        var started = Stopwatch.StartNew();
        await PublishAsync(new AgentTurnStarted(AgentRunKind.Streaming, prompt), cancellationToken).ConfigureAwait(false);

        var failed = false;

        try
        {
            // The update is inspected and then yielded untouched: a middleware stage must not consume, buffer
            // or re-time the stream it is watching.
            await using var updates = base
                .RunCoreStreamingAsync(request, session, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                AgentResponseUpdate current;
                try
                {
                    if (!await updates.MoveNextAsync().ConfigureAwait(false)) break;
                    current = updates.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await PublishAsync(new AgentTurnFaulted(AgentRunKind.Streaming, null, cancelled: true), CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    failed = true;
                    await PublishAsync(new AgentTurnFaulted(AgentRunKind.Streaming, ex, cancelled: false), CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                await PublishContentsAsync(current.Contents, cancellationToken).ConfigureAwait(false);
                yield return current;
            }
        }
        finally
        {
            // In a finally, not after the loop: a consumer that breaks out early abandons the enumerator
            // without ever reaching the code below it, and a turn that never ends leaves the transcript's
            // entry open — the next turn's first fragment would then be appended into the abandoned bubble.
            started.Stop();
            if (!failed)
                await PublishAsync(new AgentTurnCompleted(AgentRunKind.Streaming, null, started.Elapsed), CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Splits one update's content into the two things a reader cares about — what was said, and what was
    /// thought — instead of flattening both into a single text property.
    /// </summary>
    private async ValueTask PublishContentsAsync(IList<AIContent>? contents, CancellationToken cancellationToken)
    {
        if (contents is null) return;

        foreach (var content in contents)
        {
            switch (content)
            {
                case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                    await PublishAsync(new AgentReasoningDelta(reasoning.Text), cancellationToken).ConfigureAwait(false);
                    break;

                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    await PublishAsync(new AgentTextDelta(text.Text), cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private ValueTask PublishAsync(AgentEvent agentEvent, CancellationToken cancellationToken)
        => _pipeline.PublishAsync(agentEvent, cancellationToken);

    private static string? LastUserText(IReadOnlyList<ChatMessage>? messages)
    {
        if (messages is null) return null;

        string? last = null;
        foreach (var message in messages)
            if (message.Role == ChatRole.User)
                last = message.Text;
        return last;
    }
}

/// <summary>Attaches an <see cref="AgentPipeline"/> in the official middleware slot.</summary>
public static class AgentPipelineExtensions
{
    /// <summary>
    /// Wraps <paramref name="agent"/> so its runs are published into <paramref name="pipeline"/>.
    /// <para>
    /// Idiomatic form: <c>agent.AsBuilder().UseAgentPipeline(pipeline).Build()</c> — the same shape the
    /// framework's own logging and telemetry middleware take.
    /// </para>
    /// </summary>
    public static AIAgentBuilder UseAgentPipeline(this AIAgentBuilder builder, AgentPipeline pipeline)
        => (builder ?? throw new ArgumentNullException(nameof(builder)))
            .Use(inner => new AgentPipelineAgent(inner, pipeline ?? throw new ArgumentNullException(nameof(pipeline))));

    /// <summary>Wraps <paramref name="agent"/> directly, for callers not already using a builder.</summary>
    public static AIAgent WithPipeline(this AIAgent agent, AgentPipeline pipeline)
        => new AgentPipelineAgent(
            agent ?? throw new ArgumentNullException(nameof(agent)),
            pipeline ?? throw new ArgumentNullException(nameof(pipeline)));
}
