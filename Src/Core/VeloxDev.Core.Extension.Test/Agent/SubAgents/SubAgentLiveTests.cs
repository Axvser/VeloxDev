using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using OpenAI;
using System;
using System.ClientModel;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The two questions no offline double can answer.
/// <para>
/// Every other test in this folder drives the machinery with a fake model, which proves the machinery works
/// but says nothing about whether a model will use it. Only a real one can settle that: whether the tool
/// descriptions are clear enough that an agent dispatches a child at all, and whether a child with a narrowed
/// tool set can actually do the work it was handed and report back.
/// </para>
/// <para>
/// Gated on <c>API_KEY_DEEPSEEK</c>: without it these are <c>Inconclusive</c>, so a machine that holds no
/// key stays green rather than red. When they do run they cost a handful of model calls, so they are kept
/// to two turns of the host agent rather than a suite.
/// </para>
/// </summary>
[TestClass]
public class SubAgentLiveTests
{
    private const string KeyVariable = "API_KEY_DEEPSEEK";
    private const string Endpoint = "https://api.deepseek.com";
    private const string Model = "deepseek-v4-flash";

    /// <summary>What the host agent is asked, phrased the way a user would rather than the way a tool wants.</summary>
    private const string DispatchInstruction =
        "The workflow graph is in front of you. Work out how many nodes it has by dispatching a background "
        + "sub-agent to do the counting — do not count them yourself. Then wait for it and tell me the number "
        + "it reported.";

    [TestMethod]
    public async Task ARealModel_DispatchesAChildAtAll()
    {
        // The question the plan could not answer from the code: are the tool descriptions good enough that
        // a model reaches for SpawnSubAgent when it is the right tool? A green offline suite says nothing
        // about it, and a description written badly fails silently.
        var client = ClientOrNull();
        if (client is null) Assert.Inconclusive($"Set {KeyVariable} to run this against a real model.");

        await using var session = new LiveSession(client!);
        using var tree = new SubAgentTreeViewModel(session.SubAgents);

        await session.Host.RunAsync(DispatchInstruction);

        Assert.AreNotEqual(0, session.SubAgents.Snapshot.Count,
            "the model was asked to delegate and did not. The tool descriptions are the only thing that "
            + "could be wrong here — the offline suite already proves the tools work when they are called.");

        var row = session.SubAgents.Snapshot[0];
        Assert.AreEqual(1, row.Depth);
        Assert.AreNotEqual(0, row.GrantedToolCount, "and the child it dispatched was given tools to work with");
        Assert.IsNotNull(session.SubAgents.Children.FirstOrDefault(r => r.Id == row.Id));
    }

    [TestMethod]
    public async Task ARealChild_DoesTheWork_AndReportsItBack()
    {
        // The other half: a narrowed child, built by the library's own factory, running on a real model, with
        // the count it was sent for. That it actually called a tool is the claim worth making — an answer
        // invented from nothing would pass a "reply is non-empty" assertion.
        var client = ClientOrNull();
        if (client is null) Assert.Inconclusive($"Set {KeyVariable} to run this against a real model.");

        await using var session = new LiveSession(client!);

        await session.Host.RunAsync(DispatchInstruction);

        var id = session.SubAgents.Snapshot.FirstOrDefault()?.Id;
        if (id is null)
            Assert.Inconclusive("The model did not dispatch a child this time; ARealModel_DispatchesAChildAtAll is the test that reports on that.");

        // Waited for here rather than by the model, so the assertion is about the child and not about whether
        // the model chose to poll. The child's own turn is a real call on the same key.
        var reply = JObject.Parse(SubAgentFixture.InvokeTool(
            SubAgentFixture.SubAgentToolOf(session.Scope, "WaitSubAgents"),
            ("ids", new[] { id }), ("timeoutMs", 180_000)));

        var agent = ((JArray)reply["agents"]!).Single();
        Assert.AreEqual("Completed", (string?)agent["state"], (string?)agent["error"] ?? (string?)agent["stateText"]);
        StringAssert.Contains((string?)agent["result"] ?? string.Empty, "1",
            "the graph has exactly one node, so a child that looked says one");
        Assert.IsTrue((int?)agent["callCount"] > 0,
            "and it looked: a child that reported without calling a tool answered from nothing");
    }

    /// <summary>
    /// The host agent and the sub-agent subsystem over one real client, assembled the way a host would.
    /// <para>
    /// Held in one place because both live tests need exactly this and differ only in what they ask. The
    /// graph is one node, so "how many nodes" has a known answer.
    /// </para>
    /// </summary>
    private sealed class LiveSession : IAsyncDisposable
    {
        public LiveSession(IChatClient client)
        {
            Tree = new TreeDefaultViewModel();
            Tree.GetHelper().CreateNode(new NodeDefaultViewModel());

            Scope = Tree.AsAgentScope().WithMaxToolCalls(60);
            SubAgents = SubAgentScope.ForClient(client);
            Scope.WithSubAgents(SubAgents);

            Host = client.AsAIAgent(new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = "You are an assistant working on a workflow graph. Use the tools you are given.",
                },
                AIContextProviders = Scope.CreateContextProviders(),
            });
        }

        public TreeDefaultViewModel Tree { get; }
        public WorkflowAgentScope Scope { get; }
        public SubAgentScope SubAgents { get; }
        public AIAgent Host { get; }

        public ValueTask DisposeAsync() => SubAgents.DisposeAsync();
    }

    private static IChatClient? ClientOrNull()
    {
        var key = Environment.GetEnvironmentVariable(KeyVariable);
        if (string.IsNullOrWhiteSpace(key)) return null;

        return new OpenAIClient(
            new ApiKeyCredential(key),
            new OpenAIClientOptions { Endpoint = new Uri(Endpoint) })
            .GetChatClient(Model)
            .AsIChatClient();
    }
}
