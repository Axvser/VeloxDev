using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The invariant the whole subsystem exists for: a child's abilities are a <i>narrowing</i> of its parent's,
/// never a widening, and whatever a spawn asked for and did not get is said out loud.
/// <para>
/// The reporting half matters as much as the narrowing half. A model that asked for a tool, believed it had
/// it, and planned around it will not notice the absence until it has already wasted the run — so the
/// dropped list is asserted here against what the child <i>actually</i> holds, not merely against the
/// request.
/// </para>
/// </summary>
[TestClass]
public class SubAgentNarrowingTests
{
    [TestMethod]
    public async Task ARequestTheParentCannotHonour_IsDroppedAndReported()
    {
        await using var fx = new SubAgentFixture();
        fx.Scope.WithToolEnabled("DeleteNode", false);

        var id = fx.Spawn("count the nodes",
            ("allowedTools", new[] { "ListNodes", "DeleteNode", "NoSuchToolExists" }));
        var row = fx.RowOf(id);

        CollectionAssert.Contains(fx.RowVm(id).GrantedTools.ToArray(), "ListNodes");
        Assert.IsTrue(row.DroppedRequests.Any(d => d.Contains("DeleteNode")),
            "a tool the host switched off must be reported as refused, not silently omitted");
        Assert.IsTrue(row.DroppedRequests.Any(d => d.Contains("NoSuchToolExists")),
            "a tool that does not exist must be reported too — the model cannot tell the two apart otherwise");
        Assert.IsFalse(SubAgentFixture.SurfaceOf(fx.ChildScope(id)).Contains("DeleteNode"));
    }

    [TestMethod]
    public async Task WhatTheSpawnReported_IsExactlyWhatTheChildHolds()
    {
        // The strongest form of the invariant: the granted names are compared with the surface the child's
        // own toolkit offers, so a grant the server believes it made but did not apply cannot pass.
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(ui: ui);

        var id = fx.Spawn("do the thing",
            ("allowedTools", new[] { "ListNodes", "GetWorkflowSummary", "CreateNode" }));
        var reported = fx.RowVm(id).GrantedTools;
        var actual = SubAgentFixture.SurfaceOf(fx.ChildScope(id));

        CollectionAssert.AreEquivalent(reported.ToArray(), actual.ToArray(),
            "the granted list and the child's real surface must be the same set");
        Assert.IsTrue(actual.Contains("CreateNode"), "an explicitly named mutation tool is granted");
    }

    [TestMethod]
    public async Task InheritingTheSurface_MeansTheReadOnlyHalf()
    {
        // Omitting the whitelist is the common case, so its default is the one that has to be safe: a child
        // that was never asked to change anything must not be able to.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("just look at it");
        var granted = fx.RowVm(id).GrantedTools;

        CollectionAssert.Contains(granted.ToArray(), "ListNodes");
        CollectionAssert.DoesNotContain(granted.ToArray(), "CreateNode");
        CollectionAssert.DoesNotContain(granted.ToArray(), "DeleteNode");
        CollectionAssert.DoesNotContain(granted.ToArray(), "ExecuteNode");
        CollectionAssert.DoesNotContain(granted.ToArray(), "ExecuteCommandOnNode");

        CollectionAssert.AreEquivalent(
            granted.ToArray(), SubAgentFixture.SurfaceOf(fx.ChildScope(id)).ToArray());
    }

    [TestMethod]
    public async Task AnEmptyWhitelist_IsNotTheSameThingAsOmittingIt()
    {
        // "Inherit" and "grant nothing" are different requests and the model can express both; collapsing
        // them would make the narrowest possible request impossible to make.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("think, do not touch", ("allowedTools", Array.Empty<string>()));

        Assert.AreEqual(0, fx.RowOf(id).GrantedToolCount);
        Assert.AreEqual(0, SubAgentFixture.SurfaceOf(fx.ChildScope(id)).Count,
            "an empty whitelist leaves the child with no workflow tools at all");
    }

    [TestMethod]
    public async Task TheResetTool_IsNeverGranted_EvenWhenNamed()
    {
        // The one capability no child may hold. It is reported as dropped rather than silently withheld,
        // and the grant list must not name it — a grant the child cannot use is worse than no grant.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("do the thing", ("allowedTools", new[] { "ListNodes", WorkflowAgentToolkit.ResetBudgetToolName }));
        var row = fx.RowOf(id);

        CollectionAssert.DoesNotContain(fx.RowVm(id).GrantedTools.ToArray(), WorkflowAgentToolkit.ResetBudgetToolName);
        Assert.IsTrue(row.DroppedRequests.Any(d => d.Contains(WorkflowAgentToolkit.ResetBudgetToolName)),
            "the refusal has to be reported, or the model will keep trying to widen its own budget");
        Assert.IsFalse(SubAgentFixture.SurfaceOf(fx.ChildScope(id)).Contains(WorkflowAgentToolkit.ResetBudgetToolName));

        // The parent keeps it: the escape hatch belongs to the agent holding the conversation, not to the
        // ones it dispatched, and nothing about narrowing may take it from the parent.
        CollectionAssert.Contains(
            SubAgentFixture.SurfaceOf(fx.Scope).ToArray(), WorkflowAgentToolkit.ResetBudgetToolName);
    }

    [TestMethod]
    public async Task ARequestedBudgetBeyondTheRemainder_IsClampedAndReported()
    {
        await using var fx = new SubAgentFixture(maxToolCalls: 10);

        var id = fx.Spawn("the thing", ("maxToolCalls", 20));
        var row = fx.RowOf(id);

        // The parent has 10 and has spent none of them yet; one is kept back for the parent's own next call,
        // which is the arithmetic that makes an unbounded depth terminate.
        Assert.AreEqual(9, row.MaxToolCalls);
        Assert.IsTrue(row.DroppedRequests.Any(d => d.Contains("maxToolCalls")),
            "a clamped budget must be reported — a child that thinks it has 20 stops expecting to be cut off");
    }

    [TestMethod]
    public async Task AnUncappedParent_StillYieldsAFiniteGrant()
    {
        // The hole that would otherwise make "arbitrary depth" mean "unbounded depth": with no cap on the
        // parent, "what the parent has left" is undefined, and the strictly-decreasing grant disappears.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("the thing");

        Assert.AreEqual(fx.SubAgents.SpawnBudget - 1, fx.RowOf(id).MaxToolCalls,
            "the spawn budget stands in for a cap the host never set, so the grant is always finite");
    }

    [TestMethod]
    public async Task Siblings_EachGetTheRemainder_RatherThanAShareOfIt()
    {
        // A grant bounds a child; it does not reserve anything. Dividing the pot among siblings would make
        // the second spawn cheaper than the first for no reason the model could see.
        await using var fx = new SubAgentFixture(maxToolCalls: 64);

        var first = fx.Spawn("first");
        var second = fx.Spawn("second");

        Assert.AreEqual(63, fx.RowOf(first).MaxToolCalls);
        Assert.AreEqual(62, fx.RowOf(second).MaxToolCalls,
            "the second spawn sees one call already spent — the spawn itself — and nothing else");

        Assert.IsTrue(fx.RowOf(second).MaxToolCalls > 30,
            "siblings are bounded individually, not handed half the pot each");
    }

    [TestMethod]
    public async Task WithNoUIContext_MutationToolsAreDroppedAndQueriesAreKept()
    {
        // A background child edits the graph on a thread the parent's UI never serialized with, and the only
        // serialization this library has is the marshalling the UI context provides. So the gate is
        // structural, not a warning in a prompt.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("edit the graph", ("allowedTools", new[] { "ListNodes", "CreateNode" }));
        var row = fx.RowOf(id);

        CollectionAssert.Contains(fx.RowVm(id).GrantedTools.ToArray(), "ListNodes");
        Assert.IsTrue(row.DroppedRequests.Any(d => d.Contains("CreateNode") && d.Contains("UI")),
            "the drop reason has to name the missing UI context, or it reads as an arbitrary refusal");
        CollectionAssert.DoesNotContain(fx.RowVm(id).GrantedTools.ToArray(), "CreateNode");
    }

    [TestMethod]
    public async Task WithAUIContext_NamedMutationToolsAreGranted()
    {
        // The same request, against a host that did register a UI context: the gate is about the host's
        // configuration, not about mutation being forbidden to children in general.
        using var ui = new CountingUIContext();
        await using var fx = new SubAgentFixture(ui: ui);

        var id = fx.Spawn("edit the graph", ("allowedTools", new[] { "ListNodes", "CreateNode" }));

        CollectionAssert.Contains(fx.RowVm(id).GrantedTools.ToArray(), "CreateNode");
        Assert.AreEqual(0, fx.RowOf(id).DroppedRequests.Count);
    }

    [TestMethod]
    public async Task ACapTheParentDoesNotHave_CannotBeHandedDown()
    {
        // Read and write caps are inherited silently when the request is silent and clamped when it is not;
        // a child can never end up with a wider cap than its parent, whatever it asks for.
        await using var fx = new SubAgentFixture(maxToolCalls: 30);
        fx.Scope.WithMaxWriteToolCalls(2);

        var id = fx.Spawn("the thing", ("maxWriteToolCalls", 50));
        var row = fx.RowOf(id);

        Assert.IsTrue(row.DroppedRequests.Any(d => d.Contains("maxWriteToolCalls")));
        Assert.AreEqual(2, fx.ChildScope(id).MaxWriteToolCalls);
    }

    [TestMethod]
    public async Task NodeExecution_IsOptInOnBothSides()
    {
        // A capability the parent does not hold cannot be handed down, and asking is not an error — it is
        // a request that has to be answered honestly.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("run something", ("allowNodeExecution", true));
        var row = fx.RowOf(id);

        Assert.IsTrue(row.DroppedRequests.Any(d => d.Contains("allowNodeExecution")));
        Assert.IsFalse(fx.ChildScope(id).AllowNodeExecution);
    }

    [TestMethod]
    public async Task ASpawnWithNoTask_IsRefusedBeforeAnythingIsCreated()
    {
        await using var fx = new SubAgentFixture();

        var reply = JObject.Parse(fx.Invoke("SpawnSubAgent", ("task", "   ")));

        Assert.AreEqual("refused", (string?)reply["status"]);
        Assert.AreEqual(0, fx.Rows.Count, "a refused spawn leaves no row behind");
    }

    [TestMethod]
    public async Task AToolTheParentHasSwitchedOff_IsNotInherited()
    {
        // The sharp form of the narrowing rule, and the one nothing else would catch: nobody named this
        // tool, so it reaches the child only through the inherit path — and a child holding a capability its
        // parent had turned off is a widening, not a narrowing.
        await using var fx = new SubAgentFixture();
        fx.Scope.WithToolEnabled("MarkDirty", false);

        var id = fx.Spawn("just look at it");

        Assert.IsFalse(SubAgentFixture.SurfaceOf(fx.ChildScope(id)).Contains("MarkDirty"));
        Assert.IsTrue(fx.ChildScope(id).IsToolEnabled("ListNodes"),
            "and the switch is specific: the rest of the surface comes through untouched");
    }

    [TestMethod]
    public async Task TheParentKeepsEverythingItHad()
    {
        // Narrowing a child must not narrow the parent. The switches are applied to the new scope, and the
        // one shared object — the accounting ledger — has no bearing on which tools exist.
        await using var fx = new SubAgentFixture();
        var before = SubAgentFixture.SurfaceOf(fx.Scope);

        fx.Spawn("the thing", ("allowedTools", new[] { "ListNodes" }));

        CollectionAssert.AreEquivalent(before.ToArray(), SubAgentFixture.SurfaceOf(fx.Scope).ToArray());
    }
}
