using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Contracts of the shared dispatcher batch pump: one post per burst, a yield after the continuous budget, and a
/// refused yield post that must not leave the pool's scheduled flag set with work still pending.
/// </summary>
[TestClass]
public sealed class ViewBatchPumpTests
{
    [TestMethod]
    public void RefusedInitialPost_DoesNotRunTheBatch_AndTheNextScheduleCanPostAgain()
    {
        var posts = new List<Action>();
        var accept = false;
        var pump = new ViewBatchPump<int>(
            (_, action) => { posts.Add(action); return accept; },
            yieldPriority: 0,
            budgetMilliseconds: 0d,
            elapsedMilliseconds: () => 0d);
        var ran = 0;

        Assert.IsFalse(pump.Schedule(() => ran++, 1), "a refused post reports failure instead of pretending to queue");
        Assert.AreEqual(0, ran, "the batch never runs without a dispatcher callback");
        Assert.IsFalse(pump.IsPumping);
        Assert.IsFalse(pump.HasPending);

        accept = true;
        Assert.IsTrue(pump.Schedule(() => ran++, 1));
        posts[^1]();
        Assert.AreEqual(1, ran, "a later accepted post runs normally");
    }

    [TestMethod]
    public void RefusedYieldPost_RunsThePendingBatchInline_AndResetsThePump()
    {
        var posts = new List<Action>();
        var calls = 0;
        var pump = new ViewBatchPump<int>(
            (_, action) => { calls++; posts.Add(action); return calls != 2; },
            yieldPriority: 0,
            budgetMilliseconds: 0d,
            elapsedMilliseconds: () => 100d);
        var ran = new List<int>();

        Assert.IsTrue(pump.Schedule(() =>
        {
            ran.Add(1);
            Assert.IsTrue(pump.Schedule(() => ran.Add(2), 1), "a running pump accepts the next batch without another post");
        }, 1));

        posts[0]();

        CollectionAssert.AreEqual(new[] { 1, 2 }, ran.ToArray(),
            "the rejected yield post must run the pending batch inline rather than dropping it");
        Assert.IsFalse(pump.IsPumping);
        Assert.IsFalse(pump.HasPending);

        Assert.IsTrue(pump.Schedule(() => ran.Add(3), 1), "the pump is not wedged after the inline fallback");
        posts[^1]();
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ran.ToArray());
    }

    [TestMethod]
    public void Schedule_WhilePumping_RunsTheNextBatchInTheSameCallback()
    {
        var posts = new List<Action>();
        var calls = 0;
        var pump = new ViewBatchPump<int>(
            (_, action) => { calls++; posts.Add(action); return true; },
            yieldPriority: 0,
            budgetMilliseconds: 1_000_000d,
            elapsedMilliseconds: () => 0d);
        var ran = new List<int>();

        Assert.IsTrue(pump.Schedule(() =>
        {
            ran.Add(1);
            Assert.IsTrue(pump.Schedule(() => ran.Add(2), 1));
        }, 1));

        posts[0]();

        CollectionAssert.AreEqual(new[] { 1, 2 }, ran.ToArray(),
            "the batch scheduled while the pump runs is executed in the same callback");
        Assert.AreEqual(1, calls, "a running pump must not take another dispatcher round trip");
    }
}
