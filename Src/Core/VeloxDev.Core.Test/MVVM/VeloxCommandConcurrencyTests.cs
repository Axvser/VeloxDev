using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The semaphore is the only thing <see cref="VeloxCommand"/> adds on top of <c>ICommand</c> that changes how
/// many bodies run at once. Excess calls are queued and never dropped, and a queued call is released only by a
/// slot freeing or by the cap being raised.
/// <para>
/// The generated commands all pass <c>semaphore: 1</c>, so the serialising case is the one the rest of the
/// repo actually runs on; the parallel cases exist to keep that from silently becoming the only behaviour.
/// </para>
/// </summary>
[TestClass]
public class VeloxCommandConcurrencyTests
{
    [TestMethod]
    public async Task ASemaphoreOfOne_SerializesTheBodies()
    {
        var gate = new CommandGate();
        var probe = new ConcurrencyProbe();
        var command = new VeloxCommand(async (_, ct) =>
        {
            probe.Enter();
            try
            {
                await gate.RunAsync(null, ct);
            }
            finally
            {
                probe.Exit();
            }
        }, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        for (var i = 0; i < 3; i++)
        {
            _ = command.ExecuteAsync(null);
        }

        await gate.WaitForStartedAsync();
        Assert.AreEqual(1, gate.StartedCount, "only the first call may occupy the single slot");
        Assert.AreEqual(1, probe.Max, "the default cap must never let two bodies overlap");

        gate.Release(3);
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount == 3);
        Assert.AreEqual(1, probe.Max, "every call ran, and none of them overlapped");
    }

    [TestMethod]
    public async Task ASemaphoreOfThree_RunsThreeAtOnce_AndQueuesOnlyTheFourth()
    {
        var gate = new CommandGate();
        var probe = new ConcurrencyProbe();
        var command = new VeloxCommand(async (_, ct) =>
        {
            probe.Enter();
            try
            {
                await gate.RunAsync(null, ct);
            }
            finally
            {
                probe.Exit();
            }
        }, semaphore: 3);
        var recorder = new CommandEventRecorder(command);

        for (var i = 0; i < 4; i++)
        {
            _ = command.ExecuteAsync(null);
        }

        await gate.WaitForStartedAsync(3);
        Assert.AreEqual(3, gate.StartedCount, "the cap is three, so the fourth call cannot have entered");
        Assert.AreEqual(3, probe.Max, "a raised cap really does run them in parallel");
        Assert.HasCount(1, recorder.Of(CommandEventType.Enqueued), "the call over the cap waits, it is not dropped");

        gate.Release(4);
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount == 4);
    }

    [TestMethod]
    public async Task ASlotFreedByOneBody_StartsExactlyTheNextQueuedCall()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Enqueued).Length == 1);
        Assert.AreEqual(1, gate.StartedCount, "with the single slot taken, the second call waits");

        gate.Release();                                       // 腾出槽位
        await gate.WaitForStartedAsync(2);                    // 它交给排队的那一个，而不是凭空出现的第三个

        gate.Release(2);
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount == 2);
    }

    [TestMethod]
    public async Task RaisingTheCap_DrainsTheQueue()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);
        _ = command.ExecuteAsync(null);
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Enqueued).Length == 2);

        await command.ChangeSemaphoreAsync(3);

        await gate.WaitForStartedAsync(3);
        Assert.AreEqual(3, gate.StartedCount, "raising the cap is what releases the queue - nothing else will");

        gate.Release(3);
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount == 3);
    }

    [TestMethod]
    public void ConstructingWithASemaphoreBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VeloxCommand(() => Task.CompletedTask, semaphore: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VeloxCommand(() => Task.CompletedTask, semaphore: -1));
    }

    [TestMethod]
    public void ChangeSemaphore_BelowOne_ThrowsOnTheSynchronousEntryPoint()
    {
        var command = new VeloxCommand(() => Task.CompletedTask);

        // 同步版是 `_ = ChangeSemaphoreAsync(...)`，异常若只在 async 方法里抛就会变成未观察异常、静默丢失。
        Assert.Throws<ArgumentOutOfRangeException>(() => command.ChangeSemaphore(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => command.ChangeSemaphore(-5));
    }

    [TestMethod]
    public async Task ChangeSemaphoreAsync_BelowOne_ThrowsOnTheAsynchronousEntryPoint()
    {
        var command = new VeloxCommand(() => Task.CompletedTask);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => command.ChangeSemaphoreAsync(0));
    }
}
