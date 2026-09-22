using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Skills;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The two capability axes that a list of tool names cannot express: skills and MCP servers.
/// <para>
/// Neither is contributed by the workflow toolkit — each arrives with a context provider of its own, out of
/// a data layer of its own — so granting them is not a matter of switching tools on and off. The child is
/// given a <i>view</i> of the parent's source, and the claim under test is that the view is the boundary:
/// what the child can reach through it is exactly what the spawn granted, and asking for more fails inside
/// the child rather than being a rule it was merely told to respect.
/// </para>
/// <para>
/// All three axes share one default now: silence means inherit, and inheriting means the parent's own
/// switched-on set. What differs between them is only how a <i>named</i> grant is taken away — by name for a
/// skill, by the <c>server/tool</c> key the source is actually switched on for an MCP tool.
/// </para>
/// <para>
/// The third axis is the one that was already a list — custom tools registered through <c>WithTools</c> —
/// but which the toolkit's whitelist could not actually reach until the groups they were registered in
/// became inheritable. A grant that named one used to be silently empty.
/// </para>
/// </summary>
[TestClass]
public class SubAgentCapabilityGrantTests
{
    private const string System = "Workflow";

    /// <summary>A skill scope over the library's own embedded corpus — real, deterministic, and seven deep.</summary>
    private static SkillScope EmbeddedSkills()
    {
        var skills = new SkillScope().WithSource(new EmbeddedSkillSource(System));
        skills.Refresh();
        return skills;
    }

    /// <summary>An MCP scope with two connected servers, faked the way <c>McpServerSwitchTests</c> does it.</summary>
    private static McpScope TwoServers()
    {
        var mcp = new McpScope();
        mcp.SeedLoadedTools("alpha", [AIFunctionFactory.Create(() => "ok", "alpha_read")]);
        mcp.SeedLoadedTools("beta", [AIFunctionFactory.Create(() => "ok", "beta_read")]);
        return mcp;
    }

    /// <summary>One of the child's own provider tools, invoked as the model would.</summary>
    private static string InvokeProviderTool(
        WorkflowAgentScope child, string toolName, params (string Name, object? Value)[] args)
        => SubAgentFixture.InvokeTool(SubAgentFixture.ProviderToolOf(child, toolName), args);

    // ── Skills ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AGrantedSkill_ReachesTheChild_AndADeniedOneDoesNot()
    {
        var skills = EmbeddedSkills();
        var all = skills.Names;
        var granted = all.Take(2).ToArray();
        var denied = all.Skip(2).ToArray();

        await using var fx = new SubAgentFixture(skills: skills);

        var id = fx.Spawn("read some documentation", ("allowedSkills", granted));

        var childSkills = fx.ChildScope(id).Skills
            ?? throw new AssertFailedException("the child was granted skills but has no skill source");
        CollectionAssert.AreEquivalent(granted, childSkills.Names.ToArray());

        // Reached the way the model reaches it, not through the view's own bookkeeping: a view that
        // reported itself as narrowed while still offering everything would pass the line above and fail
        // this one.
        var listed = JObject.Parse(InvokeProviderTool(fx.ChildScope(id), "ListSkills"));
        var visible = listed["skills"]!.Select(s => (string)s["name"]!).ToArray();
        CollectionAssert.AreEquivalent(granted, visible);

        var refusal = JObject.Parse(InvokeProviderTool(fx.ChildScope(id), "load_skill", ("skillName", denied[0])));
        Assert.AreNotEqual("ok", (string?)refusal["status"],
            "a skill the child was not granted must fail inside the child, not merely be undisplayed");
    }

    [TestMethod]
    public async Task AGrantedSkill_IsAView_NotAFilterOnTheParent()
    {
        // The narrowing has to be one-way. Two children with different grants are served by the same parent
        // scope, so a filter applied to the shared source would leave the second child with the first's list.
        var skills = EmbeddedSkills();
        var all = skills.Names;

        await using var fx = new SubAgentFixture(skills: skills);

        var first = fx.Spawn("read A", ("allowedSkills", new[] { all[0] }));
        var second = fx.Spawn("read B", ("allowedSkills", new[] { all[1] }));

        CollectionAssert.AreEquivalent(new[] { all[0] }, fx.ChildScope(first).Skills!.Names.ToArray());
        CollectionAssert.AreEquivalent(new[] { all[1] }, fx.ChildScope(second).Skills!.Names.ToArray());
        CollectionAssert.AreEquivalent(all.ToArray(), fx.Scope.Skills!.Names.ToArray(),
            "the parent's own skill list is untouched by what it granted away");
    }

    [TestMethod]
    public async Task ASkillTheParentSwitchedOff_IsNotGrantable()
    {
        var skills = EmbeddedSkills();
        var withdrawn = skills.Names[0];
        skills.Disable(withdrawn);

        await using var fx = new SubAgentFixture(skills: skills);

        var id = fx.Spawn("read it anyway", ("allowedSkills", new[] { withdrawn }));

        Assert.IsTrue(fx.RowOf(id).DroppedRequests.Any(d => d.Contains(withdrawn)),
            "a skill the parent has switched off must be reported as refused");
        Assert.IsNull(fx.ChildScope(id).Skills,
            "and with nothing granted, there is no skill source to attach at all");
    }

    [TestMethod]
    public async Task OmittingTheSkillList_InheritsWhatTheParentHasSwitchedOn()
    {
        // Skills are knowledge rather than power — every skill tool is read-only — so the default is the
        // parent's whole switched-on set, not the empty one.
        var skills = EmbeddedSkills();
        var on = skills.Names.ToArray();
        skills.Disable(on[0]);
        var expected = on.Skip(1).ToArray();

        await using var fx = new SubAgentFixture(skills: skills);

        var id = fx.Spawn("read whatever is relevant");

        CollectionAssert.AreEquivalent(expected, fx.ChildScope(id).Skills!.Names.ToArray());
        Assert.AreEqual(expected.Length, fx.RowOf(id).GrantedSkillCount);
    }

    [TestMethod]
    public async Task AnEmptySkillList_GrantsNone_AndTakesTheSkillToolsWithThem()
    {
        // The distinction the whole subsystem turns on: an omitted argument and an empty one mean different
        // things. Empty is a deliberate "none", and it has to take `load_skill` with it — a child left
        // holding a loader for a source it does not have comes away told it can read skills.
        await using var fx = new SubAgentFixture(skills: EmbeddedSkills());

        var id = fx.Spawn("change the graph, read nothing", ("allowedSkills", Array.Empty<string>()));

        Assert.IsNull(fx.ChildScope(id).Skills, "an empty grant leaves no skill source");
        Assert.IsEmpty(SubAgentFixture.SkillSurfaceOf(fx.ChildScope(id)), "and no skill tools either");

        var granted = fx.RowVm(id).GrantedTools;
        foreach (var name in SkillAgentToolkit.ToolNames)
            CollectionAssert.DoesNotContain(granted.ToArray(), name,
                $"'{name}' operates on skills and the child has none");

        Assert.IsTrue(fx.RowOf(id).DroppedRequests.Any(d => d.Contains("load_skill")),
            "the model has to be told why the tool it can see in its own briefing is gone");
    }

    [TestMethod]
    public async Task ASkillGrant_IsToldToTheChild_AndSoIsAServerOne()
    {
        // The child's own briefing is the only place it can learn its grant from: the sources it reads are
        // narrowed, but a narrowed list of seven skills looks exactly like a complete list of seven skills
        // unless it is told otherwise.
        var skills = EmbeddedSkills();
        var granted = skills.Names.Take(2).ToArray();

        await using var fx = new SubAgentFixture(skills: skills, mcp: TwoServers());

        var id = fx.Spawn("read and connect",
            ("allowedSkills", granted),
            ("allowedMcpServers", new[] { "alpha" }));

        var briefing = SubAgentFixture.PromptOf(fx.ChildScope(id));

        foreach (var name in granted)
            Assert.Contains(name, briefing, $"'{name}' was granted and the child must be told so");
        Assert.Contains("alpha", briefing, "a granted MCP server must be named in the briefing");
        Assert.DoesNotContain(skills.Names[^1], briefing, "a skill that was not granted must not be advertised");
    }

    [TestMethod]
    public async Task WithNoSkillSourceAttached_AGrantIsReportedAsImpossible()
    {
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("read some documentation", ("allowedSkills", new[] { "slot-enumerator" }));

        Assert.IsTrue(fx.RowOf(id).DroppedRequests.Any(d => d.Contains("no skills attached")),
            "the model must be told the agent has no skills rather than that its name was wrong");
        Assert.IsNull(fx.ChildScope(id).Skills);
    }

    // ── MCP ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AGrantedServer_ReachesTheChild_AndAnUngrantedOneDoesNot()
    {
        await using var fx = new SubAgentFixture(mcp: TwoServers());

        var id = fx.Spawn("use the alpha server", ("allowedMcpServers", new[] { "alpha" }));
        var child = fx.ChildScope(id);

        CollectionAssert.AreEquivalent(new[] { "alpha_read" },
            child.Mcp!.LoadedTools.Select(t => t.Name).ToArray());

        var listed = JObject.Parse(InvokeProviderTool(child, McpAgentToolkit.ListName));
        CollectionAssert.AreEquivalent(new[] { "alpha" },
            listed["servers"]!.Select(s => (string)s["name"]!).ToArray(),
            "the child's own inventory must not mention the server it was not given");
    }

    [TestMethod]
    public async Task NamingAnMcpTool_IsNotARefusal()
    {
        // The defect the requirement named, pinned at its exact point. MCP tool names were missing from the
        // list a grant is drawn from, so a model naming one it could plainly see was answered "not available
        // to this agent, or switched off by the host" — neither half of which was true. What it learned from
        // that is that the MCP surface it had been told it owned did not exist.
        await using var fx = new SubAgentFixture(mcp: TwoServers());

        var id = fx.Spawn("read alpha", ("allowedTools", new[] { "ListNodes", "alpha_read" }));
        var row = fx.RowOf(id);

        Assert.IsFalse(row.DroppedRequests.Any(d => d.Contains("alpha_read")),
            $"naming it must not be a refusal, and it was: {string.Join(" | ", row.DroppedRequests)}");
        CollectionAssert.Contains(fx.RowVm(id).GrantedTools.ToArray(), "alpha_read");

        // And the grant is a fact about the child rather than a line in a row: the tool is on the surface its
        // own MCP provider contributes, and switched on where that source keeps its switches.
        CollectionAssert.Contains(SubAgentFixture.McpSurfaceOf(fx.ChildScope(id)).ToArray(), "alpha_read");
        Assert.IsTrue(fx.ChildScope(id).Mcp!.IsToolEnabled("alpha", "alpha_read"));
    }

    [TestMethod]
    public async Task AWhitelistThatOmitsAnMcpTool_TakesItOffTheChildsSurface()
    {
        // The tool-level half of the view. An MCP tool is switched by the `server/tool` pair on the MCP scope
        // and not by name on the workflow one, so a whitelist can only take one away where that key lives.
        // Without the filter the child would be handed every tool of every server it inherited, and the
        // whitelist would be a claim rather than a boundary.
        await using var fx = new SubAgentFixture(mcp: TwoServers());

        var id = fx.Spawn("read alpha only", ("allowedTools", new[] { "ListNodes", "alpha_read" }));
        var surface = SubAgentFixture.McpSurfaceOf(fx.ChildScope(id));

        CollectionAssert.Contains(surface.ToArray(), "alpha_read");
        CollectionAssert.DoesNotContain(surface.ToArray(), "beta_read",
            "the servers come along, but the tools the spawn did not name do not");
    }

    [TestMethod]
    public async Task OmittingTheServerList_InheritsTheParents()
    {
        // The asymmetry that used to live here is gone, and it is worth knowing why it was there: MCP is the
        // axis the framework cannot classify, having no way to tell an MCP read from an MCP write. The old
        // default answered that by inheriting "the read-only half", which for this source was the empty set —
        // so a silent spawn reached its child with no servers at all, which a host observes as its MCP surface
        // quietly emptying one level down. The default is the same as everywhere now.
        await using var fx = new SubAgentFixture(mcp: TwoServers());

        var id = fx.Spawn("just look at the graph");
        var child = fx.ChildScope(id);

        Assert.IsNotNull(child.Mcp, "a silent spawn inherits the servers its parent has connected");
        CollectionAssert.AreEquivalent(new[] { "alpha_read", "beta_read" },
            child.Mcp!.LoadedTools.Select(t => t.Name).ToArray());
        Assert.AreEqual(2, fx.RowOf(id).GrantedMcpServerCount);

        var listed = JObject.Parse(InvokeProviderTool(child, McpAgentToolkit.ListName));
        CollectionAssert.AreEquivalent(new[] { "alpha", "beta" },
            listed["servers"]!.Select(s => (string)s["name"]!).ToArray(),
            "and the child's own inventory says so");
        Assert.AreEqual(0, fx.RowOf(id).DroppedRequests.Count,
            "granting everything by default is not refusing a request the spawn never made");
    }

    [TestMethod]
    public async Task AGrantedServer_ArrivesWithoutTheSwitchesThatWouldChangeIt()
    {
        // A granted server is usable and nothing more. Loading, unloading and adding are decisions about the
        // parent's connections, and a background child is exactly the wrong place to make them — so the
        // child's surface is the granted server's own tools, the list, and the description. Nothing else.
        await using var fx = new SubAgentFixture(mcp: TwoServers());

        var id = fx.Spawn("use the alpha server", ("allowedMcpServers", new[] { "alpha" }));
        var surface = SubAgentFixture.McpSurfaceOf(fx.ChildScope(id));

        CollectionAssert.AreEquivalent(
            new[] { McpAgentToolkit.ListName, McpAgentToolkit.DescribeName, "alpha_read" },
            surface.ToArray());

        foreach (var name in new[] { McpAgentToolkit.ToolNames[1], McpAgentToolkit.ToolNames[2], McpAgentToolkit.AddToolName })
            CollectionAssert.DoesNotContain(surface.ToArray(), name);
    }

    [TestMethod]
    public async Task AServerTheParentSwitchedOff_IsNotGrantable()
    {
        var mcp = TwoServers();
        mcp.SetServerEnabled("beta", false);

        await using var fx = new SubAgentFixture(mcp: mcp);

        var id = fx.Spawn("use beta", ("allowedMcpServers", new[] { "beta" }));

        Assert.IsTrue(fx.RowOf(id).DroppedRequests.Any(d => d.Contains("beta")),
            "a server the parent switched off must be reported as refused");
        Assert.IsNull(fx.ChildScope(id).Mcp);
    }

    [TestMethod]
    public async Task AGrantedServer_OnlyOffersTheToolsTheParentHasSwitchedOn()
    {
        // The child's view is of what the parent can actually offer, not of what the server happens to hold.
        // A tool the parent disabled is one the child must not be able to reach either.
        var mcp = new McpScope();
        mcp.SeedLoadedTools("alpha", [AIFunctionFactory.Create(() => "ok", "alpha_read"), AIFunctionFactory.Create(() => "ok", "alpha_write")]);
        mcp.SetToolEnabled("alpha", "alpha_write", false);

        await using var fx = new SubAgentFixture(mcp: mcp);

        var id = fx.Spawn("read only", ("allowedMcpServers", new[] { "alpha" }));

        CollectionAssert.AreEquivalent(new[] { "alpha_read" },
            fx.ChildScope(id).Mcp!.LoadedTools.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task AGrantedView_OwesNothingToItsParentThatClosingItCouldTakeAway()
    {
        // The view deliberately holds none of the parent's clients or configurations, so tearing the child's
        // MCP surface down must not disconnect the parent — which is the reason a second McpScope is built
        // rather than the parent's own being handed over.
        var mcp = TwoServers();
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(mcp: mcp, ui: ui);

        var id = fx.Spawn("use the alpha server", ("allowedMcpServers", new[] { "alpha" }));
        var view = fx.ChildScope(id).Mcp!;

        await view.DisposeAsync();

        Assert.HasCount(2, mcp.LoadedTools, "the parent's connections and tools are untouched");
        Assert.AreEqual(2, mcp.Status.Snapshot.Count(s => s.State == McpServerStatus.Connected));
    }

    // ── Custom tools ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ACustomToolNamedInTheGrant_ActuallyReachesTheChild()
    {
        // Custom tools live in the workflow toolkit as an unbroken group, so before the groups were made
        // inheritable a grant naming one of them was silently empty — the tool was on neither side's list.
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(
            ui: ui,
            customTools: [AIFunctionFactory.Create(() => "alpha", "NoteAlpha"), AIFunctionFactory.Create(() => "beta", "NoteBeta")]);

        var id = fx.Spawn("take a note", ("allowedTools", new[] { "NoteAlpha" }));
        var child = fx.ChildScope(id);

        CollectionAssert.Contains(fx.RowVm(id).GrantedTools.ToArray(), "NoteAlpha");
        Assert.IsTrue(SubAgentFixture.CustomSurfaceOf(child, ["NoteAlpha"]).Contains("NoteAlpha"),
            "the granted custom tool must be on the child's real surface");
        Assert.IsFalse(SubAgentFixture.CustomSurfaceOf(child, ["NoteBeta"]).Contains("NoteBeta"));
    }

    [TestMethod]
    public async Task TheGuidanceGoesWithTheTools_NotWithTheGroupTheParentRegistered()
    {
        // Custom tools arrive in groups, each with the prompt paragraph explaining it. A child granted one
        // tool of one group must be told how to use that one — and must not be told about a sibling group's
        // tools it does not hold, which is the half a name whitelist could never express.
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(ui: ui);

        fx.Scope.WithTools("Alpha notes are filed under the customer's name.", AIFunctionFactory.Create(() => "a", "NoteAlpha"));
        fx.Scope.WithTools("Beta notes are filed under the ticket id.", AIFunctionFactory.Create(() => "b", "NoteBeta"));

        var id = fx.Spawn("take a note", ("allowedTools", new[] { "NoteAlpha" }));
        var prompt = SubAgentFixture.PromptOf(fx.ChildScope(id));

        Assert.Contains("Alpha notes", prompt, "the child holds the tool, so it must be told how to use it");
        Assert.DoesNotContain("Beta notes", prompt,
            "and it holds nothing from the other group, so that group's guidance must be gone with it");
        Assert.IsFalse(SubAgentFixture.CustomSurfaceOf(fx.ChildScope(id), ["NoteBeta"]).Contains("NoteBeta"));
    }

    [TestMethod]
    public async Task WithNoUiContext_AMutatingCustomToolStillReachesTheChild()
    {
        // The unconditional gate over custom tools has gone the same way as the one over the built-in ones. It
        // was the last place a request the parent could honour was answered with a refusal because of how the
        // host was configured — and a refusal is what a model reads as "this capability does not exist".
        await using var fx = new SubAgentFixture(
            customTools: [AIFunctionFactory.Create(() => "alpha", "NoteAlpha")]);

        var id = fx.Spawn("take a note", ("allowedTools", new[] { "NoteAlpha" }));

        CollectionAssert.Contains(fx.RowVm(id).GrantedTools.ToArray(), "NoteAlpha");
        Assert.AreEqual(0, fx.RowOf(id).DroppedRequests.Count, "a tool the parent holds is not refused");
        Assert.IsTrue(SubAgentFixture.CustomSurfaceOf(fx.ChildScope(id), ["NoteAlpha"]).Contains("NoteAlpha"),
            "and it is on the child's own surface");
    }

    // ── The report ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TheSummary_CarriesEveryAxisOfTheGrant()
    {
        var skills = EmbeddedSkills();
        var grantedSkills = skills.Names.Take(2).ToArray();

        await using var fx = new SubAgentFixture(skills: skills, mcp: TwoServers(), maxToolCalls: 40);

        var id = fx.Spawn("read and connect",
            ("allowedSkills", grantedSkills),
            ("allowedMcpServers", new[] { "alpha" }));

        var row = fx.RowOf(id);

        Assert.AreEqual(grantedSkills.Length, row.GrantedSkillCount);
        Assert.AreEqual(1, row.GrantedMcpServerCount);
        Assert.AreNotEqual(0, row.GrantedToolCount);

        CollectionAssert.AreEquivalent(grantedSkills, fx.RowVm(id).GrantedSkills.ToArray());
        CollectionAssert.AreEquivalent(new[] { "alpha" }, fx.RowVm(id).GrantedMcpServers.ToArray());
    }

    [TestMethod]
    public async Task AHostThatAttachedNothing_IsUnchangedByAllOfThis()
    {
        // The regression that matters most: a scope with no skills, no MCP and no custom tools behaves
        // exactly as it did before any of these axes existed.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("count the nodes");
        var child = fx.ChildScope(id);

        Assert.IsNull(child.Skills);
        Assert.IsNull(child.Mcp);
        Assert.AreEqual(0, fx.RowOf(id).GrantedSkillCount);
        Assert.AreEqual(0, fx.RowOf(id).GrantedMcpServerCount);
        Assert.IsEmpty(SubAgentFixture.SkillSurfaceOf(child));
        Assert.IsEmpty(SubAgentFixture.McpSurfaceOf(child));
        Assert.AreNotEqual(0, fx.RowOf(id).GrantedToolCount, "the workflow surface still arrives, in full");
    }
}
