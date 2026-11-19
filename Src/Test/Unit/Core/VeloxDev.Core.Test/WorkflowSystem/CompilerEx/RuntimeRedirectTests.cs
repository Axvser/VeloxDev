using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// Redirect contract: an in-node Error/Warn/exception is treated as a request to fall back to an upstream node.
/// Implementing IRedirectable with an upstream target re-runs the whole graph from that node's compile order, skipping
/// nodes before it; a target that is not upstream is ignored; a target that is the router itself only re-routes
/// without recomputing; without IRedirectable the status drops to -1; over the limit the run aborts.
/// </summary>
[TestClass]
public class RuntimeRedirectTests
{
    private static bool OnAttempt(ITaskContext ctx, int attempt)
        => ctx is IRuntimeContext rc && rc.Attempt == attempt;

    [TestMethod]
    public async Task RedirectToPredecessor_SkipsPrefixOnRerun_CountsNodesPerPass()
    {
        // a(0) → b(1) → c(2)。c 第一次抛错并请求回到 b(Order=1);第二次正常完成。
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        var c = new RedirectableNode("c")
        {
            Handler = (ctx, _) =>
            {
                if (OnAttempt(ctx, 1)) throw new InvalidOperationException("c wants to go back");
                return "C";
            },
            Resolve = _ => b, // 回到 b（上游节点）
        };
        ProbeGraph.Wire(a, b);
        ProbeGraph.Wire(b, c);
        var graph = ProbeGraph.Compile(a);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual(2, session.Attempt, "graph was re-run once for the redirect");
        Assert.HasCount(1, a.Calls, "a is before the redirect target → contract-preserved prefix, driven only on pass 1");
        Assert.HasCount(2, b.Calls, "b is the redirect target → re-executed on pass 2");
        Assert.HasCount(2, c.Calls, "c requested the redirect and is re-executed on pass 2");
        Assert.IsFalse(session.EndedWithError, "a handled redirect does not end the flow as an error");
    }

    /// <summary>
    /// The skipped prefix does not re-run, but the target still reads the chain's payload. Its input must be the
    /// output the last skipped node produced, not whatever the previous pass happened to leave behind (usually
    /// null after the failing node reported an error).
    /// </summary>
    [TestMethod]
    public async Task RedirectTarget_ReceivesTheSkippedPrefixsOutput_NotTheLastPassesNull()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        var c = new RedirectableNode("c")
        {
            Handler = (ctx, _) =>
            {
                if (OnAttempt(ctx, 1)) throw new InvalidOperationException("c wants to go back");
                return "C";
            },
            Resolve = _ => b,
        };
        ProbeGraph.Wire(a, b);
        ProbeGraph.Wire(b, c);
        var graph = ProbeGraph.Compile(a);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual(2, session.Attempt);
        Assert.HasCount(2, b.Calls);
        Assert.AreEqual("A", b.Calls[1].Data,
            "the target is driven with the skipped prefix's output, exactly as it was on the first pass");
    }

    /// <summary>
    /// A redirect back to the graph's first node has no skipped prefix to restore from, so the target must start
    /// from the run's original seed again — not from whatever the failing pass left in Data.
    /// </summary>
    [TestMethod]
    public async Task RedirectToTheFirstNode_ReplaysTheRunsSeedPayload()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new RedirectableNode("b")
        {
            Handler = (ctx, _) =>
            {
                if (OnAttempt(ctx, 1)) throw new InvalidOperationException("b wants to go back");
                return "B";
            },
            Resolve = _ => a,
        };
        ProbeGraph.Wire(a, b);
        var graph = ProbeGraph.Compile(a);

        var session = await ProbeGraph.RunAsync(graph, seed: "SEED");

        Assert.AreEqual(2, session.Attempt);
        Assert.HasCount(2, a.Calls);
        Assert.AreEqual("SEED", a.Calls[1].Data, "the first node is re-driven with the run's seed");
    }

    [TestMethod]
    public async Task RedirectTargetNotAnUpstreamNode_IsIgnored_FlowCompletesSinglePass()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var c = new ProbeNode("c") { Handler = (_, _) => "C" };
        var b = new RedirectableNode("b") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        ProbeGraph.Wire(a, b);
        ProbeGraph.Wire(b, c);
        b.Resolve = _ => c; // 下游节点：不是上游，回退没有意义
        var graph = ProbeGraph.Compile(a);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual(1, session.Attempt, "invalid redirect target must not trigger a re-run");
        Assert.AreEqual("Completed", session.Status, "flow continues past an ignored redirect request");
        Assert.IsFalse(session.EndedWithError);
        Assert.HasCount(1, a.Calls);
        Assert.HasCount(1, b.Calls);
        Assert.HasCount(1, c.Calls, "the chain carries on after the ignored request");
    }

    [TestMethod]
    public async Task RedirectToRouter_ReroutesOnly_WithoutRecomputingRouter()
    {
        // 动态 Router(0) 分支 A:[a], B:[b]。a 第一次跑抛错并请求回到 Router;重跑时把选中键改成 B。
        var router = new RouterNode("router") { CompileMode = RouterCompileMode.Dynamic, Selection = "A" };
        var a = new RedirectableNode("a")
        {
            Handler = (ctx, _) =>
            {
                if (OnAttempt(ctx, 1)) throw new InvalidOperationException("switch branch");
                return "A";
            },
            Resolve = _ =>
            {
                router.Selection = "B"; // 重跑时改选 B(动态分支运行期再决)
                return router;           // 目标是 Router 自身（上游节点）
            },
        };
        var b = new ProbeNode("b") { Handler = (_, _) => "B" };
        router.RouteTable["A"] = [a];
        router.RouteTable["B"] = [b];
        ProbeGraph.Wire(router, a);
        ProbeGraph.Wire(router, b);
        var graph = ProbeGraph.Compile(router);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual(2, session.Attempt);
        Assert.HasCount(1, router.Calls, "re-route-only must not re-drive the router on pass 2");
        Assert.HasCount(1, a.Calls, "branch A ran on pass 1 only");
        Assert.HasCount(1, b.Calls, "branch B ran after the re-route on pass 2");
        Assert.AreEqual("B", session.BranchKey, "branch re-selection must honour the runtime key");
    }

    /// <summary>
    /// A redirect aimed at a node <b>inside</b> a branch, i.e. one the router precedes. The branch must be entered
    /// with the router left alone: it sits before the target, so it is the preserved prefix. This used to be the
    /// case no test covered — the branch was skipped wholesale, the log said `Redirecting to #N`, and the second
    /// pass drove nothing at all.
    /// </summary>
    [TestMethod]
    public async Task RedirectIntoABranch_EntersIt_AndDrivesFromTheTargetInside()
    {
        // Router(0) 分支 A: x(1) → y(2)。y 第一次抛错并请求回到 x(Order=1)—— 目标落在分支内部。
        var router = new RouterNode("router") { CompileMode = RouterCompileMode.Dynamic, Selection = "A" };
        var x = new ProbeNode("x") { Handler = (_, _) => "X" };
        var y = new RedirectableNode("y");
        router.RouteTable["A"] = [x];
        ProbeGraph.Wire(router, x);
        ProbeGraph.Wire(x, y);
        var graph = ProbeGraph.Compile(router);

        var xOrder = (x as ICompileTimeAware)?.CompileContext?.Order;
        Assert.AreEqual(1, xOrder, "precondition: the target sits after the router, inside its branch");
        y.Handler = (ctx, _) =>
        {
            if (OnAttempt(ctx, 1)) throw new InvalidOperationException("go back to x");
            return "Y";
        };
        y.Resolve = _ => x;

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual(2, session.Attempt, "the redirect was accepted, so the graph was walked twice");
        Assert.HasCount(2, x.Calls, "the target is inside the branch — pass 2 must drive it again");
        Assert.HasCount(2, y.Calls, "and the node behind it too");
        Assert.HasCount(1, router.Calls, "the router is before the target: preserved prefix, not driven again");
        Assert.AreEqual("Completed", session.Status);
    }

    /// <summary>
    /// The same restoration through a router. The router is skipped as part of the prefix, and the first node of
    /// the branch must still see the router's output — the payload it received on the first pass.
    /// </summary>
    [TestMethod]
    public async Task RedirectIntoABranch_RestoresTheRoutersOutputAsTheBranchesPayload()
    {
        var router = new RouterNode("router")
        {
            Handler = (_, _) => "ROUTED",
            CompileMode = RouterCompileMode.Dynamic,
            Selection = "A",
        };
        var x = new ProbeNode("x");
        var y = new RedirectableNode("y");
        router.RouteTable["A"] = [x];
        ProbeGraph.Wire(router, x);
        ProbeGraph.Wire(x, y);
        var graph = ProbeGraph.Compile(router);

        y.Handler = (ctx, _) =>
        {
            if (OnAttempt(ctx, 1)) throw new InvalidOperationException("go back to x");
            return "Y";
        };
        y.Resolve = _ => x;

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual(2, session.Attempt);
        Assert.HasCount(2, x.Calls);
        Assert.AreEqual("ROUTED", x.Calls[1].Data,
            "the skipped router's output is the branch's payload on the re-run");
    }

    [TestMethod]
    public async Task HostRedirect_PlacesANodeThatHasNoRedirectContract()
    {
        // b does not implement IRedirectable. The host policy is the only thing that can place it.
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b")
        {
            Handler = (ctx, _) =>
            {
                if (OnAttempt(ctx, 1)) throw new InvalidOperationException("b failed");
                return "B";
            },
        };
        ProbeGraph.Wire(a, b);
        var graph = ProbeGraph.Compile(a);

        var session = new RuntimeContext
        {
            HostRedirect = (_, failing) => ReferenceEquals(failing, b) ? a : null,
        };

        await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);

        Assert.AreEqual("Completed", session.Status);
        Assert.AreEqual(2, session.Attempt, "the host fallback caused one re-run");
        Assert.HasCount(2, a.Calls, "the target and everything after it re-runs");
        Assert.HasCount(2, b.Calls);
        Assert.IsFalse(session.EndedWithError);
    }

    [TestMethod]
    public async Task HostRedirect_IsNotAskedWhenTheNodeHasItsOwnContract()
    {
        // A node that implements IRedirectable owns the answer, including "I decline" (null → carry on).
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new RedirectableNode("b")
        {
            Handler = (_, _) => throw new InvalidOperationException("boom"),
            Resolve = _ => null,
        };
        ProbeGraph.Wire(a, b);
        var graph = ProbeGraph.Compile(a);

        var asked = 0;
        var session = new RuntimeContext
        {
            HostRedirect = (_, _) => { asked++; return a; },
        };

        await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);

        Assert.AreEqual(0, asked, "the node's own contract must not be overridden");
        Assert.AreEqual(1, session.Attempt);
        Assert.AreEqual("Completed", session.Status);
        Assert.IsFalse(session.EndedWithError);
    }

    [TestMethod]
    public async Task AnInvalidHostRedirect_EndsTheFlowInsteadOfBeingIgnored()
    {
        // A node's invalid target is ignored and the chain carries on. A host's invalid target is a host bug:
        // carrying on would drop the error that raised the question.
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        var c = new ProbeNode("c") { Handler = (_, _) => "C" };
        ProbeGraph.Wire(a, b);
        ProbeGraph.Wire(b, c);
        var graph = ProbeGraph.Compile(a);

        var session = new RuntimeContext
        {
            HostRedirect = (_, _) => c, // downstream, never a valid fallback
        };

        await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);

        Assert.IsTrue(session.EndedWithError);
        Assert.AreEqual(1, session.Attempt, "an invalid target must not start another pass");
        Assert.HasCount(0, c.Calls, "the chain must not carry on");
    }

    [TestMethod]
    public async Task AHostRedirectThatThrows_EndsTheFlowAsARedirectFailure()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new ProbeNode("b") { Handler = (_, _) => throw new InvalidOperationException("boom") };
        ProbeGraph.Wire(a, b);
        var graph = ProbeGraph.Compile(a);

        var session = new RuntimeContext
        {
            HostRedirect = (_, _) => throw new InvalidOperationException("policy bug"),
        };

        await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);

        Assert.IsTrue(session.EndedWithError);
        Assert.AreEqual(-1, session.CurrentOrder);
    }

    [TestMethod]
    public void RedirectLoopsExceedingLimit_AbortWithException()
    {
        // b 每次跑都抛错并请求回到 a → 每次都被接受 → 无限重跑,直到超过 MaxRedirects 上限。
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new RedirectableNode("b")
        {
            Handler = (_, _) => throw new InvalidOperationException("always redirect"),
            Resolve = _ => a,
        };
        ProbeGraph.Wire(a, b);
        var graph = ProbeGraph.Compile(a);

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ProbeGraph.RunAsync(graph).GetAwaiter().GetResult());
        StringAssert.Contains(ex.Message, "50", "abort message must cite the redirect limit");
        Assert.IsTrue(b.Calls.Count > 50, "the redirect loop must have attempted far more than one pass");
    }
}
