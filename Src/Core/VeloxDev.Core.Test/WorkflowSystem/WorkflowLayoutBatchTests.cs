using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// A layout pass reaches every node on the canvas, and several platform events can report the same change. The batch
/// has to turn that burst into one run, skip requests whose inputs provably did not change, and never drop work on a
/// platform hop that refuses the callback.
/// </summary>
[TestClass]
public class WorkflowLayoutBatchTests
{
    [TestMethod]
    public void RequestsForDifferentKeys_EachRunOnce()
    {
        var batch = WorkflowLayoutBatch.For(new object());
        var runs = 0;

        batch.Request(new object(), () => runs++);
        batch.Request(new object(), () => runs++);

        // 两个不同的 key，各自跑一次。
        Assert.IsTrue(batch.Flush());
        Assert.AreEqual(2, runs);
    }

    [TestMethod]
    public void SameKeyRequestedTwice_RunsTheLatestWorkOnce()
    {
        var batch = WorkflowLayoutBatch.For(new object());
        var key = new object();
        var runs = 0;

        batch.Request(key, () => runs += 1);
        batch.Request(key, () => runs += 10);

        Assert.AreEqual(1, batch.PendingCount);
        Assert.IsTrue(batch.Flush());
        Assert.AreEqual(10, runs, "the request queued last describes the state that will be synced");
    }

    [TestMethod]
    public void UnchangedStamp_IsNotQueuedAgain()
    {
        var batch = WorkflowLayoutBatch.For(new object());
        var key = new object();
        var node = Node(anchorX: 10d, size: 100d);
        var stamp = WorkflowLayoutStamp.Capture(node, measuredWidth: 100d, measuredHeight: 50d, slotCount: 2);
        var runs = 0;

        Assert.IsTrue(batch.Request(key, stamp, () => runs++));
        Assert.IsTrue(batch.Flush());
        Assert.AreEqual(1, runs);

        Assert.IsFalse(batch.Request(key, stamp, () => runs++), "the same inputs mean nothing has to be measured again");
        Assert.IsFalse(batch.Flush());
        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void ChangedStamp_IsQueuedAgain()
    {
        var batch = WorkflowLayoutBatch.For(new object());
        var key = new object();
        var node = Node(anchorX: 10d, size: 100d);
        var runs = 0;

        batch.Request(key, WorkflowLayoutStamp.Capture(node, 100d, 50d, 2), () => runs++);
        batch.Flush();

        node.Anchor = new Anchor(20d, 0d, 0);
        Assert.IsTrue(batch.Request(key, WorkflowLayoutStamp.Capture(node, 100d, 50d, 2), () => runs++));
        Assert.IsTrue(batch.Flush());
        Assert.AreEqual(2, runs);
    }

    [TestMethod]
    public void ForcedRequest_ForgetsTheStamp()
    {
        var batch = WorkflowLayoutBatch.For(new object());
        var key = new object();
        var node = Node(anchorX: 10d, size: 100d);
        var stamp = WorkflowLayoutStamp.Capture(node, 100d, 50d, 2);
        var runs = 0;

        batch.Request(key, stamp, () => runs++);
        batch.Flush();

        // 强制请求没有指纹：跑完之后旧指纹不能再让下一次请求被跳过。
        batch.Request(key, () => runs++);
        batch.Flush();
        Assert.IsTrue(batch.Request(key, stamp, () => runs++));
        batch.Flush();
        Assert.AreEqual(3, runs);
    }

    [TestMethod]
    public void Flush_ClearsBeforeRunning_SoWorkThatRequestsAgainGetsItsOwnTurn()
    {
        var batch = WorkflowLayoutBatch.For(new object());
        var key = new object();
        var runs = 0;

        batch.Request(key, () =>
        {
            runs++;
            if (runs == 1) batch.Request(key, () => runs++);
        });

        Assert.IsTrue(batch.Flush());
        Assert.AreEqual(1, runs);
        Assert.AreEqual(1, batch.PendingCount, "a request made during the run is about state the run could not see");

        Assert.IsTrue(batch.Flush());
        Assert.AreEqual(2, runs);
    }

    [TestMethod]
    public void OneFailingRequest_DoesNotStarveTheRest_AndTheFirstFailureSurfaces()
    {
        var batch = WorkflowLayoutBatch.For(new object());
        var secondRan = false;

        batch.Request(new object(), () => throw new InvalidOperationException("boom"));
        batch.Request(new object(), () => secondRan = true);

        Assert.ThrowsExactly<InvalidOperationException>(() => batch.Flush());
        Assert.IsTrue(secondRan, "one node's failure must not cancel the other node's sync");
    }

    [TestMethod]
    public void Cancel_DropsTheQueuedRequest()
    {
        var batch = WorkflowLayoutBatch.For(new object());
        var key = new object();
        var runs = 0;

        batch.Request(key, () => runs++);
        batch.Cancel(key);

        Assert.IsFalse(batch.Flush());
        Assert.AreEqual(0, runs);
    }

    [TestMethod]
    public void Schedule_PostsOneHopForABurst()
    {
        var posted = new List<Action>();
        var batch = WorkflowLayoutBatch.For(new object(), action =>
        {
            posted.Add(action);
            return true;
        });
        var runs = 0;

        batch.Request(new object(), () => runs++);
        batch.Request(new object(), () => runs++);
        batch.Request(new object(), () => runs++);
        batch.Schedule();
        batch.Schedule();

        Assert.HasCount(1, posted, "a burst asks the UI thread for one flush, not one per request");
        posted[0]();
        Assert.AreEqual(3, runs);
    }

    [TestMethod]
    public void Schedule_RetriesWhenThePlatformRefusesTheHop()
    {
        var accepted = false;
        var posted = 0;
        var batch = WorkflowLayoutBatch.For(new object(), _ =>
        {
            posted++;
            return accepted;
        });

        batch.Request(new object(), () => { });
        batch.Schedule();
        Assert.AreEqual(1, posted);

        // 平台拒绝之后旗标必须放回去，否则队列会永远停在「已排程」状态。
        accepted = true;
        batch.Schedule();
        Assert.AreEqual(2, posted);
    }

    [TestMethod]
    public void For_ReturnsTheSameBatchForTheSameOwner()
    {
        var owner = new object();

        Assert.AreSame(WorkflowLayoutBatch.For(owner), WorkflowLayoutBatch.For(owner));
        Assert.AreNotSame(WorkflowLayoutBatch.For(owner), WorkflowLayoutBatch.For(new object()));
    }

    private static NodeDefaultViewModel Node(double anchorX, double size)
        => new()
        {
            Anchor = new Anchor(anchorX, 0d, 0),
            Size = new Size(size, size),
        };
}
