using System.Runtime.CompilerServices;
using VeloxDev.WeakTypes;

namespace VeloxDev.Core.Test.WeakTypes;

[TestClass]
public class WeakDelegateTests
{
    /// <summary>Stays reachable for the whole run, so a handler it holds is only held <em>through</em> it.</summary>
    private static readonly WeakDelegate<EventHandler> Holder = new();

    private sealed class HandlerTarget
    {
        public void OnEvent(object? sender, EventArgs e) { }
    }

    /// <summary>
    /// A subscription keeps its handler's target alive, exactly as an ordinary event would.
    /// </summary>
    /// <remarks>
    /// This is the property that makes the common spelling work, not an oversight despite the type's name:
    /// <c>effect.Update += (_, _) =&gt; …</c> creates a delegate nothing else references, so storage that was truly
    /// weak would let the next GC collect it and the animation would go quiet — no exception, no frame, no report.
    /// The holder here is static and outlives the call, so nothing else could be what keeps the target.
    /// </remarks>
    [TestMethod]
    public void ASubscriptionKeepsItsHandlerAliveLikeAnOrdinaryEvent()
    {
        var weak = SubscribeAndDrop();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.IsTrue(weak.IsAlive,
            "the combined delegate is cached, so a lambda with no other reference still fires");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeAndDrop()
    {
        var target = new HandlerTarget();
        Holder.AddHandler(target.OnEvent);
        return new WeakReference(target);
    }

    [TestMethod]
    public void AddHandler_And_Invoke_CallsHandler()
    {
        var wd = new WeakDelegate<Action<int>>();
        int result = 0;
        Action<int> handler = x => result = x;

        wd.AddHandler(handler);
        wd.Invoke([42]);

        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public void RemoveHandler_StopsInvoking()
    {
        var wd = new WeakDelegate<Action<int>>();
        int result = 0;
        Action<int> handler = x => result = x;

        wd.AddHandler(handler);
        wd.RemoveHandler(handler);

        wd.Invoke([42]);

        Assert.AreEqual(0, result);
        Assert.IsNull(wd.GetInvocationList(), "the last handler is gone, so there is nothing left to invoke");
    }

    [TestMethod]
    public void MultipleHandlers_InvokeInRegistrationOrder()
    {
        var wd = new WeakDelegate<Action>();
        var order = new List<int>();
        Action h1 = () => order.Add(1);
        Action h2 = () => order.Add(2);

        wd.AddHandler(h1);
        wd.AddHandler(h2);

        wd.Invoke([]);

        CollectionAssert.AreEqual(new[] { 1, 2 }, order);
    }

    /// <summary>
    /// None and one cost nothing to invoke — the walk is over an array and the single handler is handed back as it
    /// is. Two or more are combined, which allocates; see the type's remarks for why that trade is the one it takes.
    /// </summary>
    /// <remarks>
    /// Measured through the typed path, which is the one the frame loop uses
    /// (<c>GetInvocationList()?.Invoke(sender, e)</c>). <c>Invoke(object?[])</c> goes through <c>DynamicInvoke</c>,
    /// which allocates on its own, so it would hide the thing being measured.
    /// </remarks>
    [TestMethod]
    public void InvokingNoneOrOneDoesNotAllocate()
    {
        var none = new WeakDelegate<Action>();
        var one = new WeakDelegate<Action>();
        Action handler = () => { };
        one.AddHandler(handler);

        none.GetInvocationList()?.Invoke();
        one.GetInvocationList()?.Invoke();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            none.GetInvocationList()?.Invoke();
            one.GetInvocationList()?.Invoke();
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        GC.KeepAlive(handler);
        Assert.AreEqual(before, after, $"100 pairs of invokes allocated {after - before} bytes");
    }

    [TestMethod]
    public void AddHandler_Null_NoException()
    {
        var wd = new WeakDelegate<Action>();
        wd.AddHandler(null);
    }

    [TestMethod]
    public void Clone_ReturnsIndependentCopy()
    {
        var wd = new WeakDelegate<Action>();
        int counter = 0;
        Action handler = () => counter++;

        wd.AddHandler(handler);
        var clone = wd.Clone();

        clone.Invoke([]);
        Assert.AreEqual(1, counter);
    }

    [TestMethod]
    public void GetInvocationList_ReturnsDelegate()
    {
        var wd = new WeakDelegate<Action>();
        Action handler = () => { };
        wd.AddHandler(handler);

        var list = wd.GetInvocationList();
        Assert.IsNotNull(list);
    }
}
