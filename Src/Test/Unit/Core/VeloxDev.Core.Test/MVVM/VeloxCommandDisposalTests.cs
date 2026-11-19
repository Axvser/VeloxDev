using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Every cancellable execution owns a <see cref="CancellationTokenSource"/>. Two rules have to hold at once:
/// the source must OUTLIVE the body that observes it (releasing it early makes a cancelled body fault, which
/// would surface as <see cref="CommandEventType.Failed"/> instead of <see cref="CommandEventType.Canceled"/>),
/// and it must not outlive the execution itself.
/// <para>
/// The second rule has a case that is easy to miss: a call that is cleared while still queued never reaches
/// the execution path at all, so only <c>Clear</c> itself can release it.
/// </para>
/// </summary>
[TestClass]
public class VeloxCommandDisposalTests
{
    [TestMethod]
    public async Task ACompletedExecution_ReleasesItsCancellationTokenSource()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        var cts = recorder.Of(CommandEventType.Started)[0].Cts;
        Assert.IsNotNull(cts, "a cancellable execution hands out a source as soon as it starts");

        gate.Release();
        await recorder.FirstExit;

        await CommandTestKit.WaitUntilAsync(() => CommandTestKit.IsDisposed(cts));
    }

    [TestMethod]
    public async Task AClearedQueuedCall_ReleasesItsCancellationTokenSource()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);                        // 占住唯一的槽位
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);                        // 只能排队
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Enqueued).Length == 1);

        var queuedCts = recorder.Of(CommandEventType.Enqueued)[0].Cts;
        Assert.IsNotNull(queuedCts);

        await command.ClearAsync();

        await CommandTestKit.WaitUntilAsync(() => CommandTestKit.IsDisposed(queuedCts));
        gate.Release();
    }

    [TestMethod]
    public async Task AnInterruptedBody_SurfacesAsCancelledAndNeverAsFailed()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();
        var cts = recorder.Of(CommandEventType.Started)[0].Cts;
        Assert.IsNotNull(cts);

        await command.InterruptAsync();
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount >= 1);

        Assert.HasCount(0, recorder.Of(CommandEventType.Failed),
            "the source must stay usable while the body runs, or the body faults and gets reported as failed");
        await CommandTestKit.WaitUntilAsync(() => CommandTestKit.IsDisposed(cts));
    }

    [TestMethod]
    public async Task AnExecutionThatCannotBeCancelled_HandsOutNoSource()
    {
        // CreateTaskOnlyWithParameter 丢弃了 token，所以它既发不出真取消，也没有源可释放。
        var command = VeloxCommand.CreateTaskOnlyWithParameter(_ => Task.CompletedTask);
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await recorder.FirstExit;

        Assert.IsNull(recorder.Of(CommandEventType.Started)[0].Cts,
            "an execution with no cancellation token has nothing to hand out");
    }
}
