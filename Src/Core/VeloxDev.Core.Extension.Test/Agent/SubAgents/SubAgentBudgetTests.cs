using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The allowance is one pot for the whole tree, not one per agent.
/// <para>
/// "Arbitrary depth" is only safe because of this. Give every child a budget of its own and the parent's
/// superset relation holds only at the instant of the spawn — a parent with 199 left that dispatches three
/// children at 198 each has authorized 594, and depth buys unbounded work. Counting every call, wherever it
/// is made, against the root is what turns "arbitrary depth" into "bounded by the root's allowance".
/// </para>
/// <para>
/// The second half is the escape hatch, which a child now holds. The reset tool is part of the surface like
/// any other, so a child that runs out is sent to it exactly as its parent is — and it reaches the user
/// because the host's interaction configuration travels down with it. What remains a property is that
/// <i>the user</i> decides: the level, the handler and the operation key all have to have been the host's to
/// begin with, and with no handler there is no answer and the reset denies. The cost of the widening is
/// recorded where it belongs — <c>ResetChain</c> walks to the root, so a child asking successfully reopens
/// the whole session rather than its own share; see the memory note on the three constraints.
/// </para>
/// </summary>
[TestClass]
public class SubAgentBudgetTests
{
    private static int Spent(WorkflowAgentScope scope) => scope.CreateToolkit().CallUsage.ToolCalls;

    [TestMethod]
    public async Task AChildsCalls_AreCountedOnTheRootsLedger()
    {
        // A child that spends must show up on its parent's books, or the pot is not shared at all.
        await using var fx = new SubAgentFixture(
            client: new ToolCallingChatClient("ListNodes"), maxToolCalls: 20);

        var id = fx.Spawn("list the nodes", ("allowedTools", new[] { "ListNodes" }), ("maxToolCalls", 5));
        SubAgentFixture.WaitFor(() => fx.RowOf(id).State == SubAgentState.Completed);

        Assert.AreEqual(1, fx.RowOf(id).CallCount, "the child actually reached the tool it was given");
        Assert.AreEqual(2, Spent(fx.Scope),
            "the spawn itself plus the child's one call — both charged to the same root");
    }

    [TestMethod]
    public async Task TheRootsCap_BitesThroughASibling_NotThroughTheChildsOwnShare()
    {
        // The mechanism, stated as arithmetic instead of prose. A grant is always at most one less than the
        // root has left, so a single child runs out of its own share before it can reach the root's wall —
        // the wall is only reachable by a sibling spending first. This is what makes the pot shared rather
        // than merely copied, and it is why the two refusals read differently.
        await using var fx = new SubAgentFixture(maxToolCalls: 6);

        var a = fx.Spawn("first", ("maxToolCalls", 3));
        var b = fx.Spawn("second", ("maxToolCalls", 2));
        Assert.AreEqual(3, fx.RowOf(a).MaxToolCalls);
        Assert.AreEqual(2, fx.RowOf(b).MaxToolCalls);

        // Two spawns have been charged, so the root stands at 2 of 6.
        Assert.AreEqual(2, Spent(fx.Scope));

        for (var i = 0; i < 3; i++) SubAgentFixture.InvokeOn(fx.ChildScope(a), "ListNodes");
        Assert.AreEqual(5, Spent(fx.Scope), "the first child's spending is the tree's spending");

        SubAgentFixture.InvokeOn(fx.ChildScope(b), "ListNodes");
        Assert.AreEqual(6, Spent(fx.Scope));

        // The second child has one call of its own left, but the session has none.
        var refusal = SubAgentFixture.InvokeOn(fx.ChildScope(b), "ListNodes");

        StringAssert.Contains(refusal, "session's tool-call budget",
            "the harder wall must be the one named, or the model reasons about the wrong limit");
        Assert.AreEqual(6, Spent(fx.Scope), "a refused call spends nothing");
    }

    [TestMethod]
    public async Task AChildThatRunsOut_IsSentToTheEscapeHatch_AndToldToReportUpward()
    {
        // A child holds the reset tool, so the refusal may name it — and must, because a refusal with no way
        // out is where the run stops. What is added for a child and not for a root is the duty: someone is
        // suspended on this run's result, and a child that sits waiting for an answer nobody will give wastes
        // that dispatcher's turn.
        await using var fx = new SubAgentFixture(maxToolCalls: 4);

        var id = fx.Spawn("the thing", ("maxToolCalls", 1));
        SubAgentFixture.InvokeOn(fx.ChildScope(id), "ListNodes");

        var refusal = SubAgentFixture.InvokeOn(fx.ChildScope(id), "ListNodes");

        StringAssert.Contains(refusal, WorkflowAgentToolkit.ResetBudgetToolName,
            "the child holds it, so it may be pointed at it");
        StringAssert.Contains(refusal, "report", "and it still owes its dispatcher an answer");
    }

    [TestMethod]
    public async Task AChild_HoldsTheResetTool()
    {
        // The widening, stated plainly: the reset tool is part of the parent's surface like any other, so it
        // is inherited by default and can be named in a whitelist. The alternative — a name in the grant list
        // that the child's scope has switched off — is the "visible and unusable" shape this change removes,
        // and it is what the model was before: told about a way out and refused it.
        await using var fx = new SubAgentFixture(maxToolCalls: 4);

        var id = fx.Spawn("the thing",
            ("maxToolCalls", 1),
            ("allowedTools", new[] { "ListNodes", WorkflowAgentToolkit.ResetBudgetToolName }));
        var child = fx.ChildScope(id);

        Assert.IsTrue(child.IsToolEnabled(WorkflowAgentToolkit.ResetBudgetToolName),
            "the switch is on, as it is on the parent");
        Assert.IsTrue(SubAgentFixture.SurfaceOf(child).Contains(WorkflowAgentToolkit.ResetBudgetToolName),
            "so the model is shown it");
        Assert.IsFalse(fx.RowOf(id).DroppedRequests.Any(d => d.Contains(WorkflowAgentToolkit.ResetBudgetToolName)),
            "and naming it is not a refusal");

        // Reaching it with nobody registered to answer denies rather than allowing itself — the property that
        // survives the widening. An unanswerable prompt is a no, whichever scope asks it. The child's own share
        // is spent first, so the reset has something to be about and the denial cannot be a "nothing to do".
        SubAgentFixture.InvokeOn(child, "ListNodes");
        var before = Spent(child);
        var reply = JObject.Parse(SubAgentFixture.InvokeOn(child, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual("denied", (string?)reply["status"]);
        Assert.AreEqual(before, Spent(child), "a denial leaves the budget where it was");
    }

    [TestMethod]
    public async Task AChildsReset_ReachesTheUser_AndReopensTheWholeTree()
    {
        // The other half, and the reason the level and the handler have to travel with the tool: a child
        // holding the reset tool is useless unless the question it asks arrives at the host's dialog. Where it
        // lands is the cost of the widening — ResetChain walks to the root, so the child's ask reopens the
        // session and not merely its own share.
        await using var fx = new SubAgentFixture(maxToolCalls: 4);
        var asked = 0;
        fx.Scope.WithConfirmationHandler(args =>
        {
            asked++;
            args.Result = AgentConfirmationResult.AllowAlways;
            return Task.CompletedTask;
        });

        var id = fx.Spawn("the thing", ("maxToolCalls", 1));
        SubAgentFixture.InvokeOn(fx.ChildScope(id), "ListNodes");
        Assert.AreEqual(2, Spent(fx.Scope));

        var reply = JObject.Parse(SubAgentFixture.InvokeOn(fx.ChildScope(id), WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual("ok", (string?)reply["status"]);
        Assert.AreEqual(1, asked, "the host's own handler answered it, from the dialogs the host registered");
        Assert.AreEqual(0, Spent(fx.Scope),
            "and the whole chain is zeroed — the child's agreement reopens the session, not its own share");
    }

    [TestMethod]
    public async Task AChild_InheritsTheHostsInteractionConfiguration()
    {
        // The prerequisite the "hand it down as it stands" change turns on. RequestConfirmation is offered
        // only when the level is above zero AND a handler is registered on that very scope, and a child scope
        // is a fresh one — so a child handed the tool without the configuration would hold a name it does not
        // have. Asserted at both settings, because "the level travels" is only true if the off state does too.
        await using var on = new SubAgentFixture(maxToolCalls: 10);
        var asked = 0;
        on.Scope.WithConfirmationHandler(args =>
        {
            asked++;
            args.Result = AgentConfirmationResult.AllowAlways;
            return Task.CompletedTask;
        });

        var child = on.ChildScope(on.Spawn("the thing",
            ("allowedTools", new[] { "ListNodes", "RequestConfirmation" })));

        Assert.IsTrue(child.IsInteractionAllowed, "the host's level travels with the tool that needs it");
        Assert.IsTrue(SubAgentFixture.SurfaceOf(child).Contains("RequestConfirmation"),
            "and the tool is really on the child's surface, not merely in its grant list");

        var reply = JObject.Parse(SubAgentFixture.InvokeOn(child, "RequestConfirmation",
            ("operationKey", "delete-all-nodes"), ("description", "delete every node")));

        Assert.AreEqual("ok", (string?)reply["status"]);
        Assert.AreEqual(1, asked, "the host's own handler answered it — which is what 'usable' means here");

        // And a host that switched interaction off hands down the off state, which is the same decision as
        // not offering the tool: nothing is registered at level zero, on either side of the spawn.
        await using var off = new SubAgentFixture(maxToolCalls: 10);
        off.Scope.WithInteractionSafety(0);
        off.Scope.WithConfirmationHandler(args =>
        {
            args.Result = AgentConfirmationResult.AllowAlways;
            return Task.CompletedTask;
        });

        var quiet = off.ChildScope(off.Spawn("the thing"));

        Assert.IsFalse(quiet.IsInteractionAllowed, "level zero is a decision about the tree, not about one scope");
        Assert.IsFalse(SubAgentFixture.SurfaceOf(quiet).Contains("RequestConfirmation"),
            "and at level zero the tool is not offered at all");
    }

    [TestMethod]
    public async Task TheParentsReset_ReopensTheTree_WithoutRewritingAGrantAlreadyMade()
    {
        // The user agreed to reopen the session's budget. Two things follow, and the asymmetry between them
        // is deliberate.
        await using var fx = new SubAgentFixture(maxToolCalls: 4);
        fx.Scope.WithConfirmationHandler(args =>
        {
            args.Result = AgentConfirmationResult.AllowAlways;
            return Task.CompletedTask;
        });

        var id = fx.Spawn("the thing", ("maxToolCalls", 1));
        SubAgentFixture.InvokeOn(fx.ChildScope(id), "ListNodes");
        Assert.AreEqual(2, Spent(fx.Scope));

        SubAgentFixture.InvokeOn(fx.Scope, "GetWorkflowSummary");
        SubAgentFixture.InvokeOn(fx.Scope, "GetWorkflowSummary");
        Assert.AreEqual(4, Spent(fx.Scope));

        var reply = JObject.Parse(SubAgentFixture.InvokeOn(fx.Scope, WorkflowAgentToolkit.ResetBudgetToolName));
        Assert.AreEqual("ok", (string?)reply["status"]);
        Assert.AreEqual(0, Spent(fx.Scope), "the session is reopened");

        // The extension reaches the whole tree through what it grants next, not by rewriting a grant that
        // was already made: a fresh spawn sees the full allowance again. An existing child that spent its
        // share stays spent — it was told to stop and report, and the answer to "carry on" is a new agent,
        // not a sub-limit silently widened behind the model's back.
        var next = fx.Spawn("carry on");
        Assert.AreEqual(3, fx.RowOf(next).MaxToolCalls,
            "the reopened session can delegate at full strength again");
    }

    [TestMethod]
    public async Task ASpentChild_ReportsUpward_RatherThanWaitingToBeUnstuck()
    {
        // What a child does when its share runs out, pinned so the reset's asymmetry above stays a design and
        // not an omission: it is refused, it is told its dispatcher is waiting, and the grant it was given
        // does not silently grow behind the model's back.
        await using var fx = new SubAgentFixture(maxToolCalls: 4);

        var id = fx.Spawn("the thing", ("maxToolCalls", 1));
        SubAgentFixture.InvokeOn(fx.ChildScope(id), "ListNodes");
        var refused = SubAgentFixture.InvokeOn(fx.ChildScope(id), "ListNodes");

        StringAssert.Contains(refused, "report");
        Assert.AreEqual(1, fx.RowOf(id).MaxToolCalls, "the grant is a fact about the spawn, not a running total");
    }

    [TestMethod]
    public async Task WithoutTheUsersAgreement_TheTreeStaysClosed()
    {
        // The same path with a refusal, so the reset cannot be confused with an unconditional clear.
        await using var fx = new SubAgentFixture(maxToolCalls: 1);
        fx.Scope.WithConfirmationHandler(args =>
        {
            args.Result = AgentConfirmationResult.Deny;
            return Task.CompletedTask;
        });

        SubAgentFixture.InvokeOn(fx.Scope, "GetWorkflowSummary");
        var reply = JObject.Parse(SubAgentFixture.InvokeOn(fx.Scope, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual("denied", (string?)reply["status"]);
        Assert.AreEqual(1, Spent(fx.Scope), "a denial leaves the budget exactly where it was");
    }

    [TestMethod]
    public async Task ASpawnIsAQuery_AndSoIsNotChargedToTheMutationBudget()
    {
        // Spawning changes no part of the graph, so classifying it as a mutation would burn a host's write
        // budget for a read-only act — and would mark the tree dirty for an agent that only delegated.
        await using var readBlocked = new SubAgentFixture();
        readBlocked.Scope.WithMaxReadToolCalls(0);

        var refusal = readBlocked.Invoke("SpawnSubAgent", ("task", "the thing"));

        StringAssert.Contains(refusal, "Query tool call limit",
            "a spawn that counted as a mutation would slip past a spent read budget");
        Assert.AreEqual(0, readBlocked.Rows.Count);

        await using var writeBlocked = new SubAgentFixture();
        writeBlocked.Scope.WithMaxWriteToolCalls(0);

        var id = writeBlocked.Spawn("the thing");

        Assert.IsTrue(writeBlocked.RowOf(id).State is SubAgentState.Queued or SubAgentState.Running or SubAgentState.Completed,
            "a spent mutation budget has no bearing on dispatching an agent");
        Assert.AreEqual(0, writeBlocked.ChildScope(id).MaxWriteToolCalls,
            "the parent's mutation cap is handed down unchanged, as every inherited cap is");
    }

    [TestMethod]
    public async Task WithoutAnySubAgents_TheAccountingIsTheScopesOwn()
    {
        // The regression that matters: a host that never attaches this subsystem must be unable to tell the
        // shared pot was added. No outer ledger means the root is this scope, which is the old behaviour
        // reached by the new code path.
        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new NodeDefaultViewModel());
        var scope = new WorkflowAgentScope(tree).WithMaxToolCalls(3);

        Assert.AreEqual(0, Spent(scope));
        SubAgentFixture.InvokeOn(scope, "GetWorkflowSummary");
        Assert.AreEqual(1, Spent(scope));

        CollectionAssert.DoesNotContain(
            SubAgentFixture.SurfaceOf(scope).ToArray(), "SpawnSubAgent",
            "nothing contributes the sub-agent tools until a subsystem is attached");
    }
}
