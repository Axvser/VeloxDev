using System;
using System.Text;
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
/// <para>
/// <b>Fragments are coalesced before they reach the transcript.</b> A streamed answer arrives as one event
/// per token-ish chunk, and each write to an entry is a concatenation onto the whole answer so far — so
/// writing per fragment costs O(answer length) per fragment and O(answer × fragments) in all, which is
/// quadratic in what the user sees. The stage therefore holds the tail of the open entry and hands it over
/// once it either reaches <see cref="FlushThreshold"/> characters or the stream reaches a boundary (a new
/// turn, a tool call, the end of the run). It never holds a fragment across any of those, because the
/// transcript continues <i>the open entry</i> — text still held when a tool call closes that entry would
/// land in the next one.
/// </para>
/// <para>
/// A boundary is also where holding stops paying: the first fragment after one is written straight through,
/// so a short answer and the start of a long one appear with no added latency, and what lags is only ever
/// the tail. The events themselves are untouched — every <see cref="AgentTextDelta"/> still reaches every
/// later stage and every subscriber; only the write into the transcript is batched.
/// </para>
/// <para>
/// Stateful, and not thread-safe: one stage feeds one conversation, on whatever thread the events are
/// published on.
/// </para>
/// </summary>
public sealed class TextPipeline(Func<AgentTranscript?> transcript, Func<SynchronizationContext?>? marshalTo = null)
    : IAgentPipelineStage
{
    /// <summary>
    /// How much of the open entry this stage may hold before writing it through.
    /// <para>
    /// The trade is staleness against copying: the entry is a string, so every write copies all of it, and
    /// batching by <c>n</c> characters turns that from once per fragment into once per <c>n</c> characters.
    /// A kilobyte is a few lines — invisible next to a panel that re-renders on the host's own clock, and
    /// small enough that the answer's tail is never meaningfully behind the screen.
    /// </para>
    /// </summary>
    private const int FlushThreshold = 1024;

    /// <summary>
    /// Resolved per event rather than captured: a scope composes its chain on first use and anything that
    /// reads the pipeline decides what the chain is made of, so a host that attached a subsystem before its
    /// conversation would otherwise get a chain with no text stage in it — and an empty panel. Asking for
    /// the conversation each time makes the attach order irrelevant.
    /// </summary>
    private readonly Func<AgentTranscript?> _transcript = transcript ?? throw new ArgumentNullException(nameof(transcript));

    /// <summary>
    /// Resolved per event rather than captured, so a host may register its UI context after the pipeline
    /// was built — the same reason the tool policy resolved its marshalling target lazily.
    /// </summary>
    private readonly Func<SynchronizationContext?>? _marshalTo = marshalTo;

    // 攒着的尾巴，与它属于哪种条目。角色一换就得先落地：转录按「开着的、同角色的那条」续写。
    private readonly StringBuilder _pending = new();
    private bool _pendingIsReasoning;
    private bool _hasPending;

    // 上一个边界事件之后是否已经落地过一次。为 false 时下一个片段直接写穿 —— 短回答因此与不合并时无异。
    private bool _deliveredSinceBoundary;

    /// <inheritdoc />
    public async ValueTask OnEventAsync(
        AgentEvent agentEvent, Func<AgentEvent, ValueTask> next, CancellationToken cancellationToken)
    {
        var context = _marshalTo?.Invoke();
        var transcript = _transcript();

        if (transcript is null)
        {
            // 没有对话可喂：与不带转录时一样，片段流过就算了，不攒 —— 攒下来也没有第二个回合来落地它。
            DiscardPending();
            await next(agentEvent).ConfigureAwait(false);
            return;
        }

        switch (agentEvent)
        {
            case AgentTextDelta text:
                await BufferAsync(context, transcript, text.Text, reasoning: false).ConfigureAwait(false);
                break;

            case AgentReasoningDelta reasoning:
                await BufferAsync(context, transcript, reasoning.Text, reasoning: true).ConfigureAwait(false);
                break;

            default:
                // 边界：先落地再处理。顺序是必须的 —— 工具调用会在下游把开着的条目关掉。
                await DeliverAsync(context, transcript).ConfigureAwait(false);
                _deliveredSinceBoundary = false;

                switch (agentEvent)
                {
                    case AgentTurnStarted started when !string.IsNullOrEmpty(started.Prompt):
                        await PipelineDispatch.RunAsync(context, () => transcript.AddUser(started.Prompt!)).ConfigureAwait(false);
                        break;

                    case AgentTurnCompleted:
                    case AgentTurnFaulted:
                        // The turn is over, whatever happened in it: close the open entry so the next one starts
                        // fresh. Done before the fault case below so a cancelled turn closes too.
                        await PipelineDispatch.RunAsync(context, transcript.CloseOpen).ConfigureAwait(false);
                        break;
                }

                // A cancelled run is not an error: the host asked for it, and rendering it as a failure
                // would put a red line under something that worked.
                if (agentEvent is AgentTurnFaulted { Cancelled: false } faulted)
                    await PipelineDispatch.RunAsync(context, () => transcript.AddError(Describe(faulted))).ConfigureAwait(false);
                break;
        }

        await next(agentEvent).ConfigureAwait(false);
    }

    private async ValueTask BufferAsync(
        SynchronizationContext? context, AgentTranscript transcript, string fragment, bool reasoning)
    {
        if (string.IsNullOrEmpty(fragment)) return;

        if (_hasPending && _pendingIsReasoning != reasoning)
        {
            // 换了角色（推理 ↔ 回答）：转录会另开一条，所以边界与「新回合」同级 —— 落地，且下一个片段直写。
            await DeliverAsync(context, transcript).ConfigureAwait(false);
            _deliveredSinceBoundary = false;
        }

        _pendingIsReasoning = reasoning;
        _hasPending = true;
        _pending.Append(fragment);

        if (!_deliveredSinceBoundary || _pending.Length >= FlushThreshold)
            await DeliverAsync(context, transcript).ConfigureAwait(false);
    }

    /// <summary>Writes the held tail into the open entry, if there is one.</summary>
    private async ValueTask DeliverAsync(SynchronizationContext? context, AgentTranscript transcript)
    {
        if (!_hasPending) return;

        var text = _pending.ToString();
        var reasoning = _pendingIsReasoning;
        _pending.Clear();
        _hasPending = false;
        _deliveredSinceBoundary = true;

        await PipelineDispatch.RunAsync(context, () =>
        {
            if (reasoning) transcript.AppendReasoning(text);
            else transcript.AppendAnswer(text);
        }).ConfigureAwait(false);
    }

    private void DiscardPending()
    {
        _pending.Clear();
        _hasPending = false;
    }

    private static string Describe(AgentTurnFaulted faulted)
        => faulted.Error is null ? "运行失败。" : $"运行失败：{faulted.Error.Message}";
}
