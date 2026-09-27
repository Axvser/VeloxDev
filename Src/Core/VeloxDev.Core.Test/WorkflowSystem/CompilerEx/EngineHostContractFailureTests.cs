using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// What happens when a contract the <b>host</b> implements throws. These three are the engine's outward calls that
/// used to be unguarded, and each one failed in its own quiet way: a run whose session claimed it was still going,
/// and a node that vanished without a line in the log.
/// <para>
/// Two of them are the engine's own machinery and end the run, because there is no answer to give — a router with
/// no key has no branch to take, a redirect with no target has nowhere to go. The third rides inside the drive, so
/// it follows the node's rule instead: report it, count the drive as a null, carry on.
/// </para>
/// </summary>
[TestClass]
public class EngineHostContractFailureTests
{
    [TestMethod]
    public async Task ARouterThatThrows_EndsTheRun_InsteadOfLeavingItLookingRunning()
    {
        var router = new RouterNode("router") { CompileMode = RouterCompileMode.Dynamic, Selection = "A" };
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        router.RouteTable["A"] = [a];
        ProbeGraph.Wire(router, a);

        var graph = ProbeGraph.Compile(router);
        // 编译之后再设：编译期也会解析一次键（payload 为 null）。
        router.ResolveOverride = _ => throw new InvalidOperationException("router boom");

        var records = new List<ExecutionError>();
        var context = new RuntimeContext { ErrorSink = new DelegateExecutionErrorSink(records.Add) };

        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        Assert.AreEqual("Stopped", context.Status,
            "a host contract that throws must not leave the session saying the run is still going");
        Assert.IsTrue(context.EndedWithError);
        Assert.AreEqual(-1, context.CurrentOrder);
        Assert.AreEqual(RunOutcome.Failed, context.Outcome);
        Assert.IsEmpty(a.Calls, "without a key there is no branch to drive");
        Assert.HasCount(1, records);
        Assert.AreEqual(ExecutionFailurePhase.Router, records[0].Phase);
        Assert.AreSame(router, records[0].Node);
    }

    [TestMethod]
    public async Task ARedirectResolutionThatThrows_EndsTheRun_InsteadOfLeavingItLookingRunning()
    {
        var a = new RedirectableNode("a") { ResolveThrows = new InvalidOperationException("resolve boom") };
        a.Handler = (ctx, _) => { ((IRuntimeContext)ctx).Error("boom"); return "A"; };

        var records = new List<ExecutionError>();
        var context = new RuntimeContext { ErrorSink = new DelegateExecutionErrorSink(records.Add) };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.AreEqual("Stopped", context.Status);
        Assert.IsTrue(context.EndedWithError);
        Assert.AreEqual(-1, context.CurrentOrder);
        Assert.AreEqual(RunOutcome.Failed, context.Outcome);
        Assert.HasCount(1, records);
        Assert.AreEqual(ExecutionFailurePhase.Redirect, records[0].Phase);
        Assert.AreSame(a, records[0].Node);
    }

    /// <summary>
    /// The injection happens inside the drive, so a throw there is the drive's own failure: the node body never
    /// runs, the drive counts as null, and the chain carries on — where it used to swallow the node whole, with no
    /// line in the log and a status that looked healthy.
    /// </summary>
    [TestMethod]
    public async Task AnAttachThatThrows_IsReported_AndTheRunCarriesOnWithNull()
    {
        var s = new ProbeNode("s") { Handler = (_, _) => "S" };
        var a = new ProbeNode("a")
        {
            AttachGate = _ => throw new InvalidOperationException("attach boom"),
            Handler = (_, _) => "A",
        };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        ProbeGraph.Wire(s, a);
        ProbeGraph.Wire(a, b);

        var context = new RuntimeContext();

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(s), context, CancellationToken.None);

        Assert.HasCount(1, s.Calls);
        Assert.IsEmpty(a.Calls, "the node body must not run when the injection failed");
        Assert.HasCount(1, b.Calls, "the run carries on — the failure is reported, not fatal");
        Assert.IsNull(b.Calls[0].Data, "the failed drive counts as having produced null");
        Assert.AreEqual("Completed", context.Status);
        Assert.IsFalse(context.EndedWithError);
        Assert.IsTrue(context.Logs.Any(l => l.Contains("attach boom", StringComparison.Ordinal)),
            $"the failure has to reach the log; got: {string.Join(" | ", context.Logs)}");
    }
}
