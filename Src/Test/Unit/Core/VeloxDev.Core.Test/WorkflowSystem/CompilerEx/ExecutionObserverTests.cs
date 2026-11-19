using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// The observation seam: what a run reports, in what order, and the one thing it must never do — let a broken
/// observer change the run.
/// </summary>
[TestClass]
public class ExecutionObserverTests
{
    private static (RuntimeContext Context, List<ExecutionObservation> Seen) Observing()
    {
        var seen = new List<ExecutionObservation>();
        return (new RuntimeContext { Observer = new DelegateExecutionObserver(seen.Add) }, seen);
    }

    private static ExecutionObservationKind[] Kinds(List<ExecutionObservation> seen)
        => [.. seen.Select(o => o.Kind)];

    [TestMethod]
    public async Task AChain_ReportsRunAndNodeStartAndSuccess_InDriveOrder()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(a, b);
        var (context, seen) = Observing();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        ExecutionObservationKind[] expected =
        [
            ExecutionObservationKind.RunStarted,
            ExecutionObservationKind.NodeStarted, ExecutionObservationKind.NodeSucceeded,
            ExecutionObservationKind.NodeStarted, ExecutionObservationKind.NodeSucceeded,
            ExecutionObservationKind.RunEnded,
        ];
        CollectionAssert.AreEqual(expected, Kinds(seen),
            $"a chain reports each node's start and success between the run's own two events; got: {string.Join(", ", Kinds(seen))}");

        Assert.IsNull(seen[0].Node, "the run-level observations carry no node");
        Assert.IsNull(seen[^1].Node);
        Assert.AreSame(a, seen[1].Node, "the first drive is the chain's first node");
        Assert.AreSame(b, seen[3].Node);
        Assert.AreEqual(1, seen[1].Attempt, "observations carry the pass they belong to");
        Assert.AreEqual(RunOutcome.Completed, context.Outcome, "RunEnded is reported after the run has an outcome");
    }

    [TestMethod]
    public async Task AFanOut_ReportsEveryBranch()
    {
        var s = new ProbeNode("s") { Handler = (_, _) => "SRC" };
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(s, a);
        ProbeGraph.Wire(s, b);
        var (context, seen) = Observing();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(s), context, CancellationToken.None);

        Assert.AreEqual(2, seen.Count(o => o.Kind == ExecutionObservationKind.BranchStarted),
            $"each branch announces itself; got: {string.Join(", ", Kinds(seen))}");
        Assert.AreEqual(3, seen.Count(o => o.Kind == ExecutionObservationKind.NodeStarted),
            "the fan-out source and both branch nodes are driven");
        Assert.AreEqual(ExecutionObservationKind.RunStarted, seen[0].Kind);
        Assert.AreEqual(ExecutionObservationKind.RunEnded, seen[^1].Kind);
    }

    [TestMethod]
    public async Task ANodeThatThrows_IsReportedAsFailed()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        var (context, seen) = Observing();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        var failed = seen.Single(o => o.Kind == ExecutionObservationKind.NodeFailed);
        Assert.AreSame(a, failed.Node);
        Assert.AreEqual("boom", failed.Detail, "the failure message travels with the observation");
        Assert.IsTrue(failed.Elapsed >= TimeSpan.Zero, "a duration is reported with the failure");
        Assert.AreEqual(ExecutionObservationKind.RunEnded, seen[^1].Kind, "a run that ends badly still closes its own story");
    }

    /// <summary>
    /// An observer is a diagnostic. It gets the run's token, it runs on the thread driving the run, and whatever it
    /// does with either must not reach the run: a throw becomes one log line, exactly as the log writer's failure
    /// notification does not become a node failure.
    /// </summary>
    [TestMethod]
    public async Task AThrowingObserver_ChangesNothingAboutTheRun()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(a, b);
        var context = new RuntimeContext
        {
            Observer = new DelegateExecutionObserver(_ => throw new InvalidOperationException("observer boom")),
        };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.AreEqual("Completed", context.Status);
        Assert.AreEqual(RunOutcome.Completed, context.Outcome);
        Assert.HasCount(1, a.Calls);
        Assert.HasCount(1, b.Calls);
        Assert.IsTrue(context.Logs.Any(l => l.Contains("[Observer]", StringComparison.Ordinal)),
            $"the swallowed throw has to be visible somewhere; got: {string.Join(" | ", context.Logs)}");
    }
}
