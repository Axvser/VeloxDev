using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// The two levels a node can report at, and what each does to the run.
/// <para>
/// <see cref="IRuntimeContext.Warn"/> is a note: the line is written, the value the node returned still flows, and
/// the run carries on. <see cref="IRuntimeContext.Error"/> — and an exception the node did not catch — is a stop:
/// the line is written, that drive counts as having produced <c>null</c>, and the flow ends unless the node
/// implements <see cref="IRedirectable"/>, which is the one configured answer to a failure.
/// </para>
/// <para>
/// This is what the demo's python node needs: warn when a script is empty (the graph has thirty branches and one
/// empty script is not a reason to abandon them), report an error when the interpreter itself failed.
/// </para>
/// </summary>
[TestClass]
public class NodeReportTests
{
    /// <summary>`a → b`: a reports and returns a value anyway; what b receives is the assertion.</summary>
    private static async Task<RuntimeContext> RunChainAsync(
        Func<IRuntimeContext, object?> report, ProbeNode b)
    {
        var a = new ProbeNode("a")
        {
            Handler = (ctx, _) => report((IRuntimeContext)ctx),
        };
        ProbeGraph.Wire(a, b);
        return await ProbeGraph.RunAsync(ProbeGraph.Compile(a));
    }

    [TestMethod]
    public async Task AWarnWithoutARedirectableNode_LeavesTheRunGoing_WithTheNodesOwnResult()
    {
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };

        var session = await RunChainAsync(rc => { rc.Warn("nothing to run"); return "A"; }, b);

        Assert.IsTrue(session.Logs.Any(l => l.Contains("[Warning] nothing to run", StringComparison.Ordinal)),
            $"the warning is the record, and it must stay; got: {string.Join(" | ", session.Logs)}");
        Assert.HasCount(1, b.Calls, "the run must reach the node after the one that warned");
        Assert.AreEqual("A", b.Calls[0].Data, "a warning is a note — the value the node returned still flows");
        Assert.IsFalse(session.EndedWithError);
        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual(RunOutcome.Completed, session.Outcome);
    }

    [TestMethod]
    public async Task AnErrorWithoutARedirectableNode_EndsTheRun()
    {
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };

        var session = await RunChainAsync(rc => { rc.Error("the interpreter died"); return "A"; }, b);

        Assert.IsTrue(session.Logs.Any(l => l.Contains("[Error] the interpreter died", StringComparison.Ordinal)),
            $"the error is the record; got: {string.Join(" | ", session.Logs)}");
        Assert.IsEmpty(b.Calls, "an error nobody can place ends the flow where it happened");
        Assert.AreEqual(-1, session.CurrentOrder, "status code drops to -1 (absolute stop)");
        Assert.IsTrue(session.EndedWithError);
        Assert.AreEqual("Stopped", session.Status);
        Assert.AreEqual(RunOutcome.Failed, session.Outcome);
    }

    /// <summary>
    /// The same rule seen through the group a join point aggregates: the warning does not cost the node its
    /// value, so a downstream join reads what the node produced rather than a hole where it used to be.
    /// </summary>
    [TestMethod]
    public async Task AWarnedNode_ReachesAJoinPointWithItsOwnValue()
    {
        var p = new ProbeNode("p") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a") { Handler = (ctx, _) => { ((IRuntimeContext)ctx).Warn("nothing to run"); return "A"; } };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        object? seen = null;
        var join = new ProbeNode("join") { Handler = (ctx, _) => { seen = ctx.Data; return null; } };
        ProbeGraph.Wire(p, a);
        ProbeGraph.Wire(p, b);
        ProbeGraph.Wire(a, join);
        ProbeGraph.Wire(b, join);

        await ProbeGraph.RunAsync(ProbeGraph.Compile(p));

        var group = Assert.IsInstanceOfType<IGroupData>(seen, "the join receives the group of upstream outputs");
        Assert.AreEqual(2, group.Count, "the warned node is in the group, not missing from it");
        Assert.IsTrue(group.TryGetValue(a, out var fromA) && Equals(fromA, "A"),
            "and it is there with the value it produced — a note does not discard work");
        Assert.IsTrue(group.TryGetValue(b, out var fromB) && Equals(fromB, "B"), "its sibling is unaffected");
    }

    /// <summary>
    /// The configured answer to a failure: a node that implements <see cref="IRedirectable"/> gets asked where the
    /// run should go instead of the flow simply ending. Without it there is nobody to ask, which is why the test
    /// above ends the run.
    /// </summary>
    [TestMethod]
    public async Task AnError_StillLetsTheRedirectContractDecide()
    {
        var s = new ProbeNode("s") { Handler = (_, _) => "S" };
        var r = new RedirectableNode("r");
        ProbeGraph.Wire(s, r);
        r.Handler = (ctx, _) =>
        {
            if (((IRuntimeContext)ctx).Attempt == 1) ((IRuntimeContext)ctx).Error("re-run from the start, please");
            return "R";
        };
        r.Resolve = ctx => ctx.Attempt == 1 ? 0 : null;   // s 的 Order
        var context = new RuntimeContext();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(s), context, CancellationToken.None);

        Assert.AreEqual(2, context.Attempt, "the error reaches the redirect contract when the node has one");
        Assert.HasCount(2, s.Calls, "and the contract's answer is honoured: the redirected-to node is driven again");
        Assert.IsFalse(context.EndedWithError, "so the run does not end after all");
        Assert.AreEqual("Completed", context.Status);
    }
}
