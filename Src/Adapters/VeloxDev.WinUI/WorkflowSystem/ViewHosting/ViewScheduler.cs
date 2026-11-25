using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Runs materialization batches on the WinUI dispatcher and yields a frame after a continuous burst.</summary>
/// <remarks>
/// <para>
/// The pool already slices materialization by its time budget; the scheduler decides how promptly the
/// next slice may run. A batch posted at <see cref="DispatcherQueuePriority.Low"/> is drained together
/// with the frame work, so a large visible set materializes roughly one frame per batch: a 5000-view
/// pass spends much of its wall time waiting between batches rather than creating views.
/// </para>
/// <para>
/// The first batch of a burst is therefore posted at <see cref="DispatcherQueuePriority.Normal"/>, and
/// the batches the pool schedules while that callback is still running are executed by the same pump
/// instead of taking another dispatcher round trip. After
/// <see cref="ContinuousBudgetMilliseconds"/> of continuous work the pump posts itself at
/// <see cref="DispatcherQueuePriority.Low"/> so rendering and input get a turn. A batch is still never
/// invoked from <see cref="Schedule"/> itself, so materialization stays out of the collection-change
/// call stack.
/// </para>
/// </remarks>
public sealed class ViewScheduler : IViewScheduler<DispatcherQueuePriority>
{
    // 连续批次的运行时长上限；超过后下一批回到 Low，让渲染与输入插进来。
    private const double ContinuousBudgetMilliseconds = 48d;

    private readonly DispatcherQueue? _queue;
    private Action? _pending;
    private bool _pumping;

    // 面板的 DispatcherQueue 由 ViewSourcePipeline 传入；脱离 UI 线程时可能为 null。
    internal ViewScheduler(DispatcherQueue? queue)
    {
        _queue = queue;
    }

    /// <inheritdoc />
    public DispatcherQueuePriority BatchPriority => DispatcherQueuePriority.Normal;

    /// <inheritdoc />
    public bool Schedule(Action batch, DispatcherQueuePriority priority)
    {
        if (_queue is null)
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(batch);

        // 池保证同一时刻最多一批在途，因此单槽足够；泵在跑时由它接着执行，不再另排一次回调。
        _pending = batch;
        if (_pumping)
        {
            return true;
        }

        _pumping = true;
        if (_queue.TryEnqueue(priority, Pump))
        {
            return true;
        }

        _pumping = false;
        _pending = null;
        return false;
    }

    private void Pump()
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            var batch = _pending;
            _pending = null;
            if (batch is null)
            {
                _pumping = false;
                return;
            }

            try
            {
                batch();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // 与逐批投递时的行为一致：异常不能留在队列回调里终结进程。
                Debug.WriteLine($"[ViewScheduler] Batch failed: {exception}");
                _pumping = false;
                _pending = null;
                return;
            }

            if (_pending is null)
            {
                _pumping = false;
                return;
            }

            if ((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency < ContinuousBudgetMilliseconds)
            {
                continue;
            }

            // 连续预算用完：把泵放回 Low，同帧工作一起执行，真正让出一帧。
            if (!_queue!.TryEnqueue(DispatcherQueuePriority.Low, Pump))
            {
                _pumping = false;
                _pending = null;
            }

            return;
        }
    }
}
