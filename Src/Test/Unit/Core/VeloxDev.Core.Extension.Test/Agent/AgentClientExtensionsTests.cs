using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Skills;

namespace VeloxDev.Core.Extension.Test.Agent;

/// <summary>
/// Coverage for <see cref="AgentClientExtensions"/> — the one-line mounting path, and the overload
/// resolution that keeps it from silently doing nothing.
/// </summary>
[TestClass]
public class AgentClientExtensionsTests
{
    /// <summary>A chat client that answers without a network, so an agent can be built in a test.</summary>
    private sealed class OfflineChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

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

    private static IChatClient Client() => new OfflineChatClient();

    [TestMethod]
    public void AsAIAgent_AttachesTheProviders()
    {
        var mcp = new McpScope();

        var agent = Client().AsAIAgent([mcp.CreateContextProvider()]);

        Assert.IsNotNull(agent);
        Assert.HasCount(1, agent.AIContextProviders!);
        Assert.IsInstanceOfType<McpAgentContextProvider>(agent.AIContextProviders![0]);
    }

    [TestMethod]
    public void AsAIAgent_AttachesEveryProvidersContribution()
    {
        // The point of the overload: a subsystem attaches itself, and nothing has to be handed to
        // ChatOptions.Tools — which is what would duplicate every tool.
        var mcp = new McpScope();
        var skills = new SkillScope();

        var agent = Client().AsAIAgent([mcp.CreateContextProvider(), skills.CreateContextProvider()], "BASE");

        Assert.HasCount(2, agent.AIContextProviders!);
        Assert.AreEqual("BASE", agent.Instructions);
    }

    [TestMethod]
    public void AsAIAgent_WithoutInstructions_StillBuilds()
    {
        var agent = Client().AsAIAgent([new McpScope().CreateContextProvider()]);

        Assert.IsNotNull(agent);
    }

    [TestMethod]
    public void AsAIAgent_WithNoProviders_BuildsAnAgentWithNone()
    {
        var agent = Client().AsAIAgent([]);

        Assert.IsNotNull(agent);
        Assert.IsEmpty(agent.AIContextProviders ?? []);
    }

    [TestMethod]
    public void AsAIAgent_WithASingleString_BindsToTheFrameworkOverload()
    {
        // Load-bearing: this call must resolve to MAF's `AsAIAgent(this IChatClient, string instructions
        // = null, …)`, not to ours. It can only compile at all if the providers-first signature is the
        // one we kept — a `(string?, params AIContextProvider[])` version would make this ambiguous and
        // C# would pick MAF's anyway, silently attaching nothing.
        var agent = Client().AsAIAgent("JUST_INSTRUCTIONS");

        Assert.AreEqual("JUST_INSTRUCTIONS", agent.Instructions);
        Assert.IsEmpty(agent.AIContextProviders ?? [], "the framework overload attaches no providers");
    }
}
