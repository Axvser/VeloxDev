using System.Runtime.CompilerServices;
using VeloxDev.AspectOriented;

namespace VeloxDev.Core.Test.AspectOriented;

/// <summary>
/// The compile-time proxy: that it is ordinary generated code, and that it runs the three stages exactly the way
/// the reflective proxy used to.
/// </summary>
/// <remarks>
/// The semantics asserted here — stage order, what <c>coverage</c> replaces, what <c>end</c> receives, whose
/// return value is discarded — were previously only exercised by the two demo apps, by hand. They are the
/// contract a proxy swap has to keep, so they are pinned here rather than left to a demo.
/// </remarks>
[TestClass]
public class AopProxyTests
{
    [TestMethod]
    public void TheProxyIsGeneratedIntoTheConsumersOwnAssembly()
    {
        var fixture = new AopFixture();

        var proxy = fixture.Aop();

        // 这条是本方案的核心不变量。DispatchProxy 在运行期用 Reflection.Emit 造的类型**不在**消费者程序集里，
        // 所以只有它能把「代理是编译期产物」钉住，也只有它能防这条路径被换回反射实现。
        Assert.AreSame(
            typeof(AopFixture).Assembly,
            proxy.GetType().Assembly,
            "the proxy must be emitted at compile time, not created by Reflection.Emit at runtime");
    }

    [TestMethod]
    public void OneProxyPerInstance_AndADifferentOnePerTarget()
    {
        var first = new AopFixture();
        var second = new AopFixture();

        Assert.AreSame(first.Aop(), first.Aop(), "the proxy is cached per target instance");
        Assert.AreNotSame(first.Aop(), second.Aop(), "and never shared between instances");
    }

    [TestMethod]
    public void AMethodsThreeStagesRunInOrder_AndEndsReturnIsDiscarded()
    {
        var fixture = new AopFixture();
        var proxy = fixture.Aop();

        proxy.SetProxy(
            ProxyMembers.Method,
            nameof(AopFixture.Add),
            (_, _) => { fixture.Trace.Add("start"); return "from-start"; },
            null,
            (_, previous) => { fixture.Trace.Add($"end({previous})"); return "from-end"; });

        var result = proxy.Add(2, 3);

        CollectionAssert.AreEqual(
            new[] { "start", "body:Add(2,3)", "end(5)" },
            fixture.Trace,
            "start before the body, end after it, and end receives the body's result");
        Assert.AreEqual(5, result, "and end's own return value is discarded — the body's result stands");
    }

    [TestMethod]
    public void ACoverageHookReplacesTheBodyEntirely()
    {
        var fixture = new AopFixture();
        var proxy = fixture.Aop();

        proxy.SetProxy(
            ProxyMembers.Method,
            nameof(AopFixture.Add),
            null,
            (_, _) => 42,
            null);

        var result = proxy.Add(2, 3);

        Assert.AreEqual(42, result);
        Assert.IsEmpty(fixture.Trace, "with a coverage hook the member's own body must not run");
    }

    [TestMethod]
    public void AGetterAndASetterTakeSeparateHooks()
    {
        var fixture = new AopFixture();
        var proxy = fixture.Aop();

        proxy.SetProxy(ProxyMembers.Getter, "Title", (_, _) => { fixture.Trace.Add("get"); return null; }, null, null);
        proxy.SetProxy(ProxyMembers.Setter, "Title", (_, _) => { fixture.Trace.Add("set"); return null; }, null, null);

        _ = proxy.Title;
        CollectionAssert.AreEqual(new[] { "get" }, fixture.Trace, "the getter hook ran");

        fixture.Trace.Clear();
        proxy.Title = "written";
        CollectionAssert.AreEqual(new[] { "set" }, fixture.Trace, "and the setter's is a separate member");
        Assert.AreEqual("written", proxy.Title, "the write reached the target's own property");
    }

    [TestMethod]
    public void AVoidMethodRunsAndItsEndStageReceivesNull()
    {
        var fixture = new AopFixture();
        var proxy = fixture.Aop();

        string? seen = "not-set";
        proxy.SetProxy(
            ProxyMembers.Method,
            nameof(AopFixture.Ping),
            null,
            null,
            (_, previous) => { seen = previous as string ?? "(null)"; return null; });

        proxy.Ping();

        CollectionAssert.AreEqual(new[] { "body:Ping" }, fixture.Trace);
        Assert.AreEqual("(null)", seen, "a void member has no result, so end receives null");
    }

    [TestMethod]
    public void AMethodWithArguments_HandsThemToTheHooksAsAnArray()
    {
        var fixture = new AopFixture();
        var proxy = fixture.Aop();

        object?[]? captured = null;
        proxy.SetProxy(
            ProxyMembers.Method,
            nameof(AopFixture.Echo),
            (parameters, _) => { captured = parameters; return null; },
            null,
            null);

        _ = proxy.Echo("hello");

        Assert.IsNotNull(captured);
        Assert.HasCount(1, captured);
        Assert.AreEqual("hello", captured[0]);
    }

    [TestMethod]
    public void InstallingAspectsOnAnUnknownMemberThrows()
    {
        var fixture = new AopFixture();
        var proxy = fixture.Aop();

        // 以前这是静默无效 —— 名字匹配不到就什么都不做，切面永远不触发且没有任何提示。
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => proxy.SetProxy(ProxyMembers.Method, "NoSuchMember", null, null, null));
    }

    /// <summary>
    /// A hand-written <see cref="IAspectOriented"/>, i.e. one that is not a generated proxy.
    /// </summary>
    /// <remarks>
    /// Nothing in the library produces one; it exists because <see cref="IAspectOriented"/> is an empty marker
    /// anyone can implement, and <c>SetProxy</c> has to refuse such an object rather than write aspects that no
    /// proxy will ever run.
    /// </remarks>
    private sealed class HandWrittenAspectTarget : IAspectOriented
    {
    }

    [TestMethod]
    public void InstallingAspectsOnSomethingThatIsNotAProxyThrows()
    {
        // 装在真身上是切面静默不生效的失效形态。对生成的代理，这一条现在是**编译期**挡住的 ——
        // 用户类不再实现那个接口，SetProxy 的 where 约束过不去。这里钉的是剩下那一半：
        // 手写的 IAspectOriented 实现（空标记接口，谁都能实现）必须被运行期拒绝，而不是被默默忽略。
        IAspectOriented notAProxy = new HandWrittenAspectTarget();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => notAProxy.SetProxy(ProxyMembers.Method, "Anything", null, null, null));
    }

    [TestMethod]
    public void ReinstallingReplacesRatherThanStacks()
    {
        var fixture = new AopFixture();
        var proxy = fixture.Aop();

        proxy.SetProxy(ProxyMembers.Method, nameof(AopFixture.Add), (_, _) => { fixture.Trace.Add("first"); return null; }, null, null);
        proxy.SetProxy(ProxyMembers.Method, nameof(AopFixture.Add), (_, _) => { fixture.Trace.Add("second"); return null; }, null, null);

        _ = proxy.Add(1, 1);

        CollectionAssert.AreEqual(
            new[] { "second", "body:Add(1,1)" },
            fixture.Trace,
            "the second install replaces the first rather than chaining to it");
    }

    [TestMethod]
    public void AMemberWithNoAspectsCallsStraightThrough()
    {
        var fixture = new AopFixture();
        var proxy = fixture.Aop();

        Assert.AreEqual(7, proxy.Add(3, 4));
        Assert.AreEqual("echoed", proxy.Echo("echoed"));

        CollectionAssert.AreEqual(new[] { "body:Add(3,4)", "body:Echo(echoed)" }, fixture.Trace);
    }

    [TestMethod]
    public void GetTargetReversesTheProxy()
    {
        var fixture = new AopFixture();

        Assert.AreSame(fixture, Aop.GetTarget<AopFixture>(fixture.Aop()));
    }

    /// <summary>
    /// A proxy and its target become collectable together.
    /// </summary>
    /// <remarks>
    /// Both directions of the proxy-to-target mapping are weak, so once the target is unreachable the proxy goes
    /// with it. The reflective design kept a static <c>Dictionary</c> from proxy to id and another from id to
    /// proxy, which rooted every proxy — and, through its target field, every target — for the life of the
    /// process. This pins the replacement, because re-adding a static registry would not fail any other test.
    /// </remarks>
    [TestMethod]
    public void AProxyIsCollectableOnceItsTargetIs()
    {
        var (proxy, target) = MakeUnreachableProxy();

        for (int attempt = 0; attempt < 3 && (proxy.IsAlive || target.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(target.IsAlive, "nothing outside the proxy may root the target");
        Assert.IsFalse(proxy.IsAlive, "and no static registry may root the proxy");
    }

    // 必须是不内联的独立方法：放在用例体内，代理会被 Debug 下的 JIT 保活到方法结束，弱引用永远为真。
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Proxy, WeakReference Target) MakeUnreachableProxy()
    {
        var fixture = new AopFixture();
        var proxy = fixture.Aop();
        proxy.SetProxy(ProxyMembers.Method, nameof(AopFixture.Add), (_, _) => null, null, null);
        return (new WeakReference(proxy), new WeakReference(fixture));
    }
}
