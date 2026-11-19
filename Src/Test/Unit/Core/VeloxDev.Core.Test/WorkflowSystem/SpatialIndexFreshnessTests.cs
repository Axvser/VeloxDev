using System.ComponentModel;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// A query must never answer out of a stale index. An item whose bounds were empty when it was inserted — the view
/// layer has not measured it yet — is registered but <b>not</b> put in a cell, and it only ever enters one when its
/// <c>Bounds</c> change is announced. That announcement can fail to arrive: the view may never lay the item out, or
/// the change may land inside somebody else's pass and be deferred behind a rebuild that then never runs. Such an
/// item is not merely late — it is unreachable, because it sits in no cell at all, and no amount of panning or
/// zooming can bring it back. The grid therefore brings itself up to date before answering.
/// </summary>
[TestClass]
public class SpatialIndexFreshnessTests
{
    /// <summary>An item that can become measurable <b>without</b> announcing it — what a view layer that never
    /// reported the measurement looks like from the index's side.</summary>
    private sealed class TestItem : ISpatialBoundsProvider
    {
        public Viewport Bounds { get; private set; } = Viewport.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Becomes measurable and says so — the ordinary path, kept here so the event is genuinely used.</summary>
        public void MeasureAndAnnounce(double left, double top, double size)
        {
            Bounds = new Viewport(left, top, size, size);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bounds)));
        }

        /// <summary>Becomes measurable and says nothing — the path that used to lose the item for good.</summary>
        public void MeasureQuietly(double left, double top, double size)
            => Bounds = new Viewport(left, top, size, size);
    }

    private static readonly Viewport Everything = new(0, 0, 1000, 1000);

    [TestMethod]
    public void AnItemMeasuredSilently_IsFoundByTheNextQuery()
    {
        var grid = new SpatialGridHashMap<TestItem>(cellSize: 100);
        var item = new TestItem();
        grid.Insert(item);
        Assert.IsEmpty(grid.Query(Everything).ToList(),
            "precondition: an item with no bounds is registered for tracking but not yet in a cell");

        item.MeasureQuietly(150, 150, 40);

        Assert.HasCount(1, grid.Query(Everything).ToList(),
            "the query has to bring the index up to date before answering — otherwise the item stays unreachable and no viewport change can help");
    }

    [TestMethod]
    public void AnItemMeasuredAndAnnounced_IsFoundAsBefore()
    {
        var grid = new SpatialGridHashMap<TestItem>(cellSize: 100);
        var item = new TestItem();
        grid.Insert(item);

        item.MeasureAndAnnounce(150, 150, 40);

        Assert.HasCount(1, grid.Query(Everything).ToList(), "the announced path is unchanged");
    }

    [TestMethod]
    public void AnItemOutsideTheViewport_IsNotReturned()
    {
        var grid = new SpatialGridHashMap<TestItem>(cellSize: 100);
        var item = new TestItem();
        grid.Insert(item);
        item.MeasureQuietly(5000, 5000, 40);

        Assert.IsEmpty(grid.Query(Everything).ToList(),
            "bringing the index up to date must not turn into answering with everything");
    }
}
