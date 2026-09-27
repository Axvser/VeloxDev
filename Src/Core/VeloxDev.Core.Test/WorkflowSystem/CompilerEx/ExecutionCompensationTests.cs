using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// <see cref="IExecutionCompensation"/>: a run that ends badly hands back the nodes it already drove, most recent
/// first. The engine rolls nothing back itself — it cannot know what a node's effects were — so what these tests
/// pin is the list, its order, and that a compensating host never makes things worse.
/// </summary>
[TestClass]
public class ExecutionCompensationTests
{
    private static (List<string> Names, List<object?> Outputs) Recording(RuntimeContext context)
    {
        var names = new List<string>();
        var outputs = new List<object?>();
        context.Compensation = new DelegateExecutionCompensation(c =>
        {
            names.Add(((ProbeNode)c.Node).Name);
            outputs.Add(c.Output);
        });
        return (names, outputs);
    }

    [TestMethod]
    public async Task AStoppedRun_HandsBackItsSuccesses_MostRecentFirst()
    {
        using var cts = new CancellationTokenSource();
        var s = new ProbeNode("s") { Handler = (_, _) => "S" };
        var a = new ProbeNode("a") { Handler = (_, _) => { cts.Cancel(); return "A"; } };   // 宿主在节点里把运行停掉
        var b = new ProbeNode("b");
        ProbeGraph.Wire(s, a);
        ProbeGraph.Wire(a, b);

        var context = new RuntimeContext();
        var (names, outputs) = Recording(context);

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(s), context, cts.Token);

        Assert.AreEqual(RunOutcome.Cancelled, context.Outcome);
        Assert.IsEmpty(b.Calls, "the run stops at the next node boundary, so b never ran");
        CollectionAssert.AreEqual(new[] { "a", "s" }, names,
            $"compensation walks the successes backwards, and the node that never ran is not among them; got: {string.Join(", ", names)}");
        CollectionAssert.AreEqual(new object?[] { "A", "S" }, outputs, "each node comes back with what it produced");
    }

    [TestMethod]
    public async Task ACompletedRun_HandsBackNothing()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var context = new RuntimeContext();
        var (names, _) = Recording(context);

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.AreEqual(RunOutcome.Completed, context.Outcome);
        Assert.IsEmpty(names, "nothing to undo: a completed run compensates nothing");
    }

    [TestMethod]
    public async Task ACompensatorThatThrows_DoesNotStopTheNodesBehindIt()
    {
        using var cts = new CancellationTokenSource();
        var s = new ProbeNode("s") { Handler = (_, _) => "S" };
        var a = new ProbeNode("a") { Handler = (_, _) => { cts.Cancel(); return "A"; } };
        var b = new ProbeNode("b");
        ProbeGraph.Wire(s, a);
        ProbeGraph.Wire(a, b);

        var names = new List<string>();
        var context = new RuntimeContext
        {
            Compensation = new DelegateExecutionCompensation(c =>
            {
                var name = ((ProbeNode)c.Node).Name;
                names.Add(name);
                if (name == "a") throw new InvalidOperationException("cannot undo");
            }),
        };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(s), context, cts.Token);

        CollectionAssert.AreEqual(new[] { "a", "s" }, names,
            $"best effort: one node that cannot be undone must not strand the rest; got: {string.Join(", ", names)}");
        Assert.AreEqual(RunOutcome.Cancelled, context.Outcome, "the ending that caused the cleanup stays the headline");
        Assert.IsTrue(context.Logs.Any(l => l.Contains("[Compensation]", StringComparison.Ordinal)),
            $"the node that could not be undone has to be visible; got: {string.Join(" | ", context.Logs)}");
    }

    /// <summary>
    /// `s0 → s1 → r → x`: r asks for a re-run toward s1 on the first pass, and on the second x throws while its
    /// own redirect contract is what breaks — the one failure the engine cannot work around, which is how a run
    /// gets to end badly at all now that a node's own failures only produce a null.
    /// <para>
    /// s0 is the prefix the redirect preserves — driven once, never again — and it is still something the run has
    /// to answer for; s1 is driven twice and comes back once, at its latest position.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task AfterARedirect_ASkippedPrefixNode_IsStillHandedBack_AndAReDrivenNodeOnlyOnce()
    {
        var s0 = new ProbeNode("s0") { Handler = (_, _) => "S0" };
        var s1 = new ProbeNode("s1") { Handler = (_, _) => "S1" };
        var r = new RedirectableNode("r");
        var x = new RedirectableNode("x") { ResolveThrows = new InvalidOperationException("resolve boom") };
        ProbeGraph.Wire(s0, s1);
        ProbeGraph.Wire(s1, r);
        ProbeGraph.Wire(r, x);

        r.Handler = (ctx, _) =>
        {
            if (((IRuntimeContext)ctx).Attempt == 1) ((IRuntimeContext)ctx).Error("ask for a re-run");
            return "R";
        };
        r.Resolve = ctx => ctx.Attempt == 1 ? 1 : null;   // s1 的 Order：重跑时 s0 会被跳过
        x.Handler = (ctx, _) => ((IRuntimeContext)ctx).Attempt == 2
            ? throw new InvalidOperationException("boom")
            : "X";

        var context = new RuntimeContext();
        var (names, _) = Recording(context);

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(s0), context, CancellationToken.None);

        Assert.AreEqual(2, context.Attempt, "the graph was walked twice");
        Assert.HasCount(1, s0.Calls, "s0 is the preserved prefix: driven in the first pass and never again");
        Assert.AreEqual(RunOutcome.Failed, context.Outcome, "the run ends on the engine's machinery giving up");
        CollectionAssert.AreEqual(new[] { "x", "r", "s1", "s0" }, names,
            $"reverse drive order, one entry per node — s1 was driven twice and appears once; got: {string.Join(", ", names)}");
    }
}
