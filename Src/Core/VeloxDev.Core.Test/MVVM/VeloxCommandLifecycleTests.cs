using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// What a single execution reports, and in what order. This is the contract the workflow demos and the agent
/// toolkit build on — the agent discovers completion by pairing <see cref="CommandEventType.Exited"/> with
/// <see cref="CommandEventType.Failed"/>, so both the sequence and which stage carries the failure matter.
/// </summary>
[TestClass]
public class VeloxCommandLifecycleTests
{
    [TestMethod]
    public async Task AnExecutionThatRunsImmediately_ReportsTheFourCoreStagesInOrder()
    {
        var command = new VeloxCommand(() => Task.CompletedTask);
        var recorder = new CommandEventRecorder(command);

        await command.ExecuteAsync(null);
        await recorder.FirstExit;

        CollectionAssert.AreEqual(
            new[] { CommandEventType.Created, CommandEventType.Started, CommandEventType.Completed, CommandEventType.Exited },
            recorder.Types);
    }

    [TestMethod]
    public async Task AnExecutionThatWaitsForASlot_ReportsEnqueuedThenDequeuedAroundIt()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);                            // 只能排队
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Enqueued).Length == 1);

        gate.Release(2);
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount == 2);

        CollectionAssert.AreEqual(
            new[]
            {
                CommandEventType.Created, CommandEventType.Started,      // 第一个立刻跑
                CommandEventType.Created, CommandEventType.Enqueued,     // 第二个只能等
                CommandEventType.Completed, CommandEventType.Exited,     // 第一个跑完
                CommandEventType.Dequeued, CommandEventType.Started,     // 腾出的槽位交给第二个
                CommandEventType.Completed, CommandEventType.Exited,
            },
            recorder.Types);
    }

    [TestMethod]
    public async Task ABodyThatThrows_ReportsFailedInsteadOfCompleted()
    {
        var boom = new InvalidOperationException("boom");
        var command = new VeloxCommand((_, _) => Task.FromException(boom));
        var recorder = new CommandEventRecorder(command);

        await command.ExecuteAsync(null);
        await recorder.FirstExit;

        CollectionAssert.AreEqual(
            new[] { CommandEventType.Created, CommandEventType.Started, CommandEventType.Failed, CommandEventType.Exited },
            recorder.Types);
    }

    [TestMethod]
    public async Task ACallRefusedByALock_ReportsOnlyCreatedAndCanceled()
    {
        var command = new VeloxCommand(() => Task.CompletedTask);
        var recorder = new CommandEventRecorder(command);

        await command.LockAsync();
        await command.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[] { CommandEventType.Created, CommandEventType.Canceled },
            recorder.Types);
        Assert.AreEqual(0, recorder.ExitCount, "a refused call never got far enough to exit");
    }

    [TestMethod]
    public async Task OnlyFailedCarriesTheException_ExitedNeverDoes()
    {
        var boom = new InvalidOperationException("boom");
        var command = new VeloxCommand((_, _) => Task.FromException(boom));
        var recorder = new CommandEventRecorder(command);

        await command.ExecuteAsync(null);
        await recorder.FirstExit;

        Assert.AreSame(boom, recorder.Of(CommandEventType.Failed)[0].Exception, "Failed is where the failure lives");
        Assert.IsNull(recorder.Of(CommandEventType.Exited)[0].Exception,
            "Exited is not a success signal, and it must not inherit the failure either");
    }

    [TestMethod]
    public async Task ASuccessfulExecution_CarriesNoExceptionOnAnyStage()
    {
        var command = new VeloxCommand(() => Task.CompletedTask);
        var recorder = new CommandEventRecorder(command);

        await command.ExecuteAsync(null);
        await recorder.FirstExit;

        Assert.IsTrue(recorder.Events.All(e => e.Exception is null),
            "nothing failed, so no stage may report failure");
    }

    [TestMethod]
    public async Task EveryStageOfOneExecution_CarriesTheSameParameter()
    {
        var command = new VeloxCommand((_, _) => Task.CompletedTask);
        var recorder = new CommandEventRecorder(command);

        await command.ExecuteAsync("payload");
        await recorder.FirstExit;

        Assert.IsTrue(recorder.Events.All(e => (string?)e.Parameter == "payload"),
            "a handler correlates the stages of one execution through the parameter");
    }
}
