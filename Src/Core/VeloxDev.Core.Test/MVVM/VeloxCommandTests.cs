using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Construction and the plain execution path: every overload builds a command, and a call to one of them
/// reaches the body with the argument it was given.
/// <para>
/// These waits are gated on the lifecycle rather than on a clock — <c>Execute</c> returns as soon as the call is
/// accepted, so a sleep would be the only other way to know the body ran, and a sleep is a flake waiting to
/// happen.
/// </para>
/// </summary>
[TestClass]
public class VeloxCommandTests
{
    [TestMethod]
    public async Task Execute_SyncAction_Completes()
    {
        var called = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new VeloxCommand(() => called.TrySetResult(true));
        var recorder = new CommandEventRecorder(command);

        command.Execute(null);
        await recorder.FirstExit;

        Assert.IsTrue(called.Task.IsCompleted, "the body ran to completion");
    }

    [TestMethod]
    public async Task Execute_ActionWithParameter_ReceivesParameter()
    {
        object? received = null;
        var command = new VeloxCommand((Action<object?>)(p => received = p));
        var recorder = new CommandEventRecorder(command);

        command.Execute("hello");
        await recorder.FirstExit;

        Assert.AreEqual("hello", received);
    }

    [TestMethod]
    public async Task Execute_AsyncFunc_Completes()
    {
        var called = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new VeloxCommand(async () =>
        {
            await Task.Yield();
            called.TrySetResult(true);
        });
        var recorder = new CommandEventRecorder(command);

        command.Execute(null);
        await recorder.FirstExit;

        Assert.IsTrue(called.Task.IsCompleted, "an async body is awaited, not abandoned");
    }

    [TestMethod]
    public void CanExecute_NoPredicate_ReturnsTrue()
    {
        var command = new VeloxCommand(() => { });

        Assert.IsTrue(command.CanExecute(null));
    }

    [TestMethod]
    public void CanExecute_WithPredicate_RespectsIt()
    {
        var command = new VeloxCommand(() => { }, canExecute: p => p is string s && s == "yes");

        Assert.IsTrue(command.CanExecute("yes"));
        Assert.IsFalse(command.CanExecute("no"));
        Assert.IsFalse(command.CanExecute(null));
    }

    [TestMethod]
    public void Constructor_NullCommand_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new VeloxCommand((Func<object?, CancellationToken, Task>)null!));
    }

    [TestMethod]
    public async Task CreateTaskOnlyWithParameter_Works()
    {
        object? received = null;
        var command = VeloxCommand.CreateTaskOnlyWithParameter(p =>
        {
            received = p;
            return Task.CompletedTask;
        });
        var recorder = new CommandEventRecorder(command);

        command.Execute("test");
        await recorder.FirstExit;

        Assert.AreEqual("test", received);
    }
}
