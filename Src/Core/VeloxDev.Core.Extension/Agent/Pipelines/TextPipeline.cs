using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.Pipelines;

/// <summary>
/// Turns the model's streamed output into conversation entries — the answer, and the reasoning behind it.
/// <para>
/// This is the piece the demo host used to write for itself: loop the stream, buffer characters, split on
/// punctuation, push into a chat list. It lived in host code, so it was neither tested nor reusable, and it
/// was wrong in three ways at once. Reading the stream is <see cref="AgentPipelineAgent"/>'s job; deciding
/// what the fragments <i>mean</i> is this stage's, and <see cref="AgentTranscript"/> holds the rules.
/// </para>
/// <para>
/// <b>Reasoning is a first-class entry here.</b> The old host code read <c>AgentResponseUpdate.Text</c>,
/// which carries the answer and not the thinking, so a model that reasoned in the open looked like it had
/// answered from nowhere.
/// </para>
/// </summary>
public sealed class TextPipeline(AgentTranscript transcript, Func<SynchronizationContext?>? marshalTo = null)
    : IAgentPipelineStage
{
    private readonly AgentTranscript _transcript = transcript ?? throw new ArgumentNullException(nameof(transcript));

    /// <summary>
    /// Resolved per event rather than captured, so a host may register its UI context after the pipeline
    /// was built — the same reason the tool policy resolved its marshalling target lazily.
    /// </summary>
    private readonly Func<SynchronizationContext?>? _marshalTo = marshalTo;

    /// <inheritdoc />
    public async ValueTask OnEventAsync(
        AgentEvent agentEvent, Func<AgentEvent, ValueTask> next, CancellationToken cancellationToken)
    {
        var context = _marshalTo?.Invoke();

        switch (agentEvent)
        {
            case AgentTurnStarted started when !string.IsNullOrEmpty(started.Prompt):
                await PipelineDispatch.RunAsync(context, () => _transcript.AddUser(started.Prompt!)).ConfigureAwait(false);
                break;

            case AgentTextDelta text:
                await PipelineDispatch.RunAsync(context, () => _transcript.AppendAnswer(text.Text)).ConfigureAwait(false);
                break;

            case AgentReasoningDelta reasoning:
                await PipelineDispatch.RunAsync(context, () => _transcript.AppendReasoning(reasoning.Text)).ConfigureAwait(false);
                break;

            case AgentTurnCompleted:
            case AgentTurnFaulted:
                // The turn is over, whatever happened in it: close the open entry so the next one starts
                // fresh. Done before the fault case below so a cancelled turn closes too.
                await PipelineDispatch.RunAsync(context, _transcript.CloseOpen).ConfigureAwait(false);
                break;
        }

        switch (agentEvent)
        {
            case AgentTurnFaulted { Cancelled: false } faulted:
                // A cancelled run is not an error: the host asked for it, and rendering it as a failure
                // would put a red line under something that worked.
                await PipelineDispatch.RunAsync(context, () => _transcript.AddError(Describe(faulted))).ConfigureAwait(false);
                break;
        }

        await next(agentEvent).ConfigureAwait(false);
    }

    private static string Describe(AgentTurnFaulted faulted)
        => faulted.Error is null ? "运行失败。" : $"运行失败：{faulted.Error.Message}";
}
