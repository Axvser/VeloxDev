using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The five questions no offline double can answer.
/// <para>
/// Every other test in this folder drives the machinery with a fake model, which proves the machinery works
/// but says nothing about whether a model will use it. Only a real one can settle that: whether the tool
/// descriptions are clear enough that an agent dispatches a child at all, whether a child with a narrowed
/// tool set can actually do the work it was handed and report back, whether a model asked to narrow a
/// capability populates the argument that carries it rather than letting the default stand, whether it
/// titles the task it delegates — the one argument whose reader is a person rather than the model — and,
/// the one the standing text exists for, whether it delegates read-heavy work on its own initiative.
/// </para>
/// <para>
/// Gated on <c>API_KEY_DEEPSEEK</c>: without it these are <c>Inconclusive</c>, so a machine that holds no
/// key stays green rather than red. When they do run they cost a handful of model calls, so they are kept
/// to one turn of the host agent each rather than a suite.
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

    [TestMethod]
    public async Task ARealModel_PassesTheNarrowingOnRatherThanIgnoringIt()
    {
        // The third question, and the newest: the two capability axes a spawn can now name are only worth
        // having if a model populates them. A description that reads well to a person can still leave a model
        // omitting the argument and taking the default — which for both axes is now "everything the parent
        // has". The two halves are told apart by the grant, so one turn settles it.
        var client = ClientOrNull();
        if (client is null) Assert.Inconclusive($"Set {KeyVariable} to run this against a real model.");

        var skills = new VeloxDev.AI.Skills.SkillScope()
            .WithSource(new VeloxDev.AI.Skills.EmbeddedSkillSource("Workflow"));
        skills.Refresh();
        var only = skills.Names[0];

        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new NodeDefaultViewModel());

        var scope = tree.AsAgentScope().WithMaxToolCalls(60);
        scope.WithSkills(skills);
        var subAgents = SubAgentScope.ForClient(client!);
        scope.WithSubAgents(subAgents);

        var host = client!.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Instructions = "You are an assistant working on a workflow graph. Use the tools you are given.",
            },
            AIContextProviders = scope.CreateContextProviders(),
        });

        await using (subAgents)
        {
            await host.RunAsync(
                $"Dispatch a background sub-agent to summarise `{only}` for you, and allow it to read that one "
                + "skill and no other. Do not summarise it yourself and do not wait for the child.");

            var row = subAgents.Snapshot.FirstOrDefault();
            if (row is null)
                Assert.Inconclusive("The model did not dispatch a child this time; ARealModel_DispatchesAChildAtAll is the test that reports on that.");

            Assert.AreEqual(1, row.GrantedSkillCount,
                "the spawn named one skill; a model that omitted `allowedSkills` would have granted all "
                + $"{skills.Names.Count} of them, and that is the failure this asserts against");
        }
    }

    [TestMethod]
    public async Task ARealModel_TitlesTheTaskItDelegates()
    {
        // The title is the one argument whose reader is neither the model nor the child but the person
        // watching the panel, so no feedback loop can teach it: nothing the model observes says that
        // "node-counter" reads badly on a row a human scans. Offline, both halves are provable and proven —
        // the fallback is a number per parent (AChildWithNoTitle_FallsBackToANumberUnderItsOwnParent) and the
        // standing text asks for a title (TheStandingText_AsksForATitle_BecauseThePanelShowsOne). What neither
        // can answer is whether a model reading that text actually puts one in, and an omitted optional
        // argument is silent rather than an error — the same silence the narrowing test above is aimed at.
        var client = ClientOrNull();
        if (client is null) Assert.Inconclusive($"Set {KeyVariable} to run this against a real model.");

        await using var session = new LiveSession(client!);

        await session.Host.RunAsync(DispatchInstruction);

        var row = session.SubAgents.Snapshot.FirstOrDefault();
        if (row is null)
            Assert.Inconclusive("The model did not dispatch a child this time; ARealModel_DispatchesAChildAtAll is the test that reports on that.");

        Assert.IsFalse(row.Name.StartsWith("子代理 ", StringComparison.Ordinal),
            "the model left `name` out, so the panel is showing a number where the title should be. The "
            + "argument is being skipped, not misused — which is why it is asked for in the standing text "
            + "and not only in the parameter description.");
        Assert.IsTrue(row.Name.Length <= 60,
            $"a title is what the panel reads at a glance, so a sentence defeats the point. Got: {row.Name}");
    }

    [TestMethod]
    public async Task ARealModel_DelegatesAReadHeavyTask_WithoutBeingToldTo()
    {
        // Requirement 1, and the only test that can settle it. The mandate lives in the standing paragraph the
        // sub-agent provider writes, so whether a model obeys it is a question about that prose — and no
        // offline double can answer it, a double reading the prompt no more than it reads the tool
        // descriptions.
        //
        // The corpus is built here rather than borrowed from the skill library, and that is deliberate: a
        // skill's listing carries a description of each document, so "summarise the library" is answerable in
        // one call and measures nothing. Six chapters whose one useful word sits in the middle of three
        // hundred filler lines cannot be answered without six reads, so the model's choice is the one the
        // mandate is about — six calls in its own context, or one child.
        var client = ClientOrNull();
        if (client is null) Assert.Inconclusive($"Set {KeyVariable} to run this against a real model.");

        var chapters = new[] { "alpha", "bravo", "charlie", "delta", "echo", "foxtrot" };
        var markers = chapters.ToDictionary(c => c, c => $"core-{c}-{c.Length}");

        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new NodeDefaultViewModel());

        var scope = tree.AsAgentScope().WithMaxToolCalls(80);
        scope.WithTools("Reference chapters of the operating manual, one call each.",
            AIFunctionFactory.Create((string name) => Chapter(name, markers), "ReadChapter"));
        var subAgents = SubAgentScope.ForClient(client!);
        scope.WithSubAgents(subAgents);

        var host = client!.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Instructions = "You are an assistant working on a workflow graph. Use the tools you are given.",
            },
            AIContextProviders = scope.CreateContextProviders(),
        });

        await using (subAgents)
        {
            var listed = string.Join(", ", chapters);
            var reply = await host.RunAsync(
                $"The operating manual has six chapters: {listed}. Each one hides a single core token in its "
                + "middle. Tell me just those six tokens, one per line — nothing else, and I do not want the "
                + "chapters themselves.");

            // What the model did instead, when it did not delegate: a failure that says only "no child was
            // dispatched" leaves the next reader to reproduce the run to learn whether the model read the
            // chapters itself, never read them at all, or hit a wall.
            var (toolCalls, readCalls, _) = scope.CreateToolkit().CallUsage;
            Assert.AreNotEqual(0, subAgents.Snapshot.Count,
                "six chapters whose only useful content is one buried token is exactly the kind of work the "
                + "standing text says must be dispatched rather than done in the model's own context — it "
                + "reads a great deal and concludes in six words. If this fails, the mandate is not strong "
                + "enough: the offline suite already proves the tools work when they are called, so nothing "
                + "but the wording can be at fault.\n"
                + $"Instead it spent {toolCalls} tool call(s), {readCalls} of them reads, and answered: "
                + $"{reply.Text}");
        }
    }

    /// <summary>
    /// One chapter of the synthetic manual: filler with the one useful token buried in the middle, so the
    /// token cannot be reached without reading the chapter and no call can answer for two.
    /// </summary>
    private static string Chapter(string name, IReadOnlyDictionary<string, string> markers)
    {
        var lines = new List<string>();
        for (var i = 0; i < 300; i++)
            lines.Add($"{name} line {i}: the procedure continues as it does everywhere else in this manual.");

        lines.Insert(150, $"**{name} core**: {markers[name]}");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The host agent and the sub-agent subsystem over one real client, assembled the way a host would.
    /// <para>
    /// Held in one place because the first two live tests need exactly this and differ only in what they ask.
    /// The graph is one node, so "how many nodes" has a known answer. The two in the middle build their own
    /// scopes instead: the narrowing one needs a skill source attached before the sub-agent subsystem is, and
    /// the read-heavy one needs a corpus whose size makes reading it in one context the wrong choice —
    /// neither has room here.
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
