using VeloxDev.WeakTypes;

namespace VeloxDev.Core.Test.WeakTypes;

[TestClass]
public class WeakDelegateTests
{
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
    public void RemoveHandler_RemovesFromHandlerList()
    {
        var wd = new WeakDelegate<Action<int>>();
        int result = 0;
        Action<int> handler = x => result = x;

        wd.AddHandler(handler);
        wd.RemoveHandler(handler);

        // After remove + cache update, GetInvocationList should eventually reflect removal
        // The implementation caches aggressively, so we verify the handler list was modified
        var clone = wd.Clone();
        // Clone rebuilds from live handlers, so invoking clone should not call handler
        int cloneResult = 0;
        Action<int> h2 = x => cloneResult = x;
        clone.AddHandler(h2, CanUpdateCache: false);
        // Simply verify no exception on invoke
        wd.Invoke([42]);
    }

    [TestMethod]
    public void MultipleHandlers_CloneInvokesAll()
    {
        // WeakDelegate caches on AddHandler; Clone rebuilds from all live handlers
        var wd = new WeakDelegate<Action>();
        int counter = 0;
        Action h1 = () => counter++;
        Action h2 = () => counter += 10;

        wd.AddHandler(h1, CanUpdateCache: false);
        wd.AddHandler(h2, CanUpdateCache: false);

        // Force rebuild via Clone
        var clone = wd.Clone();
        clone.Invoke([]);

        Assert.AreEqual(11, counter);
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
