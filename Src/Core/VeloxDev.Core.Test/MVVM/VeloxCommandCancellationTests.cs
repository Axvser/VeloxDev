using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Whether an interrupt actually stops the work, which depends entirely on how the command was built.
/// <para>
/// Only a body that takes a <see cref="CancellationToken"/> can observe the cancel. The other overloads hand
/// the body no token at all, so an interrupted execution reports <see cref="CommandEventType.Canceled"/> while
/// the body quietly runs to completion — the most surprising corner of this module, and the reason these tests
/// exist rather than just a comment.
/// </para>
/// </summary>
[TestClass]
public class VeloxCommandCancellationTests
{
    [TestMethod]
    public async Task ATokenBody_ReallyStopsWhenInterrupted()
    {
        var gate = new CommandGate();
        var command = VeloxCommand.CreateTaskOnlyWithCancellationToken(ct => gate.RunWithTokenAsync(ct));
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        await command.InterruptAsync();
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount >= 1);

        Assert.IsTrue(recorder.Of(CommandEventType.Canceled).Length >= 1, "the interrupt is reported");
        Assert.HasCount(0, recorder.Of(CommandEventType.Failed),
            "a body that honoured the token surfaces as cancelled, not as failed");
    }

    [TestMethod]
    public async Task AParameterOnlyBody_ReportsCanceledButKeepsRunning()
    {
        var gate = new CommandGate();
        var bodyFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var command = VeloxCommand.CreateTaskOnlyWithParameter(async _ =>
        {
            // 这个重载拿到的是 CancellationToken.None，所以它根本无从观察取消。
            await gate.RunAsync(null, CancellationToken.None);
            bodyFinished.TrySetResult(true);
        });
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        await command.InterruptAsync();
        await CommandTestKit.WaitUntilAsync(() => recorder.Of(CommandEventType.Canceled).Length >= 1);

        Assert.IsFalse(bodyFinished.Task.IsCompleted,
            "with no token to observe, the body cannot have been stopped - only the bookkeeping says it was");

        gate.Release();
        await bodyFinished.Task.WaitAsync(CommandTestKit.Timeout);
        await recorder.FirstExit;
    }

    [TestMethod]
    public async Task InterruptingARunningBody_ReportsCanceledTwice()
    {
        var gate = new CommandGate();
        var command = VeloxCommand.CreateTaskOnlyWithCancellationToken(ct => gate.RunWithTokenAsync(ct));
        var recorder = new CommandEventRecorder(command);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        await command.InterruptAsync();
        await CommandTestKit.WaitUntilAsync(() => recorder.ExitCount >= 1);

        Assert.HasCount(2, recorder.Of(CommandEventType.Canceled),
            "the interrupt raises one cancel and the body's own OperationCanceledException raises the second");
    }

    [TestMethod]
    public async Task InterruptingAnUnknownCall_DoesNotFailIt()
    {
        var command = VeloxCommand.CreateTaskOnlyWithCancellationToken(_ => Task.CompletedTask);
        var recorder = new CommandEventRecorder(command);

        await command.ExecuteAsync(null);
        await recorder.FirstExit;
        await command.InterruptAsync();          // 没有在跑的东西可打断

        Assert.HasCount(0, recorder.Of(CommandEventType.Failed), "interrupting an idle command is a no-op, not an error");
    }
}
