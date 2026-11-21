using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// What happens when a contract the <b>host</b> implements throws. The router and the redirect resolver decide where
/// the run goes, so their failure leaves no answer and ends it; the optional <see cref="IRuntimeAware"/> notification
/// decides nothing, so its failure is logged and the drive continues.
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
    /// IRuntimeAware is a notification, not a dependency: a node whose attach throws is no worse off than one that
    /// does not implement it. The failure is logged, the node body still runs, and the chain continues.
    /// </summary>
    [TestMethod]
    public async Task AnAttachThatThrows_OnlySkipsTheNotification_TheRunContinues()
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
        Assert.HasCount(1, a.Calls, "the node body runs even though its notification failed");
        Assert.HasCount(1, b.Calls, "the chain continues past it");
        Assert.AreEqual("Completed", context.Status);
        Assert.IsFalse(context.EndedWithError);
        Assert.IsTrue(context.Logs.Any(l => l.Contains("attach boom", StringComparison.Ordinal)),
            $"the notification failure has to reach the log; got: {string.Join(" | ", context.Logs)}");
    }
}
