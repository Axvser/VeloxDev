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
/// The second half is the safety property, and it is the reason this subsystem may hand out spendable
/// capabilities at all: a child cannot widen its own budget. The parent's escape hatch — ask the user — is
/// structurally absent from a background child, in two independent ways.
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
    public async Task AChildThatRunsOut_IsToldToReportUpward_NotToAskTheUser()
    {
        // The refusal text differs by whether the scope owns the session's allowance. A child sent to the
        // reset tool would find it switched off and learn to retry; it is told the option it actually has.
        await using var fx = new SubAgentFixture(maxToolCalls: 4);

        var id = fx.Spawn("the thing", ("maxToolCalls", 1));
        SubAgentFixture.InvokeOn(fx.ChildScope(id), "ListNodes");

        var refusal = SubAgentFixture.InvokeOn(fx.ChildScope(id), "ListNodes");

        StringAssert.Contains(refusal, "cannot extend it yourself");
        Assert.IsFalse(refusal.Contains(WorkflowAgentToolkit.ResetBudgetToolName),
            "a child must not be pointed at a tool it does not hold");
        Assert.IsTrue(refusal.Contains("report"), "it needs to be told what to do instead");
    }

    [TestMethod]
    public async Task AChild_CannotReachTheResetTool()
    {
        // The safety property. Interaction safety closes the question, the switch closes the tool; either
        // alone would do, and both are asserted because a regression in one must not be masked by the other.
        await using var fx = new SubAgentFixture(maxToolCalls: 4);

        var id = fx.Spawn("the thing", ("allowedTools", new[] { "ListNodes", WorkflowAgentToolkit.ResetBudgetToolName }));
        var child = fx.ChildScope(id);

        Assert.IsFalse(child.IsToolEnabled(WorkflowAgentToolkit.ResetBudgetToolName),
            "the switch is off on the child");
        Assert.IsFalse(SubAgentFixture.SurfaceOf(child).Contains(WorkflowAgentToolkit.ResetBudgetToolName),
            "so the model is never shown it");

        // And reaching for it anyway — through the unfiltered surface — is refused by the gate rather than
        // quietly obeyed. The refused call must not move the ledger either: a refusal is not a call.
        var before = Spent(child);
        var refusal = SubAgentFixture.InvokeTool(
            SubAgentFixture.ToolOn(child.CreateToolkit(), WorkflowAgentToolkit.ResetBudgetToolName));

        StringAssert.Contains(refusal, "disabled");
        Assert.AreEqual(before, Spent(child));
        Assert.AreEqual(1, Spent(fx.Scope), "and nothing was charged to the tree either");
    }

    [TestMethod]
    public async Task AChildWithInteractionOff_CannotAskEvenIfTheSwitchWereOn()
    {
        // The second half of the same property, tested where it lives. A background child raising a modal
        // question would put it to the user while its parent's turn is still suspended, so the level is
        // zeroed unconditionally — and at level zero the reset denies rather than asking.
        await using var fx = new SubAgentFixture(maxToolCalls: 10);
        var id = fx.Spawn("the thing", ("maxToolCalls", 1));
        var child = fx.ChildScope(id);

        Assert.IsFalse(child.IsInteractionAllowed, "a background child never puts a question to the user");

        // Proving the belt independently of the braces: put the tool back, exhaust the child's own share,
        // and the escape hatch still refuses — because there is nobody to ask, not because it is missing.
        child.WithToolEnabled(WorkflowAgentToolkit.ResetBudgetToolName, true);
        SubAgentFixture.InvokeOn(child, "ListNodes");

        var reply = JObject.Parse(SubAgentFixture.InvokeOn(child, WorkflowAgentToolkit.ResetBudgetToolName));

        Assert.AreEqual("denied", (string?)reply["status"],
            "level zero means the user cannot be asked, and a budget only reopens with their agreement");
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
        // What a child does when its share runs out, pinned so the reset's asymmetry above stays a design
        // and not an omission: it is refused, it is not pointed at the escape hatch, and the grant it was
        // given does not silently grow.
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
