using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// Depth, and the two places a subsystem's identity has to be right: the context provider's session-state
/// key, and what a spawned agent is told about itself.
/// <para>
/// Arbitrary depth is the setting the host chose, so what matters here is not that deep trees are forbidden
/// but that they terminate and stay legible — every level gets a smaller allowance than the one above, and
/// every agent in the tree knows its own depth and what it was refused.
/// </para>
/// </summary>
[TestClass]
public class SubAgentHierarchyTests
{
    /// <summary>
    /// Spawns through a descendant's own scope, given the path of ids down to it — so a grandchild is
    /// dispatched by the row that owns it and not by the fixture, which is the whole point of the walk.
    /// </summary>
    private static string SpawnFrom(SubAgentFixture fx, string[] path, string task)
    {
        var reply = JObject.Parse(SubAgentFixture.InvokeTool(
            SubAgentFixture.SubAgentToolOf(fx.DescendantScope(path), "SpawnSubAgent"), ("task", task)));

        if ((string?)reply["status"] != "ok")
            throw new InvalidOperationException($"The nested spawn was refused: {reply["message"]}");
        return (string)reply["id"]!;
    }

    [TestMethod]
    public async Task AChildMayDispatchAChildOfItsOwn()
    {
        await using var fx = new SubAgentFixture(client: new InstantChatClient());

        var child = fx.Spawn("the outer job", ("name", "outer"));
        var grand = SpawnFrom(fx, [child], "the inner job");

        var row = fx.ChildSubAgents(child).Snapshot.Single();
        Assert.AreEqual(grand, row.Id);
        Assert.AreEqual(2, row.Depth, "a grandchild of the host's scope is depth 2");
        Assert.AreEqual(child, row.ParentId, "and it names the row that dispatched it, so the tree can be built");
    }

    [TestMethod]
    public async Task TheAllowanceStrictlyDecreases_DownEveryPath()
    {
        // This is the whole of what makes an unbounded depth terminate: whatever else happens, a grant is
        // always smaller than the one above it, so no path can be infinitely long.
        await using var fx = new SubAgentFixture(client: new InstantChatClient(), maxToolCalls: 40);

        var child = fx.Spawn("the outer job");
        var grand = SpawnFrom(fx, [child], "the inner job");
        SpawnFrom(fx, [child, grand], "the innermost job");

        Assert.AreEqual(39, fx.RowOf(child).MaxToolCalls);
        var inner = fx.DescendantSubAgents(child).Snapshot.Single();
        var innermost = fx.DescendantSubAgents(child, grand).Snapshot.Single();

        Assert.IsTrue(inner.MaxToolCalls < 39, $"expected a smaller grant than 39, got {inner.MaxToolCalls}");
        Assert.IsTrue(innermost.MaxToolCalls < inner.MaxToolCalls,
            $"expected a smaller grant than {inner.MaxToolCalls}, got {innermost.MaxToolCalls}");
        Assert.IsTrue(innermost.MaxToolCalls >= 1, "and never zero: a grant below one is a refusal instead");
    }

    [TestMethod]
    public async Task TheDepthLimit_StopsTheGrandchild()
    {
        // The budget is what makes the tree terminate; this is what makes it useful. A host that wants a
        // bounded fan-out sets both, and the refusal has to say which wall it hit.
        await using var fx = new SubAgentFixture(client: new InstantChatClient(), maxDepth: 1);

        var child = fx.Spawn("the outer job");
        var reply = JObject.Parse(SubAgentFixture.InvokeTool(
            SubAgentFixture.SubAgentToolOf(fx.DescendantScope([child]), "SpawnSubAgent"), ("task", "the inner job")));

        Assert.AreEqual("refused", (string?)reply["status"]);
        StringAssert.Contains((string?)reply["message"] ?? string.Empty, "depth",
            "the refusal names the limit, or the model reads it as an arbitrary refusal and retries");
        Assert.AreEqual(0, fx.ChildSubAgents(child).Snapshot.Count, "and nothing was created");
    }

    [TestMethod]
    public async Task AtTheDepthLimit_TheAgentIsToldToDoTheWorkItself()
    {
        // A tool the model can see but never use is a trap. The capability is withdrawn from the wording
        // too, not only from the gate.
        await using var fx = new SubAgentFixture(client: new InstantChatClient(), maxDepth: 1);
        var child = fx.Spawn("the outer job");
        var childScope = fx.ChildScope(child);

        var briefing = childScope.CreateContextProviders()
            .OfType<SubAgentAgentContextProvider>().Single().BuildContext().Instructions ?? string.Empty;

        StringAssert.Contains(briefing, "depth limit");
        StringAssert.Contains(briefing, "yourself");
    }

    [TestMethod]
    public async Task AChildIsToldWhatItIs_AndWhatItDidNotGet()
    {
        // The briefing exists because a child's instructions are the host's: a host that supplied its own
        // preamble would otherwise lose every fact about the spawn. Contributing it per turn survives either
        // choice, and this is that claim tested.
        await using var fx = new SubAgentFixture(client: new InstantChatClient());

        // The sub-agent tools are named explicitly here so the child still holds them: its briefing has to
        // say it may delegate for this test to be about the briefing rather than about the whitelist.
        string[] tools = [.. SubAgentAgentToolkit.ToolNames, "ListNodes", "NoSuchToolExists"];
        var child = fx.Spawn("count the nodes",
            ("name", "counter"),
            ("notes", "the graph is small"),
            ("allowedTools", tools));

        var briefing = InstructionsOf(fx.ChildScope(child));

        StringAssert.Contains(briefing, "sub-agent at depth 1");
        StringAssert.Contains(briefing, "the graph is small", "the dispatcher's notes reach the child");
        StringAssert.Contains(briefing, "NoSuchToolExists",
            "the child is told what was asked for on its behalf and refused — it cannot ask why later");
        StringAssert.Contains(briefing, "may dispatch", "and it knows it may delegate in turn");
    }

    [TestMethod]
    public async Task AChildThatCannotDispatch_IsNotToldItCan()
    {
        // The other half of the briefing being built from the tools the child actually holds: a whitelist
        // that leaves the sub-agent tools out removes them from the gate, and the wording has to go with
        // them. Otherwise the child is invited to delegate and refused when it tries — an invitation it
        // cannot distinguish from a bug in itself.
        await using var fx = new SubAgentFixture(client: new InstantChatClient());

        var child = fx.Spawn("count the nodes", ("allowedTools", new[] { "ListNodes" }));

        var briefing = InstructionsOf(fx.ChildScope(child));

        StringAssert.Contains(briefing, "sub-agent at depth 1", "it is still told what it is");
        Assert.IsFalse(briefing.Contains("may dispatch"),
            "but nothing invites it to delegate, because it holds no tool that could");
        Assert.IsFalse(SubAgentFixture.SubAgentSurfaceOf(fx.ChildScope(child)).Contains("SpawnSubAgent"));
    }

    [TestMethod]
    public async Task TwoScopes_NeverShareAStateKey()
    {
        // The framework throws when two providers attached to one agent share a key, and the keys default to
        // the provider's type name — so the discriminator has to exist. A parent and its child sit on the
        // same tree, which is exactly the pair a tree-derived discriminator would collide on.
        await using var fx = new SubAgentFixture(client: new InstantChatClient());
        var child = fx.Spawn("the job");

        var hostKeys = KeysOf(fx.Scope);
        var childKeys = KeysOf(fx.ChildScope(child));

        Assert.AreEqual(0, hostKeys.Intersect(childKeys, StringComparer.Ordinal).Count(),
            "a parent and its child must not claim the same session state");
    }

    [TestMethod]
    public async Task TwoProvidersOverOneScope_DoShareAKey()
    {
        // The other direction, and the one the framework enforces: two providers over one subsystem would
        // contribute everything twice, so they must collide loudly rather than silently double up.
        var scope = SubAgentScope.ForClient(new InstantChatClient());

        var first = scope.CreateContextProvider().StateKeys;
        var second = scope.CreateContextProvider().StateKeys;

        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
        Assert.AreEqual(1, first.Count, "a provider claims exactly one key per subsystem instance");
    }

    [TestMethod]
    public async Task AnUnattachedSubsystem_ContributesNothing()
    {
        // A host may build the subsystem before attaching it, and the workflow scope's providers already
        // tolerate being read early. This one must too, and silently — the alternative is a throw from
        // inside the framework's prompt rendering, which is unreachable from the host's own code.
        var scope = SubAgentScope.ForClient(new InstantChatClient());
        var provider = (SubAgentAgentContextProvider)scope.CreateContextProvider();

        var context = provider.BuildContext();

        Assert.IsNull(context.Instructions);
        Assert.IsNull(context.Tools);
    }

    [TestMethod]
    public async Task TheSubAgentTools_ArriveOnlyWithTheSubsystem()
    {
        var tree = new VeloxDev.WorkflowSystem.TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new VeloxDev.WorkflowSystem.NodeDefaultViewModel());

        var bare = new WorkflowAgentScope(tree);
        Assert.AreEqual(0, bare.CreateContextProviders().OfType<SubAgentAgentContextProvider>().Count(),
            "a scope nobody attached the subsystem to contributes none of it");

        bare.WithSubAgents(SubAgentScope.ForClient(new InstantChatClient()));

        Assert.AreEqual(1, bare.CreateContextProviders().OfType<SubAgentAgentContextProvider>().Count());

        var contributed = SubAgentFixture.SubAgentSurfaceOf(bare);
        CollectionAssert.AreEquivalent(SubAgentAgentToolkit.ToolNames.ToArray(), contributed.ToArray(),
            "and all five management tools reach the model through it");
    }

    [TestMethod]
    public async Task AChildsToolsAreMarshalledOntoTheHostsThread()
    {
        // A child runs on a thread-pool thread while its parent's turn is suspended on the UI thread. The
        // only thing that keeps their graph edits from interleaving is that both pass through the same
        // context — so a child holding a mutation permission without its parent's context is holding
        // permission to race. The grant is asserted as a fact about the child here, not about the request.
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(
            client: new ToolCallingChatClient("ListNodes"), ui: ui, maxToolCalls: 50);

        var id = fx.Spawn("count them", ("allowedTools", new[] { "ListNodes", "CreateNode" }));
        SubAgentFixture.WaitFor(() => fx.RowOf(id).IsFinished);

        Assert.AreEqual(ui, fx.ChildScope(id).UIContext, "the child marshals onto the host's context");
        Assert.AreEqual(1, fx.RowOf(id).CallCount, "and the marshalled call was the one it made");
        Assert.AreEqual(0, fx.RowOf(id).DroppedRequests.Count, "which the grant promised it");

        foreach (var thread in ui.RanOnThreads)
            Assert.AreEqual(ui.PumpThreadId, thread, "nothing drained on a thread other than the pump");
    }

    private static string InstructionsOf(WorkflowAgentScope scope)
        => scope.CreateContextProviders()
            .OfType<SubAgentAgentContextProvider>().Single().BuildContext().Instructions ?? string.Empty;

    private static IReadOnlyList<string> KeysOf(WorkflowAgentScope scope)
        => [.. scope.CreateContextProviders()
            .OfType<SubAgentAgentContextProvider>()
            .SelectMany(p => p.StateKeys)];
}
