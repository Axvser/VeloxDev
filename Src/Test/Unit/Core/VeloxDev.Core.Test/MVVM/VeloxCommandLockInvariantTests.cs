using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// <c>_stateLock</c> is a non-reentrant <see cref="SemaphoreSlim"/>, so a subscriber must never be invoked
/// while it is held. These tests drive that invariant from the outside: a handler that takes the same lock
/// must not be able to park the thread that already owns it.
/// <para>
/// Only a BLOCKING wait hangs. A handler that merely calls <see cref="IVeloxCommand.Execute"/> survives even
/// under the lock, because the fire-and-forget call yields on <c>WaitAsync</c>. The handlers here block on
/// purpose, which is what turns "the lock is held" into an observable hang.
/// </para>
/// </summary>
[TestClass]
public class VeloxCommandLockInvariantTests
{
    [TestMethod]
    public async Task AStartedHandlerThatBlocksOnTheStateLock_DoesNotDeadlock()
    {
        var reached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        VeloxCommand? command = null;
        command = new VeloxCommand(() => Task.CompletedTask);
        command.Started += _ =>
        {
            // 处理器若在持锁期间被调用，这一句就是自锁：锁的持有者正是当前线程。
            command!.LockAsync().GetAwaiter().GetResult();
            reached.TrySetResult(true);
        };

        // 必须离开测试线程：旧代码是在 ExecuteAsync 的同步前缀里把当前线程锁死的。
        _ = Task.Run(() => command.ExecuteAsync(null));

        await reached.Task.WaitAsync(CommandTestKit.Timeout);
    }

    [TestMethod]
    public async Task AnEnqueuedHandlerThatBlocksOnTheStateLock_DoesNotDeadlock()
    {
        var gate = new CommandGate();
        var reached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        VeloxCommand? command = null;
        command = new VeloxCommand(gate.RunAsync, semaphore: 1);
        command.Enqueued += _ =>
        {
            command!.LockAsync().GetAwaiter().GetResult();
            reached.TrySetResult(true);
        };

        _ = Task.Run(() => command.ExecuteAsync(null));
        await gate.WaitForStartedAsync();                    // 第一个占住唯一的槽位

        _ = Task.Run(() => command.ExecuteAsync(null));      // 第二个只能排队

        await reached.Task.WaitAsync(CommandTestKit.Timeout);

        gate.Release(2);                                     // 收尾：别把停着的命令体留给后面
    }
}
