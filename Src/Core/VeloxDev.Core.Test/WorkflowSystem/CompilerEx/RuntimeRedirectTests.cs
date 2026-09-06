using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>
/// 重定向契约:节点内 Error/Warn/异常被视为"回到更早编译状态"的请求。实现 IRedirectable 且目标为前驱 →
/// 整图按目标 Order 重跑、目标前节点跳过;目标非法(非前驱)→ 忽略继续;目标恰为路由器 → 只重选路、不重算;
/// 不实现 IRedirectable → 状态 -1;超过上限 → 中止。
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
            Resolve = _ => 1, // 回到 b
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

    [TestMethod]
    public async Task RedirectTargetNotAPredecessor_IsIgnored_FlowCompletesSinglePass()
    {
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new RedirectableNode("b")
        {
            Handler = (_, _) => throw new InvalidOperationException("boom"),
            Resolve = _ => 5, // 5 >= current Order(1) → 非法(非前驱)
        };
        ProbeGraph.Wire(a, b);
        var graph = ProbeGraph.Compile(a);

        var session = await ProbeGraph.RunAsync(graph);

        Assert.AreEqual(1, session.Attempt, "invalid redirect target must not trigger a re-run");
        Assert.AreEqual("Completed", session.Status, "flow continues past an ignored redirect request");
        Assert.IsFalse(session.EndedWithError);
        Assert.HasCount(1, a.Calls);
        Assert.HasCount(1, b.Calls);
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
                return 0;               // 目标是 Router 自身
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

    [TestMethod]
    public void RedirectLoopsExceedingLimit_AbortWithException()
    {
        // b 每次跑都抛错并请求回到 a → 每次都被接受 → 无限重跑,直到超过 MaxRedirects 上限。
        var a = new ProbeNode("a") { Handler = (_, _) => "A" };
        var b = new RedirectableNode("b")
        {
            Handler = (_, _) => throw new InvalidOperationException("always redirect"),
            Resolve = _ => 0,
        };
        ProbeGraph.Wire(a, b);
        var graph = ProbeGraph.Compile(a);

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ProbeGraph.RunAsync(graph).GetAwaiter().GetResult());
        StringAssert.Contains(ex.Message, "50", "abort message must cite the redirect limit");
        Assert.IsTrue(b.Calls.Count > 50, "the redirect loop must have attempted far more than one pass");
    }
}
