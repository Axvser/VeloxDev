using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// <see cref="IExecutionErrorSink"/> and <see cref="RunOutcome"/>: the same failures the log already carries, as
/// records a host can count, store or show — plus the ending that used to share one word with the other.
/// </summary>
[TestClass]
public class ExecutionErrorSinkTests
{
    [TestMethod]
    public async Task EveryFailureTheEngineRecords_ReachesTheSinkAsARecord()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        ProbeGraph.Wire(a, b);

        var records = new List<ExecutionError>();
        var context = new RuntimeContext { ErrorSink = new DelegateExecutionErrorSink(records.Add) };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        // 两条是刻意的：先是节点自己的失败，再是引擎「无处可重定向的错误结束流程」这个判断 —— 数失败的宿主两条都要。
        Assert.HasCount(2, records);

        Assert.AreEqual(ExecutionFailurePhase.Node, records[0].Phase);
        Assert.AreSame(b, records[0].Node);
        Assert.AreEqual("boom", records[0].Message);
        Assert.IsInstanceOfType<InvalidOperationException>(records[0].Error);
        Assert.AreEqual(1, records[0].Attempt);
        Assert.AreEqual(1, records[0].Order, "the node's compile order travels with the record");

        Assert.AreEqual(ExecutionFailurePhase.Node, records[1].Phase);
        Assert.AreSame(b, records[1].Node);
        Assert.IsNull(records[1].Error, "an engine decision has no exception behind it");
    }

    /// <summary>
    /// A run the host stopped is not a failure, so it gets no <c>[Error]</c> line — but it is an ending, and a host
    /// counting endings wants it. This is also the first test the engine's cancellation path has ever had.
    /// </summary>
    [TestMethod]
    public async Task ARunTheHostCancels_ReachesTheSink_ButLeavesNoErrorLine()
    {
        using var cts = new CancellationTokenSource();
        var a = new ProbeNode("a")
        {
            Handler = (_, _) => { cts.Cancel(); return "A"; },   // 宿主从节点体里把运行停掉
        };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(a, b);

        var records = new List<ExecutionError>();
        var context = new RuntimeContext { ErrorSink = new DelegateExecutionErrorSink(records.Add) };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, cts.Token);

        Assert.AreEqual("Stopped", context.Status);
        Assert.AreEqual(RunOutcome.Cancelled, context.Outcome,
            "Status has to share one word between a failure and a cancellation; Outcome is what tells them apart");
        Assert.IsEmpty(b.Calls, "the run stops at the next node boundary");

        Assert.HasCount(1, records);
        Assert.AreEqual(ExecutionFailurePhase.Run, records[0].Phase);
        Assert.IsNull(records[0].Node);
        Assert.IsInstanceOfType<OperationCanceledException>(records[0].Error);
        Assert.IsFalse(context.Logs.Any(l => l.Contains("[Error]", StringComparison.Ordinal)),
            $"a stop the host asked for is not an error; got: {string.Join(" | ", context.Logs)}");
    }

    [TestMethod]
    public async Task Outcome_ReadsFailed_WhenANodeEndsTheFlowWithAnError()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        var context = new RuntimeContext();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.AreEqual(RunOutcome.Unknown, new RuntimeContext().Outcome, "a session that never ran has no outcome");
        Assert.AreEqual("Stopped", context.Status);
        Assert.IsTrue(context.EndedWithError);
        Assert.AreEqual(RunOutcome.Failed, context.Outcome);
    }

    [TestMethod]
    public async Task AThrowingErrorSink_DoesNotAddAFailureToTheRun()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        var context = new RuntimeContext
        {
            ErrorSink = new DelegateExecutionErrorSink(_ => throw new InvalidOperationException("sink boom")),
        };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.AreEqual(RunOutcome.Failed, context.Outcome, "the run's own failure stays the headline");
        Assert.IsTrue(context.Logs.Any(l => l.Contains("[ErrorSink]", StringComparison.Ordinal)),
            $"the sink's own failure has to be visible somewhere; got: {string.Join(" | ", context.Logs)}");
    }
}
