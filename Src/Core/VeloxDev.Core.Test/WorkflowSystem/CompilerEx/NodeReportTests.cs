using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// What a node's report does to the run — the rule that replaced "a <c>Warn</c> ends the whole flow".
/// <para>
/// <see cref="IRuntimeContext.Error"/>/<see cref="IRuntimeContext.Warn"/> and a thrown exception are all reports:
/// the line is written, the drive counts as having produced <c>null</c>, and the chain carries on. The one thing
/// that changes the plan is a configured handler — a node implementing <see cref="IRedirectable"/>, whose answer
/// decides where the run goes instead.
/// </para>
/// <para>
/// This is what makes a wide graph survivable: the demo's python node warns when a script is empty and reports
/// when the interpreter fails, and both used to end a thirty-branch run at that node.
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
    public async Task AWarnWithoutARedirectableNode_LeavesTheRunGoing_WithANullResult()
    {
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };

        var session = await RunChainAsync(rc => { rc.Warn("nothing to run"); return "A"; }, b);

        Assert.IsTrue(session.Logs.Any(l => l.Contains("[Warning] nothing to run", StringComparison.Ordinal)),
            $"the warning is the record, and it must stay; got: {string.Join(" | ", session.Logs)}");
        Assert.HasCount(1, b.Calls, "the run must reach the node after the one that warned");
        Assert.IsNull(b.Calls[0].Data, "the drive that warned counts as having produced null, not as having produced A");
        Assert.IsFalse(session.EndedWithError);
        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual(RunOutcome.Completed, session.Outcome);
    }

    [TestMethod]
    public async Task AnErrorWithoutARedirectableNode_LeavesTheRunGoing_WithANullResult()
    {
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };

        var session = await RunChainAsync(rc => { rc.Error("the interpreter died"); return "A"; }, b);

        Assert.IsTrue(session.Logs.Any(l => l.Contains("[Error] the interpreter died", StringComparison.Ordinal)),
            $"the error is the record; got: {string.Join(" | ", session.Logs)}");
        Assert.HasCount(1, b.Calls);
        Assert.IsNull(b.Calls[0].Data);
        Assert.IsFalse(session.EndedWithError);
        Assert.AreEqual(RunOutcome.Completed, session.Outcome);
    }

    /// <summary>
    /// A join point aggregates by source node, so "produced null" has to be a registered null rather than an
    /// absent source: the graph's author wired that node deliberately, and its silence is information.
    /// </summary>
    [TestMethod]
    public async Task AReportedNode_IsVisibleToAJoinPoint_AsNull()
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
        Assert.AreEqual(2, group.Count, "the reporting node is in the group, not missing from it");
        Assert.IsTrue(group.TryGetValue(a, out var fromA) && fromA is null, "and it is there as a null");
        Assert.IsTrue(group.TryGetValue(b, out var fromB) && Equals(fromB, "B"), "its sibling is unaffected");
    }

    /// <summary>
    /// The configured handling, and the only thing a report changes: a node that implements
    /// <see cref="IRedirectable"/> gets asked where the run should go. Without it there is nothing to ask, which is
    /// why the tests above just carry on.
    /// </summary>
    [TestMethod]
    public async Task AReport_StillLetsTheRedirectContractDecide()
    {
        var s = new ProbeNode("s") { Handler = (_, _) => "S" };
        var r = new RedirectableNode("r");
        ProbeGraph.Wire(s, r);
        r.Handler = (ctx, _) =>
        {
            if (((IRuntimeContext)ctx).Attempt == 1) ((IRuntimeContext)ctx).Warn("re-run from the start, please");
            return "R";
        };
        r.Resolve = ctx => ctx.Attempt == 1 ? 0 : null;   // s 的 Order
        var context = new RuntimeContext();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(s), context, CancellationToken.None);

        Assert.AreEqual(2, context.Attempt, "a warning reaches the redirect contract when the node has one");
        Assert.HasCount(2, s.Calls, "and the contract's answer is honoured: the redirected-to node is driven again");
        Assert.AreEqual("Completed", context.Status);
    }
}
