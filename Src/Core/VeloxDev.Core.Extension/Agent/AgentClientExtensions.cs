using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;

namespace VeloxDev.AI;

/// <summary>
/// Mounts context providers onto an agent without going through the workflow layer.
/// </summary>
public static class AgentClientExtensions
{
    /// <summary>
    /// Creates an agent whose instructions are the given static skeleton and whose tools come entirely
    /// from <paramref name="providers"/>.
    /// <para>
    /// <b>There is deliberately no tools parameter.</b> The Agent Framework unions a provider's tools with
    /// whatever <c>ChatOptions.Tools</c> carries without deduplicating by name, so a tool offered through
    /// both channels is sent to the model twice. Leaving the parameter out makes that mistake unavailable
    /// rather than merely documented.
    /// </para>
    /// <example>
    /// <code>
    /// var agent = chatClient.AsAIAgent([mcp.CreateContextProvider()]);
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="chatClient">The client to build the agent over.</param>
    /// <param name="providers">
    /// The providers to attach, in the order their contributions should be concatenated into the prompt.
    /// For a scope, that is <c>CreateContextProviders()</c>.
    /// </param>
    /// <param name="instructions">The static skeleton, or <c>null</c> for none.</param>
    /// <remarks>
    /// <b>Providers come first on purpose.</b> The Agent Framework already offers
    /// <c>AsAIAgent(this IChatClient, string instructions = null, …)</c> with every parameter optional. A
    /// signature of <c>(string?, params AIContextProvider[])</c> would make <c>AsAIAgent("x")</c> match
    /// both overloads — theirs in normal form, this one in expanded form — and C# prefers the normal one,
    /// so the call would silently bind to theirs and attach nothing. Putting a differently-typed required
    /// parameter first removes the ambiguity.
    /// </remarks>
    public static ChatClientAgent AsAIAgent(
        this IChatClient chatClient,
        IReadOnlyList<AIContextProvider> providers,
        string? instructions = null)
    {
        if (chatClient is null) throw new ArgumentNullException(nameof(chatClient));

        return chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = instructions },
            AIContextProviders = providers ?? [],
        });
    }
}
