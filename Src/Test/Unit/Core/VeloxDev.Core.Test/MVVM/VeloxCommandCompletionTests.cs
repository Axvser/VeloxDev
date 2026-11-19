using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// <see cref="VeloxCommand.ExecuteAndWaitAsync"/> has to answer for <em>every</em> way an execution can end —
/// including the two that never run. A call refused by a lock and a call dropped from the queue while it waited
/// never raise <see cref="CommandEventType.Exited"/>, which is exactly why hand-rolled
/// "subscribe Exited + Failed" waits used to hang on them forever.
/// </summary>
[TestClass]
public class VeloxCommandCompletionTests
{
    [TestMethod]
    public async Task AnExecutionThatRunsImmediately_ReportsCompleted()
    {
        var command = new VeloxCommand(() => Task.CompletedTask);

        var completion = await command.ExecuteAndWaitAsync(null);

        Assert.AreEqual(CommandOutcome.Completed, completion.Outcome);
        Assert.IsTrue(completion.Succeeded);
        Assert.IsNull(completion.Exception);
    }

    [TestMethod]
    public async Task AnExecutionThatWaitsForASlot_ReportsCompleted()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        _ = command.ExecuteAsync(null);                       // 占住唯一的槽位
        await gate.WaitForStartedAsync();

        var waiting = command.ExecuteAndWaitAsync(null);
        await CommandTestKit.WaitUntilAsync(() => command.IsBusy() && command.PendingCount() == 1);

        gate.Release(2);

        Assert.AreEqual(CommandOutcome.Completed, (await waiting.WaitAsync(CommandTestKit.Timeout)).Outcome);
    }

    [TestMethod]
    public async Task ABodyThatThrows_ReportsFailedWithTheException_EvenWithNoFailedSubscriber()
    {
        var boom = new InvalidOperationException("boom");
        var command = new VeloxCommand((_, _) => Task.FromException(boom));

        // 刻意不订阅 Failed：等结果不能依赖「碰巧有人订阅了事件」。
        var completion = await command.ExecuteAndWaitAsync(null);

        Assert.AreEqual(CommandOutcome.Failed, completion.Outcome);
        Assert.AreSame(boom, completion.Exception);
        Assert.IsFalse(completion.Succeeded);
    }

    [TestMethod]
    public async Task AnExecutionInterruptedWhileRunning_ReportsCanceled()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        var running = command.ExecuteAndWaitAsync(null);
        await gate.WaitForStartedAsync();

        await command.InterruptAsync();

        Assert.AreEqual(CommandOutcome.Canceled, (await running.WaitAsync(CommandTestKit.Timeout)).Outcome);
    }

    [TestMethod]
    public async Task ACallRefusedByALock_ReportsRefusedInsteadOfHanging()
    {
        var command = new VeloxCommand(() => Task.CompletedTask);
        await command.LockAsync();

        var completion = await command.ExecuteAndWaitAsync(null).WaitAsync(CommandTestKit.Timeout);

        Assert.AreEqual(CommandOutcome.Refused, completion.Outcome,
            "a refused call never raises Exited, so this is the only way to observe it");
    }

    [TestMethod]
    public async Task ACallDroppedFromTheQueue_ReportsCanceledInsteadOfHanging()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        _ = command.ExecuteAsync(null);                       // 占住槽位
        await gate.WaitForStartedAsync();

        var queued = command.ExecuteAndWaitAsync(null);
        await CommandTestKit.WaitUntilAsync(() => command.PendingCount() == 1);

        await command.ClearAsync();

        Assert.AreEqual(CommandOutcome.Canceled, (await queued.WaitAsync(CommandTestKit.Timeout)).Outcome,
            "a queued call dropped by Clear never raises Exited either");
    }

    [TestMethod]
    public async Task AbandoningTheWait_Throws_AndLeavesTheExecutionAlone()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        using var cts = new CancellationTokenSource();

        var running = command.ExecuteAndWaitAsync(null, cts.Token);
        await gate.WaitForStartedAsync();

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => running);

        // 放弃等待不等于取消执行：命令体还在跑，槽位还占着。
        Assert.AreEqual(1, command.ActiveCount(), "cancelling the wait must not touch the execution");
        gate.Release();
    }

    [TestMethod]
    public async Task AsynchronousCompletion_IsStillReportedExactlyOnce()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        var running = command.ExecuteAndWaitAsync(null);
        await gate.WaitForStartedAsync();
        gate.Release();

        Assert.AreEqual(CommandOutcome.Completed, (await running.WaitAsync(CommandTestKit.Timeout)).Outcome);
    }
}
