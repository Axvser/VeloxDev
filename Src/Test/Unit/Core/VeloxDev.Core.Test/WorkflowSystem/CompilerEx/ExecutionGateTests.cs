using System;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// The pause point: <see cref="IExecutionGate"/> is asked before every node, never inside one, and it is asked
/// through the session even when the node being driven sits inside a fan-out branch.
/// <para>
/// Every test here configures the gate. With none configured the run must be exactly what it was before the
/// capability existed, which is what <see cref="ParallelExecutionTests"/> keeps pinned.
/// </para>
/// </summary>
[TestClass]
public class ExecutionGateTests
{
    // `a → b`，a 在驱动中关上门，于是运行被拦在 b 之前。
    private static (ProbeNode Source, ProbeNode After, ManualExecutionGate Gate, TaskCompletionSource<bool> Closed) ChainHeldBeforeB()
    {
        var a = new ProbeNode("a");
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(a, b);

        var gate = new ManualExecutionGate();
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        a.Handler = (_, _) =>
        {
            gate.Pause();
            closed.TrySetResult(true);
            return "A";
        };
        return (a, b, gate, closed);
    }

    [TestMethod]
    public async Task AClosedGate_HoldsTheRunAtTheNextNodeBoundary_AndResumeLetsItGo()
    {
        var (source, b, gate, closed) = ChainHeldBeforeB();
        var context = new RuntimeContext { ExecutionGate = gate };

        var run = new RuntimeEngine().RunAsync(ProbeGraph.Compile(source), context, CancellationToken.None);
        await closed.Task;
        await Task.Delay(100);   // 门若没拦住，这点时间足够引擎把 b 驱动掉

        Assert.IsEmpty(b.Calls, "a held run must not drive the next node");
        Assert.IsTrue(context.IsRunning, "paused is not stopped: the run is still in flight");
        Assert.AreEqual("Paused", context.Status, "a held run is the one thing a host can bind to see the pause");
        Assert.IsFalse(run.IsCompleted, "the run is parked at the node boundary, not finished");

        await Task.Run(gate.Resume);   // 任何线程都能放行，不只是驱动运行的那条
        await run;

        Assert.HasCount(1, b.Calls, "releasing the gate carries the run on from where it was held");
        Assert.AreEqual("Completed", context.Status);
        Assert.AreEqual(RunOutcome.Completed, context.Outcome);
    }

    /// <summary>
    /// The reason <see cref="BranchRuntimeContext"/> exposes its session: a branch drives its nodes through that
    /// facade, so a gate resolved by casting the context to <see cref="RuntimeContext"/> would come up empty
    /// exactly here. With the bug, both branches run while the gate is closed.
    /// </summary>
    [TestMethod]
    public async Task AClosedGate_AlsoHoldsTheBranchesOfAFanOut()
    {
        var s = new ProbeNode("s") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a");
        var b = new ProbeNode("b");
        ProbeGraph.Wire(s, a);
        ProbeGraph.Wire(s, b);

        var gate = new ManualExecutionGate();
        // 先启动的那条分支会在节点里把门关上。下面断言不关心是哪一条，只看总数。
        a.Handler = (_, _) => { gate.Pause(); return "A"; };
        b.Handler = (_, _) => { gate.Pause(); return "B"; };

        var context = new RuntimeContext { ExecutionGate = gate };
        var run = new RuntimeEngine().RunAsync(ProbeGraph.Compile(s), context, CancellationToken.None);
        await Task.Delay(150);

        Assert.AreEqual(1, a.Calls.Count + b.Calls.Count,
            "the second branch must be held too — a gate the branches cannot see is a gate that does nothing");

        gate.Resume();
        await run;

        Assert.HasCount(1, a.Calls);
        Assert.HasCount(1, b.Calls);
        Assert.AreEqual("Completed", context.Status);
    }

    [TestMethod]
    public async Task CancellingAPausedRun_EndsIt_InsteadOfWaitingForever()
    {
        var (source, b, gate, closed) = ChainHeldBeforeB();
        using var cts = new CancellationTokenSource();
        var context = new RuntimeContext { ExecutionGate = gate };

        var run = new RuntimeEngine().RunAsync(ProbeGraph.Compile(source), context, cts.Token);
        await closed.Task;
        await Task.Delay(100);
        Assert.IsEmpty(b.Calls);

        cts.Cancel();

        var finished = await Task.WhenAny(run, Task.Delay(5000));
        Assert.AreSame(run, finished, "cancellation has to beat the pause — a run stopped while held must end, not hang");
        Assert.AreEqual("Stopped", context.Status);
        Assert.AreEqual(RunOutcome.Cancelled, context.Outcome);
        Assert.IsFalse(context.IsRunning);
        Assert.IsEmpty(b.Calls, "the run stops where it was held");
    }
}
