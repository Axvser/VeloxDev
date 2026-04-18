using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class SpatialGridHashMapTests
{
    private sealed class FakeSpatialItem : INotifyPropertyChanged, ISpatialBoundsProvider
    {
        private Viewport _bounds;

        public Viewport Bounds
        {
            get => _bounds;
            set
            {
                _bounds = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bounds)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [TestMethod]
    public void Insert_And_Query_ReturnsItem()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        var item = new FakeSpatialItem { Bounds = new Viewport(10, 10, 50, 50) };

        map.Insert(item);

        var results = map.Query(new Viewport(0, 0, 100, 100)).ToList();
        CollectionAssert.Contains(results, item);
    }

    [TestMethod]
    public void Query_NoIntersection_ReturnsEmpty()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        var item = new FakeSpatialItem { Bounds = new Viewport(200, 200, 50, 50) };
        map.Insert(item);

        var results = map.Query(new Viewport(0, 0, 50, 50)).ToList();
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Remove_ItemNoLongerQueried()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        var item = new FakeSpatialItem { Bounds = new Viewport(10, 10, 50, 50) };
        map.Insert(item);
        map.Remove(item);

        var results = map.Query(new Viewport(0, 0, 200, 200)).ToList();
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Insert_Duplicate_IgnoredSilently()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        var item = new FakeSpatialItem { Bounds = new Viewport(10, 10, 50, 50) };
        map.Insert(item);
        map.Insert(item);

        var results = map.Query(new Viewport(0, 0, 200, 200)).ToList();
        Assert.AreEqual(1, results.Count);
    }

    [TestMethod]
    public void BoundsChange_UpdatesIndex()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        var item = new FakeSpatialItem { Bounds = new Viewport(10, 10, 50, 50) };
        map.Insert(item);

        // Move item far away
        item.Bounds = new Viewport(500, 500, 50, 50);

        // Old region should be empty
        var oldResults = map.Query(new Viewport(0, 0, 100, 100)).ToList();
        Assert.AreEqual(0, oldResults.Count);

        // New region should contain the item
        var newResults = map.Query(new Viewport(450, 450, 200, 200)).ToList();
        CollectionAssert.Contains(newResults, item);
    }

    [TestMethod]
    public void Clear_RemovesAllItems()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        map.Insert(new FakeSpatialItem { Bounds = new Viewport(0, 0, 50, 50) });
        map.Insert(new FakeSpatialItem { Bounds = new Viewport(100, 100, 50, 50) });

        map.Clear();

        var results = map.Query(new Viewport(-1000, -1000, 5000, 5000)).ToList();
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Query_EmptyViewport_ReturnsEmpty()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        map.Insert(new FakeSpatialItem { Bounds = new Viewport(0, 0, 50, 50) });

        var results = map.Query(new Viewport(0, 0, 0, 0)).ToList();
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Remove_Null_NoException()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        map.Remove(null!);
    }

    [TestMethod]
    public void Insert_Null_NoException()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        map.Insert(null!);
    }
}
