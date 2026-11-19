using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// <see cref="INodeRetryPolicy"/>: a thrown exception is offered to the policy, a redirect request is not, and a
/// retry is never mistaken for a new pass over the graph — <c>Attempt</c> both counts the passes and stamps the
/// output registry, so moving it would silently change what join points aggregate.
/// </summary>
[TestClass]
public class ExecutionRetryTests
{
    [TestMethod]
    public async Task AThrownException_IsDrivenAgainUntilThePolicyStops()
    {
        var a = new ProbeNode("a");
        var tries = 0;
        a.Handler = (_, _) =>
        {
            if (++tries < 3) throw new InvalidOperationException($"boom {tries}");
            return "A";
        };
        var context = new RuntimeContext { RetryPolicy = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 0) };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.AreEqual(3, tries, "the node gets the attempts the policy allows");
        Assert.HasCount(3, a.Calls);
        Assert.AreEqual("Completed", context.Status);
        Assert.AreEqual("A", context.Data);
        Assert.AreEqual(1, context.Attempt, "a retry is not a pass over the graph");
        Assert.AreEqual(RunOutcome.Completed, context.Outcome);
        Assert.IsTrue(context.Logs.Any(l => l.Contains("[Retry 1]", StringComparison.Ordinal)),
            $"the retries have to be readable afterwards; got: {string.Join(" | ", context.Logs)}");
        Assert.IsFalse(context.Logs.Any(l => l.Contains("[Error]", StringComparison.Ordinal)),
            $"an attempt that is going to be retried is not an error yet; got: {string.Join(" | ", context.Logs)}");
    }

    [TestMethod]
    public async Task APolicyThatGivesUp_LeavesTheFailureOnTheEnginesOrdinaryPath()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        var context = new RuntimeContext { RetryPolicy = new ExponentialBackoffRetry(maxAttempts: 2, baseDelayMs: 0) };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.HasCount(2, a.Calls, "exactly the attempts the policy allows, and no more");
        Assert.AreEqual(1, context.Attempt);
        Assert.IsTrue(context.Logs.Any(l => l.Contains("[Error]", StringComparison.Ordinal) && l.Contains("boom")),
            "the last failure still reaches the log exactly as it did before the policy existed");
        Assert.IsTrue(context.EndedWithError, "giving up on the retries leaves the failure at error level");
        Assert.AreEqual("Stopped", context.Status);
        Assert.AreEqual(RunOutcome.Failed, context.Outcome);
    }

    /// <summary>
    /// <c>Error()</c>/<c>Warn()</c> is control flow the node chose, not a failure to try again — a node that asks
    /// for a redirect and then throws on the way out must not be driven a second time, and its redirect must still
    /// be resolved.
    /// </summary>
    [TestMethod]
    public async Task ANodeThatAsksForARedirect_IsNotRetried()
    {
        var resolved = 0;
        var a = new RedirectableNode("a")
        {
            Resolve = _ => { resolved++; return null; },   // null = 继续；这条测的是驱动次数
        };
        a.Handler = (ctx, _) =>
        {
            ((IRuntimeContext)ctx).Error("deliberate");
            throw new InvalidOperationException("on the way out");
        };
        var context = new RuntimeContext { RetryPolicy = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 0) };

        await new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, CancellationToken.None);

        Assert.HasCount(1, a.Calls, "a deliberate redirect is not a failure to retry");
        Assert.AreEqual(1, resolved, "the redirect request must still reach IRedirectable");
        Assert.IsFalse(context.Logs.Any(l => l.Contains("[Retry", StringComparison.Ordinal)),
            $"nothing may have been retried; got: {string.Join(" | ", context.Logs)}");
    }

    [TestMethod]
    public async Task CancellingDuringTheRetryWait_EndsTheRun()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        var retried = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var context = new RuntimeContext
        {
            // 被策略自己的 maxDelayMs 压到 5 秒：够在等待里取消，又短到实现坏掉时是失败而不是挂住整个测试。
            RetryPolicy = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 30_000),
            Observer = new DelegateExecutionObserver(o =>
            {
                if (o.Kind == ExecutionObservationKind.NodeRetried) retried.TrySetResult(true);
            }),
        };

        var run = new RuntimeEngine().RunAsync(ProbeGraph.Compile(a), context, cts.Token);
        await retried.Task;   // 运行内部发信号，因此不轮询、不抢跑
        cts.Cancel();

        var finished = await Task.WhenAny(run, Task.Delay(3000));
        Assert.AreSame(run, finished, "the wait between attempts has to honour the run's token");
        Assert.AreEqual("Stopped", context.Status);
        Assert.AreEqual(RunOutcome.Cancelled, context.Outcome);
        Assert.HasCount(1, a.Calls, "the node must not be driven again after the run was cancelled");
    }
}
