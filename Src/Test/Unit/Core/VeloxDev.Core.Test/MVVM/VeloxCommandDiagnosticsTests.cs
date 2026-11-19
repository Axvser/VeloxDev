using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// A subscriber that throws must never disturb the command — a broken <c>Exited</c> handler would otherwise
/// strand the queue, and a broken <c>Completed</c> handler would be reclassified as a failure. That decision
/// keeps the failure invisible, so <see cref="VeloxCommand.HandlerException"/> exists to surface it.
/// <para>
/// The hook does not change the swallowing, and it must not be able to break the command either: it is invoked
/// from inside the same swallow, so a throwing hook is discarded rather than propagated.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]   // 钩子是进程级静态事件，并行运行时别的用例的处理器异常会落进来
public class VeloxCommandDiagnosticsTests
{
    [TestMethod]
    public async Task AThrowingHandler_IsReportedAndLeavesTheLifecycleAlone()
    {
        var boom = new InvalidOperationException("handler blew up");
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Hook(Exception ex) => reported.TrySetResult(ex);

        VeloxCommand.HandlerException += Hook;
        try
        {
            var command = new VeloxCommand(() => Task.CompletedTask);
            var recorder = new CommandEventRecorder(command);
            command.Started += _ => throw boom;

            await command.ExecuteAsync(null);
            await recorder.FirstExit;

            Assert.AreSame(boom, await reported.Task.WaitAsync(CommandTestKit.Timeout),
                "the handler's failure is reported instead of vanishing");
            CollectionAssert.AreEqual(
                new[]
                {
                    CommandEventType.Created, CommandEventType.Started,
                    CommandEventType.Completed, CommandEventType.Exited,
                },
                recorder.Types,
                "a broken subscriber must not change the lifecycle the command reports");
        }
        finally
        {
            VeloxCommand.HandlerException -= Hook;
        }
    }

    [TestMethod]
    public async Task WithNoHookSubscribed_ABrokenHandlerIsSimplySwallowed()
    {
        var command = new VeloxCommand(() => Task.CompletedTask);
        var recorder = new CommandEventRecorder(command);
        command.Started += _ => throw new InvalidOperationException("handler blew up");

        await command.ExecuteAsync(null);
        await recorder.FirstExit;

        Assert.HasCount(1, recorder.Of(CommandEventType.Completed),
            "with nothing subscribed the command behaves exactly as if the hook did not exist");
    }

    [TestMethod]
    public async Task ADiagnosticHookThatThrows_DoesNotTurnASuccessIntoAFailure()
    {
        void Hook(Exception _) => throw new InvalidOperationException("hook blew up");

        VeloxCommand.HandlerException += Hook;
        try
        {
            var command = new VeloxCommand(() => Task.CompletedTask);
            var recorder = new CommandEventRecorder(command);
            command.Started += _ => throw new InvalidOperationException("handler blew up");

            await command.ExecuteAsync(null);
            await recorder.FirstExit;

            // Completed 是在命令体的 try 内发出的：钩子若把异常漏出去，就会被当成命令体自己没有跑好。
            Assert.HasCount(0, recorder.Of(CommandEventType.Failed),
                "a broken diagnostic hook must not rewrite the outcome");
            Assert.HasCount(1, recorder.Of(CommandEventType.Completed));
        }
        finally
        {
            VeloxCommand.HandlerException -= Hook;
        }
    }

    [TestMethod]
    public async Task AThrowingCanExecuteChangedHandler_IsReportedToo()
    {
        var boom = new InvalidOperationException("CanExecuteChanged blew up");
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Hook(Exception ex) => reported.TrySetResult(ex);

        VeloxCommand.HandlerException += Hook;
        try
        {
            var command = new VeloxCommand(() => Task.CompletedTask);
            command.CanExecuteChanged += (_, _) => throw boom;

            command.Notify();

            Assert.AreSame(boom, await reported.Task.WaitAsync(CommandTestKit.Timeout));
        }
        finally
        {
            VeloxCommand.HandlerException -= Hook;
        }
    }
}
