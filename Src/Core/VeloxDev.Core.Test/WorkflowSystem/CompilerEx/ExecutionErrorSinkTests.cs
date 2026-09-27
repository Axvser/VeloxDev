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
/// <para>
/// Two ways in, and the difference is the point: the engine reports what it sees itself (a node that threw, a host
/// contract that gave up, the cap, a cancellation), a node reports its own through
/// <see cref="IRuntimeContext.ErrorAsync"/>/<see cref="IRuntimeContext.WarnAsync"/>.
/// </para>
/// </summary>
[TestClass]
public class ExecutionErrorSinkTests
{
    private static (RuntimeContext Context, List<ExecutionError> Records) Recording()
    {
        var records = new List<ExecutionError>();
        return (new RuntimeContext { ErrorSink = new DelegateExecutionErrorSink(records.Add) }, records);
    }

    [TestMethod]
    public async Task ANodeThatThrows_ReachesTheSinkWithTheNodeAndTheException()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        ProbeGraph.Wire(a, b);
        var (context, records) = Recording();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.HasCount(1, records, "one drive, one failure — what the engine decides to do about it is not a second one");
        Assert.AreEqual(ExecutionFailurePhase.Node, records[0].Phase);
        Assert.AreSame(b, records[0].Node);
        Assert.AreEqual("boom", records[0].Message);
        Assert.IsInstanceOfType<InvalidOperationException>(records[0].Error);
        Assert.AreEqual(ExecutionReportLevel.Error, records[0].Level);
        Assert.AreEqual(1, records[0].Attempt);
        Assert.AreEqual(1, records[0].Order, "the node's compile order travels with the record");
    }

    [TestMethod]
    public async Task ANodeReportedError_ReachesTheSink_AsTheNodeThatMadeIt()
    {
        var a = new ProbeNode("a");
        a.AsyncHandler = async (ctx, _) =>
        {
            await ((IRuntimeContext)ctx).ErrorAsync("python is not installed");
            return "A";
        };
        var (context, records) = Recording();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.HasCount(1, records);
        Assert.AreEqual(ExecutionFailurePhase.Node, records[0].Phase);
        Assert.AreSame(a, records[0].Node, "a record without the node that made it is not worth handing to a host");
        Assert.AreEqual("python is not installed", records[0].Message);
        Assert.AreEqual(ExecutionReportLevel.Error, records[0].Level);
        Assert.AreEqual(0, records[0].Order);
        Assert.IsNull(records[0].Error, "the node reported a message, not an exception");
        Assert.AreEqual("Completed", context.Status, "and the run carries on");
    }

    [TestMethod]
    public async Task ANodeReportedWarning_IsRecorded_AsAWarning()
    {
        var a = new ProbeNode("a");
        a.AsyncHandler = async (ctx, _) =>
        {
            await ((IRuntimeContext)ctx).WarnAsync("script is empty");
            return null;
        };
        var (context, records) = Recording();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.HasCount(1, records);
        Assert.AreEqual(ExecutionReportLevel.Warning, records[0].Level, "a warning is not a failure");
        Assert.AreSame(a, records[0].Node);
        Assert.IsTrue(context.Logs.Any(l => l.Contains("[Warning] script is empty", StringComparison.Ordinal)),
            $"the line is still written; got: {string.Join(" | ", context.Logs)}");
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
        var (context, records) = Recording();

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
    public async Task Outcome_ReadsUnknownUntilARunEnds()
    {
        Assert.AreEqual(RunOutcome.Unknown, new RuntimeContext().Outcome, "a session that never ran has no outcome");

        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var context = new RuntimeContext();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.AreEqual(RunOutcome.Completed, context.Outcome);
    }

    [TestMethod]
    public async Task AThrowingErrorSink_DoesNotAddAFailureToTheRun()
    {
        var a = new ProbeNode("a");
        a.AsyncHandler = async (ctx, _) =>
        {
            await ((IRuntimeContext)ctx).ErrorAsync("boom");
            return "A";
        };
        var context = new RuntimeContext
        {
            ErrorSink = new DelegateExecutionErrorSink(_ => throw new InvalidOperationException("sink boom")),
        };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.AreEqual(RunOutcome.Completed, context.Outcome, "a broken sink must not reach the run that reported");
        Assert.IsTrue(context.Logs.Any(l => l.Contains("[ErrorSink]", StringComparison.Ordinal)),
            $"the sink's own failure has to be visible somewhere; got: {string.Join(" | ", context.Logs)}");
    }
}
