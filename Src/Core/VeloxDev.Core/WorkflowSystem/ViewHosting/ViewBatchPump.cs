using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Runs materialization batches back to back on a platform dispatcher, yielding after a continuous budget so
/// rendering and input get a turn.
/// </summary>
/// <remarks>
/// <para>
/// A platform scheduler hands this pump an enqueue delegate and its two priorities: the batch priority for the first
/// post of a burst, and the yield priority used once the continuous budget is spent. A batch is never run from
/// <see cref="Schedule(Action, TPriority)"/> itself, so materialization stays out of the collection-change call stack.
/// </para>
/// <para>
/// If the yield post is refused (the dispatcher is shutting down), the remaining batches run inline so the pool's
/// scheduled flag cannot stay set with work still pending.
/// </para>
/// </remarks>
/// <typeparam name="TPriority">The platform dispatcher priority type.</typeparam>
public sealed class ViewBatchPump<TPriority>
{
    private readonly Func<TPriority, Action, bool> enqueue;
    private readonly TPriority yieldPriority;
    private readonly Func<double> elapsedMilliseconds;
    private readonly double budgetMilliseconds;
    private Action? pending;
    private bool pumping;

    /// <summary>Creates a pump over <paramref name="enqueue"/>.</summary>
    /// <param name="enqueue">Posts an action at a priority; returns <see langword="false"/> when the dispatcher refuses it.</param>
    /// <param name="yieldPriority">The priority used after the continuous budget is spent.</param>
    /// <param name="budgetMilliseconds">Continuous work allowed before yielding; zero yields after every batch.</param>
    /// <param name="elapsedMilliseconds">
    /// A monotonic millisecond clock; <see langword="null"/> uses <see cref="System.Diagnostics.Stopwatch"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="enqueue"/> is <see langword="null"/>.</exception>
    public ViewBatchPump(
        Func<TPriority, Action, bool> enqueue,
        TPriority yieldPriority,
        double budgetMilliseconds = 48d,
        Func<double>? elapsedMilliseconds = null)
    {
        this.enqueue = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
        this.yieldPriority = yieldPriority;
        this.budgetMilliseconds = Math.Max(0d, budgetMilliseconds);
        this.elapsedMilliseconds = elapsedMilliseconds ?? DefaultClock;
    }

    /// <summary>Gets whether a pump callback is currently posted or running.</summary>
    public bool IsPumping => pumping;

    /// <summary>Gets the number of batches waiting for the pump.</summary>
    public bool HasPending => pending is not null;

    /// <summary>Queues a batch, posting the pump when it is not already running.</summary>
    /// <param name="batch">The work to run on the dispatcher.</param>
    /// <param name="priority">The priority for the first post of this burst.</param>
    /// <returns><see langword="false"/> when the dispatcher refused the post.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public bool Schedule(Action batch, TPriority priority)
    {
        if (batch is null) throw new ArgumentNullException(nameof(batch));

        // The pool guarantees at most one batch in flight, so a single slot is enough; a running pump picks
        // the batch up instead of taking another dispatcher round trip.
        pending = batch;
        if (pumping)
        {
            return true;
        }

        pumping = true;
        if (enqueue(priority, Pump))
        {
            return true;
        }

        pumping = false;
        pending = null;
        return false;
    }

    private void Pump()
    {
        var started = elapsedMilliseconds();
        while (true)
        {
            var batch = pending;
            pending = null;
            if (batch is null)
            {
                pumping = false;
                return;
            }

            try
            {
                batch();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // 与逐批投递时的行为一致：异常不能留在队列回调里终结进程。
                System.Diagnostics.Debug.WriteLine($"[ViewBatchPump] Batch failed: {exception}");
                pumping = false;
                pending = null;
                return;
            }

            if (pending is null)
            {
                pumping = false;
                return;
            }

            if (elapsedMilliseconds() - started < budgetMilliseconds)
            {
                continue;
            }

            // 连续预算用完：把泵放回低优先级，让渲染与输入插进来。
            if (!enqueue(yieldPriority, Pump))
            {
                // 队列已关闭：没有帧可让，剩余批次就地跑完，避免池的 scheduled 旗标悬挂。
                RunPendingInline();
            }

            return;
        }
    }

    private void RunPendingInline()
    {
        while (pending is { } batch)
        {
            pending = null;
            try
            {
                batch();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewBatchPump] Batch failed during shutdown: {exception}");
                break;
            }
        }

        pumping = false;
        pending = null;
    }

    private static double DefaultClock()
        => System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency;
}
