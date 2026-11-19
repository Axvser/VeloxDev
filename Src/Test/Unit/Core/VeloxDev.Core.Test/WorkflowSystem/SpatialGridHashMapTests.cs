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

    // ═══════════════════════════════════════════════════════════════════════
    // Bounds
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Bounds_Empty_ReturnsEmpty()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        Assert.IsTrue(map.Bounds.IsEmpty);
    }

    [TestMethod]
    public void Bounds_SingleItem_MatchesItemBounds()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        var item = new FakeSpatialItem { Bounds = new Viewport(10, 20, 100, 200) };
        map.Insert(item);
        Assert.AreEqual(item.Bounds, map.Bounds);
    }

    [TestMethod]
    public void Bounds_MultipleItems_ReturnsUnion()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        map.Insert(new FakeSpatialItem { Bounds = new Viewport(0, 0, 50, 50) });
        map.Insert(new FakeSpatialItem { Bounds = new Viewport(100, 200, 80, 60) });
        var expected = new Viewport(0, 0, 180, 260);
        Assert.AreEqual(expected, map.Bounds);
    }

    [TestMethod]
    public void Bounds_AfterRemove_Shrinks()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        var far = new FakeSpatialItem { Bounds = new Viewport(500, 500, 50, 50) };
        var near = new FakeSpatialItem { Bounds = new Viewport(10, 10, 50, 50) };
        map.Insert(far);
        map.Insert(near);
        map.Remove(far);
        Assert.AreEqual(near.Bounds, map.Bounds);
    }

    [TestMethod]
    public void Bounds_AfterItemMoves_Updates()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        var item = new FakeSpatialItem { Bounds = new Viewport(10, 10, 50, 50) };
        map.Insert(item);
        item.Bounds = new Viewport(200, 200, 100, 100);
        Assert.AreEqual(item.Bounds, map.Bounds);
    }

    [TestMethod]
    public void Bounds_AfterClear_ReturnsEmpty()
    {
        var map = new SpatialGridHashMap<FakeSpatialItem>(100);
        map.Insert(new FakeSpatialItem { Bounds = new Viewport(10, 10, 50, 50) });
        map.Clear();
        Assert.IsTrue(map.Bounds.IsEmpty);
    }

    }
