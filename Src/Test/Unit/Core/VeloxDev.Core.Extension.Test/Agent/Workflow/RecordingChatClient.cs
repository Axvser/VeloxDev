using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Workflow;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// A chat client that answers offline and records what it was handed, so a test can assert on the tool
/// surface and the prose the model <i>actually</i> received rather than on what a provider says it would
/// contribute.
/// </summary>
internal sealed class RecordingChatClient : IChatClient
{
    public List<string> ToolNames { get; } = [];

    /// <summary>Everything the model would read as prose — the options' instructions and the system
    /// messages the providers contributed.</summary>
    public List<string> Prose { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ToolNames.AddRange((options?.Tools ?? []).Select(t => t.Name));
        if (options?.Instructions is { Length: > 0 }) Prose.Add(options.Instructions);
        Prose.AddRange(messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text ?? string.Empty));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>Stands up an agent over a scope and runs one turn, with no network involved.</summary>
internal static class OfflineAgent
{
    /// <summary>
    /// Runs one turn and returns what the model was offered. <paramref name="instructions"/> stands in for
    /// whatever the host froze into <c>ChatOptions.Instructions</c> — pass a scope's static skeleton to see
    /// what the model reads once the provider's contribution is appended to it.
    /// </summary>
    internal static async Task<RecordingChatClient> RunOnce(WorkflowAgentScope scope, string instructions = "BASE")
    {
        var client = new RecordingChatClient();
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = instructions },
            AIContextProviders = scope.CreateContextProviders(),
        });

        await agent.RunAsync("hello");
        return client;
    }
}
