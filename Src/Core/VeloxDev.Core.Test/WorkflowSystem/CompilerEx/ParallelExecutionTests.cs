using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// The contract of a fan-out group: its branches run concurrently (as interleaved async operations), each one
/// sees only the fan-out source's payload, each one's log lines stay together, and when several branches ask to
/// redirect the first in branch order wins while the rest are reported.
/// <para>
/// Written before the engine change: <see cref="Branches_AreInFlightAtTheSameTime"/> and
/// <see cref="TwoBranchesAskingToRedirect_TheFirstInBranchOrderWins_AndTheOtherIsLogged"/> fail against a
/// sequential engine; the other two are guards that must stay green through the change.
/// </para>
/// </summary>
[TestClass]
public class ParallelExecutionTests
{
    /// <summary>`s → {a, b} → j`: a plain node's fan-out, which the compiler turns into a ParallelSegment.</summary>
    private static (ProbeNode Source, ProbeNode A, ProbeNode B, ProbeNode Join) FanOut()
    {
        var s = new ProbeNode("s") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        var j = new ProbeNode("j");
        ProbeGraph.Wire(s, a);
        ProbeGraph.Wire(s, b);
        ProbeGraph.Wire(a, j);
        ProbeGraph.Wire(b, j);
        return (s, a, b, j);
    }

    [TestMethod]
    public void FanOut_CompilesToAParallelSegment()   // 前置确认：下面三条测的确实是并行段
    {
        var (s, _, _, _) = FanOut();

        var graph = ProbeGraph.Compile(s);

        Assert.IsNotNull(graph.Entries.Select(ProbeGraph.AsParallel).FirstOrDefault(p => p is not null),
            "a plain node with two targets must compile into a ParallelSegment");
    }

    /// <summary>
    /// Runs a two-branch fan-out in which each branch waits until the other has arrived (or 1.5 s) before
    /// finishing, and reports when each was in flight. If the branches overlap, both release each other almost
    /// immediately; if they are serialised, the first one waits out the timeout and the second runs after it.
    /// </summary>
    private static async Task<(DateTime[] Starts, DateTime[] Ends)> RunGatedFanOut(int? maxParallelBranches)
    {
        var (s, a, b, _) = FanOut();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        var starts = new DateTime[2];
        var ends = new DateTime[2];

        async Task<object?> Body(int index, ITaskContext ctx, CancellationToken ct)
        {
            starts[index] = DateTime.UtcNow;
            if (Interlocked.Increment(ref arrived) == 2) gate.TrySetResult(true);
            await Task.WhenAny(gate.Task, Task.Delay(1500));
            ends[index] = DateTime.UtcNow;
            return ctx.Data ?? string.Empty;
        }

        a.AsyncHandler = (ctx, ct) => Body(0, ctx, ct);
        b.AsyncHandler = (ctx, ct) => Body(1, ctx, ct);

        var context = new RuntimeContext { MaxParallelBranches = maxParallelBranches };
        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(s), context, CancellationToken.None);
        return (starts, ends);
    }

    private static bool Overlap(DateTime[] starts, DateTime[] ends)
        => starts[0] < ends[1] && starts[1] < ends[0];

    [TestMethod]
    public async Task Branches_AreInFlightAtTheSameTime()
    {
        var (starts, ends) = await RunGatedFanOut(maxParallelBranches: null);

        Assert.IsTrue(Overlap(starts, ends),
            $"the two branches must be in flight at the same time; a=[{starts[0]:HH:mm:ss.fff},{ends[0]:HH:mm:ss.fff}] b=[{starts[1]:HH:mm:ss.fff},{ends[1]:HH:mm:ss.fff}]");
    }

    [TestMethod]
    public async Task MaxParallelBranches_SerialisesTheGroupWhenSetToOne()
    {
        var (starts, ends) = await RunGatedFanOut(maxParallelBranches: 1);

        Assert.IsFalse(Overlap(starts, ends),
            $"a cap of one must let one branch finish before the next starts; a=[{starts[0]:HH:mm:ss.fff},{ends[0]:HH:mm:ss.fff}] b=[{starts[1]:HH:mm:ss.fff},{ends[1]:HH:mm:ss.fff}]");
    }

    [TestMethod]
    public async Task EachBranch_SeesOnlyTheFanOutSourcePayload()
    {
        var (s, a, b, _) = FanOut();
        object? seenByB = null;
        a.Handler = (_, _) => "A";
        b.Handler = (ctx, _) => { seenByB = ctx.Data; return "B"; };

        await ProbeGraph.RunAsync(ProbeGraph.Compile(s));

        Assert.AreEqual("SRC", a.Calls[0].Data, "the first branch receives the fan-out source payload");
        Assert.AreEqual("SRC", seenByB, "a sibling's output must never arrive as another branch's input");
    }

    [TestMethod]
    public async Task EachBranchesLogLines_StayTogether()
    {
        var (s, a, b, _) = FanOut();
        a.Handler = (ctx, _) => { var rc = (IRuntimeContext)ctx; rc.Log("A1"); rc.Log("A2"); return "A"; };
        b.Handler = (ctx, _) => { var rc = (IRuntimeContext)ctx; rc.Log("B1"); rc.Log("B2"); return "B"; };

        var session = await ProbeGraph.RunAsync(ProbeGraph.Compile(s));

        var logs = session.Logs.ToList();
        var lastOfA = logs.FindIndex(l => l.Contains("A2"));
        var firstOfB = logs.FindIndex(l => l.Contains("B1"));
        Assert.IsTrue(lastOfA >= 0 && firstOfB >= 0, "both branches must have logged");
        Assert.IsTrue(lastOfA < firstOfB,
            $"one fan-out should read as one block per branch, not as interleaved streams; got: {string.Join(" | ", logs)}");
    }

    [TestMethod]
    public async Task TwoBranchesAskingToRedirect_TheFirstInBranchOrderWins_AndTheOtherIsLogged()
    {
        var s0 = new ProbeNode("s0");
        var s1 = new ProbeNode("s1");
        var a = new RedirectableNode("a");
        var b = new RedirectableNode("b");
        var j = new ProbeNode("j");
        ProbeGraph.Wire(s0, s1);
        ProbeGraph.Wire(s1, a);
        ProbeGraph.Wire(s1, b);
        ProbeGraph.Wire(a, j);
        ProbeGraph.Wire(b, j);

        // 只在第一趟报错：否则重跑会再报一次，一路撞到 50 次重定向上限而抛异常。
        static Func<ITaskContext, CancellationToken, object?> FailOnce(string label) =>
            (ctx, _) =>
            {
                if (ctx is IRuntimeContext rc && rc.Attempt == 1) rc.Error($"{label} failed");
                return label;
            };
        a.Handler = FailOnce("A");
        b.Handler = FailOnce("B");
        a.Resolve = _ => 1;   // s1 的 Order：s0(0) 在目标之前，所以重跑时 s0 应被跳过
        b.Resolve = _ => 0;   // s0 的 Order：若「后写者胜」，重跑会连 s0 一起重跑

        var session = await ProbeGraph.RunAsync(ProbeGraph.Compile(s0));

        Assert.AreEqual(2, session.Attempt, "the first branch's redirect must be honoured, with exactly one re-run");
        Assert.HasCount(1, s0.Calls,
            "the winner redirects to s1, so s0 (Order 0) must be skipped on the re-run — a last-writer-wins engine re-drives it");
        Assert.IsTrue(session.Logs.Any(l => l.Contains("ignored")),
            $"the losing branch's request must be reported rather than silently dropped; got: {string.Join(" | ", session.Logs)}");
    }
}
