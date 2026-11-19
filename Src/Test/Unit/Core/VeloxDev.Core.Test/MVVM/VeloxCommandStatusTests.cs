using VeloxDev.Core.Test.WorkflowSystem;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// <see cref="System.Windows.Input.ICommand.CanExecute"/> reports the predicate and the lock, never the queue, so
/// a command with a full slot still looks executable. These members are the read model that can answer it — and
/// because they live on a separate interface, reaching them from a plain <see cref="IVeloxCommand"/> is opt-in
/// and never assumed.
/// </summary>
[TestClass]
public class VeloxCommandStatusTests
{
    [TestMethod]
    public void AnIdleCommand_ReportsItselfAsNotBusy()
    {
        var command = new VeloxCommand(() => Task.CompletedTask);

        Assert.IsFalse(command.IsBusy());
        Assert.AreEqual(0, command.ActiveCount());
        Assert.AreEqual(0, command.PendingCount());
    }

    [TestMethod]
    public async Task ARunningCommand_ReportsItselfAsBusy()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        Assert.IsTrue(command.IsBusy());
        Assert.AreEqual(1, command.ActiveCount());

        gate.Release();
        await CommandTestKit.WaitUntilAsync(() => !command.IsBusy());
        Assert.AreEqual(0, command.ActiveCount());
    }

    [TestMethod]
    public async Task QueuedCalls_AreCountedAsPending()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        _ = command.ExecuteAsync(null);
        _ = command.ExecuteAsync(null);

        await CommandTestKit.WaitUntilAsync(() => command.PendingCount() == 2);
        Assert.IsTrue(command.IsBusy(), "a backlog is exactly what an executable-looking command is hiding");

        gate.Release(3);
        await CommandTestKit.WaitUntilAsync(() => !command.IsBusy());
    }

    [TestMethod]
    public async Task CanExecute_StaysTrueWhileBusy_WhichIsWhyTheseMembersExist()
    {
        var gate = new CommandGate();
        var command = new VeloxCommand(gate.RunAsync, semaphore: 1);

        _ = command.ExecuteAsync(null);
        await gate.WaitForStartedAsync();

        Assert.IsTrue(command.CanExecute(null),
            "CanExecute cannot see the queue, so it is the wrong thing to bind a button to");
        Assert.IsTrue(command.IsBusy(), "IsBusy is the one that can");

        gate.Release();
        await CommandTestKit.WaitUntilAsync(() => !command.IsBusy());
    }

    [TestMethod]
    public void AHandWrittenCommand_ThatDoesNotOptIn_ThrowsRatherThanGuessing()
    {
        // StubCommand 是仓内那 4 个手写实现方之一：它只实现 IVeloxCommand，因此完全不受本轮改动影响。
        var plain = new StubCommand();

        Assert.Throws<NotSupportedException>(() => plain.IsBusy());
        Assert.Throws<NotSupportedException>(() => plain.ActiveCount());
        Assert.Throws<NotSupportedException>(() => plain.ExecuteAndWaitAsync(null));
    }
}
