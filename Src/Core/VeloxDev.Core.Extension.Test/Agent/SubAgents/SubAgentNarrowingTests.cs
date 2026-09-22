using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The invariant the whole subsystem exists for: a child's abilities are its parent's own, or fewer — never
/// more — and whatever a spawn asked for and did not get is said out loud.
/// <para>
/// "Its parent's own" is the default and the whole of it: omitting a field means inherit, so a spawn that
/// names nothing gets the parent's entire surface. Naming tools is the only way to take anything away, and it
/// is the only reason the report below has anything to report.
/// </para>
/// <para>
/// The reporting half matters as much as the granting half. A model that asked for a tool, believed it had
/// it, and planned around it will not notice the absence until it has already wasted the run — so the
/// dropped list is asserted here against what the child <i>actually</i> holds, not merely against the
/// request. Its mirror image is the defect this replaced: a name the parent really did hold, reported as
/// "not available to this agent", which taught the model that asking was futile.
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
        var actual = SubAgentFixture.FullSurfaceOf(fx.ChildScope(id));

        CollectionAssert.AreEquivalent(reported.ToArray(), actual.ToArray(),
            "the granted list and the child's real surface must be the same set");
        Assert.IsTrue(actual.Contains("CreateNode"), "an explicitly named mutation tool is granted");
    }

    [TestMethod]
    public async Task InheritingTheSurface_MeansTheParentsOwn()
    {
        // Omitting the whitelist is the common case, so its default is the one that has to be stated exactly:
        // the child gets the parent's whole surface, mutation tools included. There is no read-only half any
        // more and no structural gate behind the surface — a whitelist is the only way to take anything away,
        // and a child is left unable to change the graph only when its dispatcher said so on purpose.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("just look at it");
        var granted = fx.RowVm(id).GrantedTools;

        CollectionAssert.Contains(granted.ToArray(), "ListNodes");
        CollectionAssert.Contains(granted.ToArray(), "CreateNode");
        CollectionAssert.Contains(granted.ToArray(), "DeleteNode");
        CollectionAssert.Contains(granted.ToArray(), "ExecuteNode");
        CollectionAssert.Contains(granted.ToArray(), "ExecuteCommandOnNode");
        CollectionAssert.Contains(granted.ToArray(), WorkflowAgentToolkit.ResetBudgetToolName,
            "the reset tool is on the parent's surface, so it is on its child's");

        Assert.AreEqual(0, fx.RowOf(id).DroppedRequests.Count,
            "a silent spawn asks for nothing, so there is nothing to refuse it");

        // The five management tools are part of that surface — a child dispatched without a whitelist can
        // dispatch one of its own, and it has to be able to: a child that only delegates when its parent
        // remembered to name the tool is a child that never delegates.
        CollectionAssert.IsSubsetOf(SubAgentAgentToolkit.ToolNames, granted.ToArray());

        // Compared against the whole surface, not the workflow toolkit alone: the report has to cover the
        // tools the child gets from its own providers too, or "exactly what it holds" is measured against a
        // surface that was never the whole of it.
        CollectionAssert.AreEquivalent(
            granted.ToArray(), SubAgentFixture.FullSurfaceOf(fx.ChildScope(id)).ToArray());
    }

    [TestMethod]
    public async Task TheSubAgentTools_CanBeNamed_InAWhitelist()
    {
        // The other end of the same defect. The five were missing from the list a grant draws on, so naming
        // one was answered with "not available to this agent" — which is false, and which leaves the model
        // concluding that delegation is a thing it cannot ask for.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("count the nodes", ("allowedTools", new[] { "ListNodes", "SpawnSubAgent" }));
        var row = fx.RowOf(id);

        CollectionAssert.Contains(fx.RowVm(id).GrantedTools.ToArray(), "SpawnSubAgent");
        Assert.IsFalse(row.DroppedRequests.Any(d => d.Contains("SpawnSubAgent")),
            "a tool the parent holds and did not switch off is not a refusal");
        CollectionAssert.Contains(
            SubAgentFixture.SubAgentSurfaceOf(fx.ChildScope(id)).ToArray(), "SpawnSubAgent",
            "and the grant is a fact about the child, not only about the row");

        // Naming a whitelist that leaves them out still takes them away — the grant is a list either way.
        var narrow = fx.Spawn("count the nodes", ("allowedTools", new[] { "ListNodes" }));
        CollectionAssert.DoesNotContain(
            SubAgentFixture.SubAgentSurfaceOf(fx.ChildScope(narrow)).ToArray(), "SpawnSubAgent");
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
    public async Task WithNoUIContext_TheSurfaceIsStillTheParentsOwn()
    {
        // The gate that used to sit here is gone, and this pins its absence rather than leaving it to be
        // re-invented. It read well — a background child editing a graph nothing has serialized with is a race
        // — but it answered a request for a tool the parent held with a refusal, which is the shape the "hand
        // it down as it stands" rule forbids whatever the reason for it. The serialization is still supplied
        // where there is one: the child inherits the host's context.
        await using var fx = new SubAgentFixture();

        var id = fx.Spawn("edit the graph", ("allowedTools", new[] { "ListNodes", "CreateNode" }));
        var row = fx.RowOf(id);

        CollectionAssert.Contains(fx.RowVm(id).GrantedTools.ToArray(), "CreateNode");
        Assert.AreEqual(0, row.DroppedRequests.Count, "nothing is refused for want of a UI context");
        CollectionAssert.Contains(
            SubAgentFixture.SurfaceOf(fx.ChildScope(id)).ToArray(), "CreateNode",
            "and the grant is a fact about the child, not only about the row");
    }

    [TestMethod]
    public async Task TheUIContext_ChangesNothingAboutWhatIsGranted()
    {
        // The pair that stood here asserted that a UI context decided whether mutation tools were granted. It
        // no longer does: the context decides where a call runs, never whether the child may make it — so the
        // same request against the two hosts has to come out the same.
        using var ui = new CountingUIContext();
        await using var withUi = new SubAgentFixture(ui: ui);
        await using var withoutUi = new SubAgentFixture();

        var a = withUi.Spawn("edit the graph", ("allowedTools", new[] { "ListNodes", "CreateNode" }));
        var b = withoutUi.Spawn("edit the graph", ("allowedTools", new[] { "ListNodes", "CreateNode" }));

        CollectionAssert.AreEquivalent(
            withUi.RowVm(a).GrantedTools.ToArray(), withoutUi.RowVm(b).GrantedTools.ToArray());
        Assert.AreEqual(0, withUi.RowOf(a).DroppedRequests.Count);
        Assert.AreEqual(0, withoutUi.RowOf(b).DroppedRequests.Count);
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
