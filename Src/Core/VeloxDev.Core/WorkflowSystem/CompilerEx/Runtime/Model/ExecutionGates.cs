using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>An <see cref="IExecutionGate"/> over a delegate — the one-liner form for a host that already has the logic.</summary>
/// <param name="wait">Asked before each node; return a completed task to proceed.</param>
public sealed class DelegateExecutionGate(Func<CancellationToken, Task> wait) : IExecutionGate
{
    private readonly Func<CancellationToken, Task> _wait = wait ?? throw new ArgumentNullException(nameof(wait));

    /// <inheritdoc />
    public Task WaitAsync(CancellationToken cancellationToken) => _wait(cancellationToken);
}

/// <summary>
/// A gate the host opens and closes by hand: <see cref="Pause"/> holds the run at its next node boundary,
/// <see cref="Resume"/> lets it go. Releasable from any thread — including the one the run is on.
/// </summary>
/// <remarks>
/// <para>
/// Built the way this repository already builds "hold here until someone says go"
/// (<c>VeloxDev.Core.Timing.TimeSourceCore</c>): a <see cref="TaskCompletionSource{TResult}"/> that exists
/// <b>only</b> while the gate is closed, replaced rather than completed in place, and completed <b>outside</b> the
/// lock. The last two are not style — completing under the lock re-enters continuations that may come straight back
/// for the same lock, and a blocking wait on the caller's thread would deadlock a UI-driven run. The same file's
/// other lesson is why no boolean is polled: a run that spun on a flag would burn the thread it is supposed to be
/// yielding.
/// </para>
/// <para>
/// An open gate costs nothing: <see cref="WaitAsync"/> returns a completed task without allocating.
/// </para>
/// </remarks>
public sealed class ManualExecutionGate : IExecutionGate
{
    private readonly object _lock = new();

    /// <summary>The gate while paused, <c>null</c> while the run may proceed.</summary>
    private TaskCompletionSource<bool>? _parked;

    /// <summary>Whether the run is currently held.</summary>
    public bool IsPaused
    {
        get { lock (_lock) return _parked is not null; }
    }

    /// <summary>Holds the run at its next node boundary. Idempotent while already paused.</summary>
    public void Pause()
    {
        lock (_lock) _parked ??= NewGate();
    }

    /// <summary>Lets a held run continue. A no-op when it is not held.</summary>
    public void Resume()
    {
        TaskCompletionSource<bool>? gate;
        lock (_lock)
        {
            gate = _parked;
            _parked = null;
        }

        // 锁外完成：`Cancel()`/`TrySetResult` 会同步跑延续，持锁发信号可能再进同一把锁。
        gate?.TrySetResult(true);
    }

    /// <inheritdoc />
    /// <remarks>Cancellation throws rather than returning: a caller that came back without the gate being opened
    /// would keep driving, which is the opposite of stopping.</remarks>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? gate;
        lock (_lock) gate = _parked;

        return gate is null ? Task.CompletedTask : WaitOnGateAsync(gate, cancellationToken);
    }

    private static async Task WaitOnGateAsync(TaskCompletionSource<bool> gate, CancellationToken cancellationToken)
    {
        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => stop.TrySetResult(true));

        if (await Task.WhenAny(gate.Task, stop.Task).ConfigureAwait(false) == stop.Task)
            throw new OperationCanceledException(cancellationToken);
    }

    private static TaskCompletionSource<bool> NewGate()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
