using VeloxDev.WeakTypes;

namespace VeloxDev.Core.Test.WeakTypes;

[TestClass]
public class WeakStackTests
{
    [TestMethod]
    public void Push_And_TryPop_ReturnsItem()
    {
        var stack = new WeakStack<string>();
        stack.Push("hello");

        Assert.IsTrue(stack.TryPop(out var item));
        Assert.AreEqual("hello", item);
    }

    [TestMethod]
    public void TryPop_Empty_ReturnsFalse()
    {
        var stack = new WeakStack<string>();
        Assert.IsFalse(stack.TryPop(out _));
    }

    [TestMethod]
    public void Push_MultipleItems_LIFO_Order()
    {
        var stack = new WeakStack<string>();
        stack.Push("a");
        stack.Push("b");
        stack.Push("c");

        Assert.IsTrue(stack.TryPop(out var item));
        Assert.AreEqual("c", item);
        Assert.IsTrue(stack.TryPop(out item));
        Assert.AreEqual("b", item);
        Assert.IsTrue(stack.TryPop(out item));
        Assert.AreEqual("a", item);
    }

    [TestMethod]
    public void TryPeek_ReturnsTopWithoutRemoving()
    {
        var stack = new WeakStack<string>();
        stack.Push("x");

        Assert.IsTrue(stack.TryPeek(out var peeked));
        Assert.AreEqual("x", peeked);

        Assert.IsTrue(stack.TryPop(out var popped));
        Assert.AreEqual("x", popped);
    }

    [TestMethod]
    public void Clear_EmptiesStack()
    {
        var stack = new WeakStack<string>();
        stack.Push("a");
        stack.Push("b");
        stack.Clear();

        Assert.IsTrue(stack.IsEmpty);
    }

    [TestMethod]
    public void Push_Null_Throws()
    {
        var stack = new WeakStack<string>();
        Assert.Throws<ArgumentNullException>(() => stack.Push(null!));
    }

    [TestMethod]
    public void PushRange_AddsMultiple()
    {
        var stack = new WeakStack<string>();
        var count = stack.PushRange(["a", "b", "c"]);
        Assert.AreEqual(3, count);
        Assert.AreEqual(3, stack.Count);
    }

    [TestMethod]
    public void PushRange_Null_Throws()
    {
        var stack = new WeakStack<string>();
        Assert.Throws<ArgumentNullException>(() => stack.PushRange(null!));
    }

    [TestMethod]
    public void Enumerable_ReturnsAllLiveItems()
    {
        var stack = new WeakStack<string>();
        stack.Push("a");
        stack.Push("b");

        var items = stack.ToList();
        Assert.AreEqual(2, items.Count);
        CollectionAssert.Contains(items, "a");
        CollectionAssert.Contains(items, "b");
    }

    [TestMethod]
    public void TrimExcess_DoesNotThrow()
    {
        var stack = new WeakStack<string>();
        stack.Push("x");
        stack.TrimExcess();
    }
}
