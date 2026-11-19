using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The tracker exists for exactly one reason: the generated property getters pass a method group, which creates
/// a FRESH delegate every time they are read. Dedupe therefore cannot be by delegate reference, and these tests
/// pin that down — along with the counter-intuitive consequence that <c>Unsubscribe</c> does not blacklist the
/// pair, so a later getter subscribes again.
/// </summary>
[TestClass]
public class ObservableCollectionTrackerTests
{
    [TestMethod]
    public void EnsureSubscribed_WithAFreshMethodGroupDelegate_SubscribesOnlyOnce()
    {
        var collection = new ObservableCollection<int>();
        var target = new TrackerProbe();

        for (var i = 0; i < 5; i++)
        {
            ObservableCollectionTracker.EnsureSubscribed(collection, target.OnChanged);
        }

        collection.Add(1);

        Assert.AreEqual(1, target.Calls,
            "a method group makes a new delegate each time, so reference equality would subscribe five times");
    }

    [TestMethod]
    public void EnsureSubscribed_ReportsEachChangeExactlyOnce()
    {
        var collection = new ObservableCollection<int>();
        var target = new TrackerProbe();

        ObservableCollectionTracker.EnsureSubscribed(collection, target.OnChanged);
        collection.Add(1);
        collection.Add(2);
        collection.Remove(1);

        Assert.AreEqual(3, target.Calls);
    }

    [TestMethod]
    public void Unsubscribe_DetachesTheHandler()
    {
        var collection = new ObservableCollection<int>();
        var target = new TrackerProbe();

        ObservableCollectionTracker.EnsureSubscribed(collection, target.OnChanged);
        ObservableCollectionTracker.Unsubscribe(collection, target.OnChanged);

        collection.Add(1);

        Assert.AreEqual(0, target.Calls);
    }

    [TestMethod]
    public void Unsubscribe_ThenEnsureSubscribed_SubscribesAgain()
    {
        var collection = new ObservableCollection<int>();
        var target = new TrackerProbe();

        ObservableCollectionTracker.EnsureSubscribed(collection, target.OnChanged);
        ObservableCollectionTracker.Unsubscribe(collection, target.OnChanged);
        ObservableCollectionTracker.EnsureSubscribed(collection, target.OnChanged);

        collection.Add(1);

        Assert.AreEqual(1, target.Calls,
            "Unsubscribe removes the subscription, not the record of it - a later getter re-subscribes");
    }

    [TestMethod]
    public void EnsureSubscribed_TreatsDistinctTargetsAsDistinctHandlers()
    {
        var collection = new ObservableCollection<int>();
        var first = new TrackerProbe();
        var second = new TrackerProbe();

        ObservableCollectionTracker.EnsureSubscribed(collection, first.OnChanged);
        ObservableCollectionTracker.EnsureSubscribed(collection, second.OnChanged);

        collection.Add(1);

        Assert.AreEqual(1, first.Calls, "same method, different targets are different handlers");
        Assert.AreEqual(1, second.Calls);
    }

    [TestMethod]
    public void EnsureSubscribed_OnSomethingThatCannotNotify_IsANoOp()
    {
        ObservableCollectionTracker.EnsureSubscribed(null, Noop);

        var notACollection = new object();
        ObservableCollectionTracker.EnsureSubscribed(notACollection, Noop);

        ObservableCollectionTracker.Unsubscribe(null, Noop);
        ObservableCollectionTracker.Unsubscribe(notACollection, Noop);
    }

    [TestMethod]
    public void Unsubscribe_OnACollectionNeverSubscribed_IsANoOp()
    {
        ObservableCollectionTracker.Unsubscribe(new ObservableCollection<int>(), Noop);
    }

    [TestMethod]
    public async Task ConcurrentEnsureSubscribed_SubscribesOnce()
    {
        var collection = new ObservableCollection<int>();
        var target = new TrackerProbe();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                ObservableCollectionTracker.EnsureSubscribed(collection, target.OnChanged);
            }
        })));

        collection.Add(1);

        Assert.AreEqual(1, target.Calls,
            "concurrent getters must not each end up adding their own subscription");
    }

    private static void Noop(object? sender, NotifyCollectionChangedEventArgs e)
    {
    }

    private sealed class TrackerProbe
    {
        private int _calls;

        internal int Calls => Volatile.Read(ref _calls);

        internal void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => Interlocked.Increment(ref _calls);
    }
}
