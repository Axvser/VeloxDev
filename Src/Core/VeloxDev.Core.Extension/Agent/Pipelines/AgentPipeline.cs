using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.Pipelines;

/// <summary>
/// One stage of an <see cref="AgentPipeline"/>.
/// <para>
/// A stage sees every event in order and decides what the next stage sees: pass it on, drop it, replace it
/// with another, or emit more than one. That is the whole contract — a stage does not drive the run, it
/// observes it.
/// </para>
/// </summary>
public interface IAgentPipelineStage
{
    /// <summary>
    /// Handles one event, calling <paramref name="next"/> to pass a (possibly changed) event down the chain.
    /// Not calling <paramref name="next"/> drops it for the stages after this one.
    /// </summary>
    ValueTask OnEventAsync(AgentEvent agentEvent, Func<AgentEvent, ValueTask> next, CancellationToken cancellationToken);
}

/// <summary>A stage written as a delegate, for the common "look at it and pass it on" case.</summary>
public sealed class DelegateAgentPipelineStage(
    Func<AgentEvent, Func<AgentEvent, ValueTask>, CancellationToken, ValueTask> handler) : IAgentPipelineStage
{
    /// <inheritdoc />
    public ValueTask OnEventAsync(AgentEvent agentEvent, Func<AgentEvent, ValueTask> next, CancellationToken cancellationToken)
        => handler(agentEvent, next, cancellationToken);
}

/// <summary>
/// An ordered chain of <see cref="IAgentPipelineStage"/> that the agent's events are published through.
/// <para>
/// This is the library's own layer, not MAF's: the framework's agent middleware decides <i>when</i> a run
/// happens and what it returns, and this decides what that run <i>means</i> to a host. The two compose —
/// <see cref="AgentPipelineAgent"/> sits in the official middleware slot and publishes here.
/// </para>
/// <para>
/// Stages run in the order they were added. Events are published sequentially, so a stage may rely on the
/// ones before it having been delivered.
/// </para>
/// </summary>
public sealed class AgentPipeline
{
    private readonly List<IAgentPipelineStage> _stages = [];

    /// <summary>The stages, in order.</summary>
    public IReadOnlyList<IAgentPipelineStage> Stages => _stages;

    /// <summary>Appends a stage. Returns this, so stages read as a chain.</summary>
    public AgentPipeline Use(IAgentPipelineStage stage)
    {
        if (stage is null) throw new ArgumentNullException(nameof(stage));
        _stages.Add(stage);
        return this;
    }

    /// <summary>Appends a stage written as a delegate.</summary>
    public AgentPipeline Use(Func<AgentEvent, Func<AgentEvent, ValueTask>, CancellationToken, ValueTask> handler)
        => Use(new DelegateAgentPipelineStage(handler ?? throw new ArgumentNullException(nameof(handler))));

    /// <summary>
    /// Raised when a stage threw. The publish carries on regardless — see <see cref="PublishAsync"/>.
    /// </summary>
    public event EventHandler<AgentStageFailedEventArgs>? StageFailed;

    /// <summary>
    /// Sends an event through every stage in order.
    /// <para>
    /// <b>A broken stage does not break the run.</b> Publishing happens on the run's own path — for a tool
    /// call it happens inside the wrapper's <c>try</c>, whose <c>catch</c> turns anything thrown into the
    /// tool's error result and throws the real result away. A stage that is merely drawing something would
    /// then tell the model its tool had failed when it had succeeded.
    /// </para>
    /// <para>
    /// So each stage is isolated and a failure skips the rest of the chain, surfacing on
    /// <see cref="StageFailed"/> instead. It is reported rather than swallowed: a pipeline that silently
    /// stopped reporting would be worse than one that never had the stage.
    /// </para>
    /// </summary>
    public ValueTask PublishAsync(AgentEvent agentEvent, CancellationToken cancellationToken = default)
        => InvokeAsync(0, agentEvent ?? throw new ArgumentNullException(nameof(agentEvent)), cancellationToken);

    // Index-passing rather than a mutable cursor in a closure: a stage that publishes a second event must
    // not advance the outer chain past where it was.
    private async ValueTask InvokeAsync(int index, AgentEvent agentEvent, CancellationToken cancellationToken)
    {
        if (index >= _stages.Count) return;

        var stage = _stages[index];
        try
        {
            // A failure further down is caught at its own level, so anything caught here belongs to this
            // stage — which keeps the attribution honest.
            await stage.OnEventAsync(
                agentEvent,
                next => InvokeAsync(index + 1, next, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StageFailed?.Invoke(this, new AgentStageFailedEventArgs(stage, agentEvent, ex));
        }
    }
}

/// <summary>Carries a stage failure: which stage, which event, and what went wrong.</summary>
public sealed class AgentStageFailedEventArgs(IAgentPipelineStage stage, AgentEvent agentEvent, Exception error) : EventArgs
{
    /// <summary>The stage that threw.</summary>
    public IAgentPipelineStage Stage { get; } = stage;

    /// <summary>The event being published when it threw.</summary>
    public AgentEvent Event { get; } = agentEvent;

    /// <summary>What it threw.</summary>
    public Exception Error { get; } = error;
}
