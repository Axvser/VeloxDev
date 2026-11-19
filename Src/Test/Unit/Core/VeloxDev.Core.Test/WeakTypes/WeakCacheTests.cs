using VeloxDev.WeakTypes;

namespace VeloxDev.Core.Test.WeakTypes;

[TestClass]
public class WeakCacheTests
{
    [TestMethod]
    public void AddOrUpdate_And_TryGetCache_ReturnsValue()
    {
        var cache = new WeakCache<string, string>();
        var key = "key1";
        cache.AddOrUpdate(key, "value1");

        Assert.IsTrue(cache.TryGetCache(key, out var result));
        Assert.AreEqual("value1", result);
    }

    [TestMethod]
    public void TryGetCache_Missing_ReturnsFalse()
    {
        var cache = new WeakCache<string, string>();
        Assert.IsFalse(cache.TryGetCache("nonexistent", out _));
    }

    [TestMethod]
    public void AddOrUpdate_OverwritesExisting()
    {
        var cache = new WeakCache<string, string>();
        var key = "key1";
        cache.AddOrUpdate(key, "old");
        cache.AddOrUpdate(key, "new");

        Assert.IsTrue(cache.TryGetCache(key, out var result));
        Assert.AreEqual("new", result);
    }

    [TestMethod]
    public void Remove_DeletesEntry()
    {
        var cache = new WeakCache<string, string>();
        var key = "key1";
        cache.AddOrUpdate(key, "value1");
        cache.Remove(key);

        Assert.IsFalse(cache.TryGetCache(key, out _));
    }

    [TestMethod]
    public void Remove_NonExistent_NoException()
    {
        var cache = new WeakCache<string, string>();
        cache.Remove("ghost");
    }

    [TestMethod]
    public void ForeachCache_IteratesAllEntries()
    {
        var cache = new WeakCache<string, string>();
        var k1 = "k1";
        var k2 = "k2";
        cache.AddOrUpdate(k1, "v1");
        cache.AddOrUpdate(k2, "v2");

        var visited = new Dictionary<string, string>();
        cache.ForeachCache((key, val) => visited[key] = val);

        Assert.AreEqual(2, visited.Count);
        Assert.AreEqual("v1", visited["k1"]);
        Assert.AreEqual("v2", visited["k2"]);
    }

    [TestMethod]
    public void AddOrUpdate_ManyItems_TriggersCleanup()
    {
        var cache = new WeakCache<object, string>();
        var keys = new object[20];
        for (int i = 0; i < 20; i++)
        {
            keys[i] = new object();
            cache.AddOrUpdate(keys[i], $"value{i}");
        }

        // Keep references alive; the last items should still be accessible
        Assert.IsTrue(cache.TryGetCache(keys[19], out var val));
        Assert.AreEqual("value19", val);
        GC.KeepAlive(keys);
    }

    [TestMethod]
    public void AddOrUpdate_SameTargetTwice_ForeachVisitsOnce()
    {
        var cache = new WeakCache<string, string>();
        cache.AddOrUpdate("k1", "old");
        cache.AddOrUpdate("k1", "new");
        var visits = 0;

        cache.ForeachCache((_, _) => visits++);

        Assert.AreEqual(1, visits);
    }

    [TestMethod]
    public void Remove_AfterUpdate_ForeachVisitsNothing()
    {
        var cache = new WeakCache<string, string>();
        cache.AddOrUpdate("k1", "old");
        cache.AddOrUpdate("k1", "new");
        cache.Remove("k1");
        var visits = 0;

        cache.ForeachCache((_, _) => visits++);

        Assert.AreEqual(0, visits);
    }

    [TestMethod]
    public void ForeachCache_CallbackWaitingOnAnotherThreadWrite_DoesNotDeadlock()
    {
        var cache = new WeakCache<string, string>();
        cache.AddOrUpdate("k1", "v1");
        var completed = false;

        cache.ForeachCache((_, _) =>
            completed = Task.Run(() => cache.AddOrUpdate("k2", "v2")).Wait(TimeSpan.FromSeconds(2)));

        Assert.IsTrue(completed);
    }

    [TestMethod]
    public void TryGetCache_WhileForeachCallbackRuns_IsNotBlocked()
    {
        var cache = new WeakCache<string, string>();
        cache.AddOrUpdate("k1", "v1");
        var completed = false;

        cache.ForeachCache((_, _) =>
            completed = Task.Run(() => cache.TryGetCache("k1", out _)).Wait(TimeSpan.FromSeconds(2)));

        Assert.IsTrue(completed);
    }
}
