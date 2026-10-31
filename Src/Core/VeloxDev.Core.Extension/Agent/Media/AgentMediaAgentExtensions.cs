using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace VeloxDev.AI;

/// <summary>
/// Runs an agent with multimodal content attached to the user message, validating it first.
/// <para>
/// The agent framework already accepts a <see cref="ChatMessage"/> with <see cref="DataContent"/> or
/// <see cref="UriContent"/>; these overloads exist so a host does not have to hand-build that message and
/// forget the kind/media-type check, the size limits or the transcoder. A rejected payload fails before any
/// request is made — no tokens are spent on something the endpoint would refuse.
/// </para>
/// </summary>
public static class AgentMediaAgentExtensions
{
    /// <summary>Runs the agent with validated media attached to the user message.</summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="prompt">The prompt; empty is allowed when at least one payload is present.</param>
    /// <param name="media">The payloads, in the order they should appear.</param>
    /// <param name="options">The limits and transcoder; <see langword="null"/> uses the shipped defaults.</param>
    /// <param name="session">The agent session to continue, or <see langword="null"/> for a fresh run.</param>
    /// <param name="runOptions">Framework run options.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public static async Task<AgentResponse> RunWithMediaAsync(
        this AIAgent agent,
        string prompt,
        IEnumerable<AgentMedia> media,
        AgentMediaOptions? options = null,
        AgentSession? session = null,
        AgentRunOptions? runOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));
        var message = await AgentMessage.CreateUserAsync(prompt, media, options, cancellationToken).ConfigureAwait(false);
        return await agent.RunAsync(message, session, runOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the agent with already-validated media or URL references attached.</summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="prompt">The prompt; empty is allowed when at least one payload is present.</param>
    /// <param name="media">The already-validated payloads or references.</param>
    /// <param name="session">The agent session to continue, or <see langword="null"/> for a fresh run.</param>
    /// <param name="runOptions">Framework run options.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public static Task<AgentResponse> RunWithMediaAsync(
        this AIAgent agent,
        string prompt,
        IEnumerable<IAgentMediaContent> media,
        AgentSession? session = null,
        AgentRunOptions? runOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));
        return agent.RunAsync(AgentMessage.User(prompt, media), session, runOptions, cancellationToken);
    }

    /// <summary>Runs the agent with validated media attached to the user message, streaming the answer.</summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="prompt">The prompt; empty is allowed when at least one payload is present.</param>
    /// <param name="media">The payloads, in the order they should appear.</param>
    /// <param name="options">The limits and transcoder; <see langword="null"/> uses the shipped defaults.</param>
    /// <param name="session">The agent session to continue, or <see langword="null"/> for a fresh run.</param>
    /// <param name="runOptions">Framework run options.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public static async IAsyncEnumerable<AgentResponseUpdate> RunStreamingWithMediaAsync(
        this AIAgent agent,
        string prompt,
        IEnumerable<AgentMedia> media,
        AgentMediaOptions? options = null,
        AgentSession? session = null,
        AgentRunOptions? runOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));
        var message = await AgentMessage.CreateUserAsync(prompt, media, options, cancellationToken).ConfigureAwait(false);

        await foreach (var update in agent
            .RunStreamingAsync(message, session, runOptions, cancellationToken)
            .ConfigureAwait(false))
            yield return update;
    }

    /// <summary>Runs the agent with already-validated media or URL references attached, streaming the answer.</summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="prompt">The prompt; empty is allowed when at least one payload is present.</param>
    /// <param name="media">The already-validated payloads or references.</param>
    /// <param name="session">The agent session to continue, or <see langword="null"/> for a fresh run.</param>
    /// <param name="runOptions">Framework run options.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public static async IAsyncEnumerable<AgentResponseUpdate> RunStreamingWithMediaAsync(
        this AIAgent agent,
        string prompt,
        IEnumerable<IAgentMediaContent> media,
        AgentSession? session = null,
        AgentRunOptions? runOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));

        await foreach (var update in agent
            .RunStreamingAsync(AgentMessage.User(prompt, media), session, runOptions, cancellationToken)
            .ConfigureAwait(false))
            yield return update;
    }
}
