using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.Pipelines;

/// <summary>
/// Everything about tool calls in one place: the gates consulted before a call runs, and the conversation
/// entries written after it does.
/// <para>
/// It replaces <c>AgentToolPolicy</c>, which carried the same two gates but reported what happened through
/// a single <c>AfterCall</c> delegate — a shape that could only ever feed one observer, and that said
/// nothing at all about a call which was refused or threw. The gates stay where they were, because they
/// have to run at call time inside the wrapper; the reporting becomes pipeline events like everything else.
/// </para>
/// <para>
/// It deliberately holds no reference to the <see cref="AgentPipeline"/> its events travel on: the gates
/// and the chain are different things, and a stage naming the chain it belongs to could not be built
/// before it. <c>TrackedAIFunction</c> is handed both.
/// </para>
/// </summary>
public sealed class ToolPipeline(AgentTranscript? transcript = null, Func<SynchronizationContext?>? marshalTo = null)
    : IAgentPipelineStage
{
    private readonly AgentTranscript? _transcript = transcript;

    /// <summary>
    /// The thread the whole call is marshalled onto, or <c>null</c> to run on the caller's.
    /// <para>
    /// A delegate rather than a value because the host decides the context and may register it after this
    /// was built. Read once per call.
    /// </para>
    /// </summary>
    public Func<SynchronizationContext?>? MarshalTo { get; set; } = marshalTo;

    /// <summary>
    /// Consulted before a tool runs. Returning a message refuses the call — it becomes the tool's error
    /// result and the tool is not invoked. Call budgets are enforced here.
    /// </summary>
    public Func<string, string?>? Refuse { get; set; }

    /// <summary>The thread the wrapper should marshal a call onto, resolved now.</summary>
    internal SynchronizationContext? ResolveContext() => MarshalTo?.Invoke();

    /// <summary>
    /// Asks the gate whether this call may run. Returns the refusal, or <c>null</c> to allow it.
    /// </summary>
    internal string? CheckRefusal(string toolName) => Refuse?.Invoke(toolName);

    /// <inheritdoc />
    public async ValueTask OnEventAsync(
        AgentEvent agentEvent, Func<AgentEvent, ValueTask> next, CancellationToken cancellationToken)
    {
        if (agentEvent is AgentToolCallCompleted completed && _transcript is not null)
        {
            var context = MarshalTo?.Invoke();
            await PipelineDispatch.RunAsync(
                context,
                () => _transcript.AddToolCall(completed.ToolName, completed.Result, completed.Outcome))
                .ConfigureAwait(false);
        }

        await next(agentEvent).ConfigureAwait(false);
    }
}
