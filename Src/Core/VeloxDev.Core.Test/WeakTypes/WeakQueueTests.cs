using VeloxDev.WeakTypes;

namespace VeloxDev.Core.Test.WeakTypes;

[TestClass]
public class WeakQueueTests
{
    [TestMethod]
    public void Enqueue_And_TryDequeue_ReturnsItem()
    {
        var queue = new WeakQueue<string>();
        queue.Enqueue("hello");

        Assert.IsTrue(queue.TryDequeue(out var item));
        Assert.AreEqual("hello", item);
    }

    [TestMethod]
    public void TryDequeue_Empty_ReturnsFalse()
    {
        var queue = new WeakQueue<string>();
        Assert.IsFalse(queue.TryDequeue(out _));
    }

    [TestMethod]
    public void Enqueue_MultipleItems_FIFO_Order()
    {
        var queue = new WeakQueue<string>();
        queue.Enqueue("a");
        queue.Enqueue("b");
        queue.Enqueue("c");

        Assert.IsTrue(queue.TryDequeue(out var item));
        Assert.AreEqual("a", item);
        Assert.IsTrue(queue.TryDequeue(out item));
        Assert.AreEqual("b", item);
        Assert.IsTrue(queue.TryDequeue(out item));
        Assert.AreEqual("c", item);
    }

    [TestMethod]
    public void TryPeek_ReturnsFrontWithoutRemoving()
    {
        var queue = new WeakQueue<string>();
        queue.Enqueue("x");

        Assert.IsTrue(queue.TryPeek(out var peeked));
        Assert.AreEqual("x", peeked);

        Assert.IsTrue(queue.TryDequeue(out var dequeued));
        Assert.AreEqual("x", dequeued);
    }

    [TestMethod]
    public void Clear_EmptiesQueue()
    {
        var queue = new WeakQueue<string>();
        queue.Enqueue("a");
        queue.Enqueue("b");
        queue.Clear();

        Assert.IsTrue(queue.IsEmpty);
    }

    [TestMethod]
    public void Enqueue_Null_Throws()
    {
        var queue = new WeakQueue<string>();
        Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!));
    }

    [TestMethod]
    public void EnqueueRange_AddsMultiple()
    {
        var queue = new WeakQueue<string>();
        var count = queue.EnqueueRange(["a", "b", "c"]);
        Assert.AreEqual(3, count);
        Assert.AreEqual(3, queue.Count);
    }

    [TestMethod]
    public void EnqueueRange_Null_Throws()
    {
        var queue = new WeakQueue<string>();
        Assert.Throws<ArgumentNullException>(() => queue.EnqueueRange(null!));
    }

    [TestMethod]
    public void Enumerable_ReturnsAllLiveItems()
    {
        var queue = new WeakQueue<string>();
        queue.Enqueue("a");
        queue.Enqueue("b");

        var items = queue.ToList();
        Assert.AreEqual(2, items.Count);
        Assert.AreEqual("a", items[0]);
        Assert.AreEqual("b", items[1]);
    }

    [TestMethod]
    public void TrimExcess_DoesNotThrow()
    {
        var queue = new WeakQueue<string>();
        queue.Enqueue("x");
        queue.TrimExcess();
    }
}
