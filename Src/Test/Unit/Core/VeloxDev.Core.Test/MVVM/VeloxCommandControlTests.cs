using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The queue controls - <c>Lock</c>, <c>Unlock</c>, <c>Interrupt</c>, <c>Clear</c>, <c>Continue</c>.
/// <para>
/// The load-bearing rule these pin down: the lock is a single flag that belongs to the caller, so a control
/// that temporarily locks the command to do its work must put the flag back the way it found it. Otherwise
/// <c>Interrupt</c> on a command the caller had deliberately held open would silently release the queue.
/// </para>
/// </summary>
[TestClass]
public class VeloxCommandControlTests
{
    [TestMethod]
    public async Task Locked_NewExecutionsAreCancelledWithoutEverStarting()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        await command.LockAsync();
        Assert.IsFalse(command.CanExecute(null), "a locked command reports itself as not executable");

        await command.ExecuteAsync(null);

        Assert.HasCount(1, recorder.Of(CommandEventType.Canceled), "a call arriving at a locked command is refused");
        Assert.HasCount(0, recorder.Of(CommandEventType.Started), "refused means never started");
        Assert.AreEqual(0, gate.StartedCount);
    }

    [TestMethod]
    public async Task Lock_DoesNotInterruptABodyAlreadyInFlight()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        await command.LockAsync();

        Assert.HasCount(0, recorder.Of(CommandEventType.Canceled), "locking stops new calls, not the one in flight");
        gate.Release();
        await recorder.FirstExit;
    }

    [TestMethod]
    public async Task Unlock_LetsANewExecutionThrough()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        await command.LockAsync();
        await command.UnlockAsync();
        Assert.IsTrue(command.CanExecute(null), "unlocking restores executability");

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();
        gate.Release();
    }

    [TestMethod]
    public async Task Interrupt_OnAnUnlockedCommand_LeavesItUnlocked()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        await command.InterruptAsync();

        Assert.IsTrue(command.CanExecute(null), "Interrupt only borrows the lock, so an unlocked command stays unlocked");
    }

    [TestMethod]
    public async Task Interrupt_OnALockedCommand_LeavesItLocked()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        await command.LockAsync();
        await command.InterruptAsync();

        Assert.IsFalse(command.CanExecute(null), "Interrupt must not disturb a lock that was already held");
    }

    [TestMethod]
    public async Task Interrupt_OnALockedCommand_DoesNotReleaseTheQueue()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);                       // 占住槽位
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);                       // 排队
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Enqueued).Length == 1);

        await command.LockAsync();
        await command.InterruptAsync();

        Assert.AreEqual(1, gate.StartedCount, "the queued call must stay parked while the lock is still held");
    }

    [TestMethod]
    public async Task Interrupt_CancelsTheRunningBody_ButNotTheQueuedOnes()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Enqueued).Length == 1);

        await command.InterruptAsync();

        // Interrupt 与 Clear 的分界就在这里：排队项没有被取消，只是被放行去跑。
        await gate.WaitForStartedAsync(2);
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Canceled).Length >= 1);
        Assert.HasCount(1, recorder.Of(CommandEventType.Canceled),
            "a cancelled execution reports it once, however many places wanted to report it");
        Assert.HasCount(0, recorder.Of(CommandEventType.Failed), "cancelling the running body must not fail the queued one");

        gate.Release(2);
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount == 2);
    }

    [TestMethod]
    public async Task Clear_CancelsRunningAndQueued_DequeuingTheQueuedOnesFirst()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);
        _ = command.ExecuteAsync(null);
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Enqueued).Length == 2);

        await command.ClearAsync();

        Assert.HasCount(2, recorder.Of(CommandEventType.Dequeued), "each cleared queued call is dequeued before it is cancelled");

        // 等到正在跑的那个真正收尾：它的第二次 Canceled 也在那之前发完，这样计数就没有竞态。
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount >= 1);
        Assert.HasCount(3, recorder.Of(CommandEventType.Canceled),
            "one cancel per queued call, plus one for the running one - Clear and its body both want to report it, the first wins");
    }

    [TestMethod]
    public async Task Clear_OnALockedCommand_LeavesItLocked()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        await command.LockAsync();
        await command.ClearAsync();

        Assert.IsFalse(command.CanExecute(null), "Clear must not disturb a lock that was already held");
    }

    [TestMethod]
    public async Task Continue_WhileLocked_StartsNothing()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Enqueued).Length == 1);

        await command.LockAsync();
        await command.ContinueAsync();

        Assert.AreEqual(1, gate.StartedCount, "Continue is a kick of the queue, and a locked queue does not move");
    }

    [TestMethod]
    public async Task Continue_AfterClear_StartsNothing()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Enqueued).Length == 1);

        await command.ClearAsync();
        await command.ContinueAsync();

        Assert.AreEqual(1, gate.StartedCount, "Clear already emptied the queue, so Continue has nothing to kick");
    }
}
