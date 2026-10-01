using System.Collections.Concurrent;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Shared helpers for the <see cref="VeloxDev.MVVM.VeloxCommand"/> tests.
/// <para>
/// Waits in this folder are bounded by <see cref="Timeout"/> and driven by signals rather than sleeps:
/// the command pipeline is fully event-driven, so a real clock buys nothing and only adds flakiness.
/// </para>
/// </summary>
internal static class CommandTestKit
{
    // 够长到慢机器不误杀，够短到失败时不拖垮整轮测试。
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Spins until <paramref name="condition"/> holds. Only for the one assertion that is genuinely
    /// about "eventually": the pipeline reports completion strictly before it tears its per-execution
    /// resources down, so there is no later event to await.
    /// </summary>
    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > Timeout.TotalMilliseconds)
            {
                Assert.Fail($"condition was still false after {Timeout}");
            }

            await Task.Delay(1).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="CancellationTokenSource.Token"/> throws once the source is disposed, so this is the
    /// observable probe for "was it disposed" that does not reach into internals.
    /// </summary>
    internal static bool IsDisposed(CancellationTokenSource source)
    {
        try
        {
            _ = source.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}

/// <summary>
/// Subscribes to all eight lifecycle events of a command and records what actually fired, in order.
/// </summary>
internal sealed class CommandEventRecorder
{
    private readonly ConcurrentQueue<CommandEventArgs> _events = new();
    private readonly TaskCompletionSource<bool> _firstExit =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _exitCount;

    internal CommandEventRecorder(IVeloxCommand command)
    {
        command.Created += Record;
        command.Enqueued += Record;
        command.Dequeued += Record;
        command.Started += Record;
        command.Completed += Record;
        command.Failed += Record;
        command.Canceled += Record;
        command.Exited += e =>
        {
            Record(e);
            // 入队在置信号之前，所以等到 Exited 时它前面的事件一定都已经在队列里。
            if (Interlocked.Increment(ref _exitCount) == 1)
            {
                _firstExit.TrySetResult(true);
            }
        };
    }

    private void Record(CommandEventArgs e) => _events.Enqueue(e);

    /// <summary>How many executions have reached <see cref="CommandEventType.Exited"/>.</summary>
    internal int ExitCount => Volatile.Read(ref _exitCount);

    /// <summary>Completes when the first execution raises <see cref="CommandEventType.Exited"/>.</summary>
    internal Task FirstExit => _firstExit.Task.WaitAsync(CommandTestKit.Timeout);

    internal CommandEventType[] Types => [.. _events.Select(e => e.EventType)];

    internal CommandEventArgs[] Events => [.. _events];

    internal CommandEventArgs[] Of(CommandEventType type) => [.. _events.Where(e => e.EventType == type)];
}

/// <summary>
/// A command body that parks until the test hands out a permit, so assertions can be made at the exact
/// moment a body is in flight. <see cref="Release"/> with a count of one, n, or all - no sleeps.
/// </summary>
internal sealed class CommandGate
{
    private readonly SemaphoreSlim _permits = new(0);
    private int _started;

    /// <summary>How many bodies have entered so far.</summary>
    internal int StartedCount => Volatile.Read(ref _started);

    internal void Release(int count = 1) => _permits.Release(count);

    /// <summary>Awaits <paramref name="count"/> bodies having entered.</summary>
    internal Task WaitForStartedAsync(int count = 1) =>
        CommandTestKit.WaitUntilAsync(() => StartedCount >= count);

    internal async Task RunAsync(object? parameter, CancellationToken ct)
    {
        Interlocked.Increment(ref _started);
        // 取消会从这里抛出 OperationCanceledException，正是要被测的那条路径。
        await _permits.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>For bodies that take only the token, e.g. <c>CreateTaskOnlyWithCancellationToken</c>.</summary>
    internal Task RunWithTokenAsync(CancellationToken ct) => RunAsync(null, ct);
}

/// <summary>
/// Records the highest number of command bodies that were ever running at the same time.
/// </summary>
internal sealed class ConcurrencyProbe
{
    private int _current;
    private int _max;

    /// <summary>The peak observed concurrency.</summary>
    internal int Max => Volatile.Read(ref _max);

    internal void Enter()
    {
        var now = Interlocked.Increment(ref _current);
        int seen;
        while (now > (seen = Volatile.Read(ref _max)))
        {
            if (Interlocked.CompareExchange(ref _max, now, seen) == seen)
            {
                break;
            }
        }
    }

    internal void Exit() => Interlocked.Decrement(ref _current);
}
