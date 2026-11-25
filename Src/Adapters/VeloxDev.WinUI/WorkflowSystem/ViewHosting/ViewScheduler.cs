using Microsoft.UI.Dispatching;
using System;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Runs materialization batches on the WinUI dispatcher and yields a frame after a continuous burst.</summary>
/// <remarks>
/// <para>
/// The pool already slices materialization by its time budget; the scheduler decides how promptly the next slice
/// may run. A batch posted at <see cref="DispatcherQueuePriority.Low"/> is drained together with the frame work, so
/// a large visible set materializes roughly one frame per batch. The first batch of a burst therefore goes out at
/// <see cref="DispatcherQueuePriority.Normal"/>, and the batches the pool schedules while that callback runs are
/// executed by <see cref="ViewBatchPump{TPriority}"/> instead of taking another dispatcher round trip. After the
/// continuous budget the pump posts itself at <see cref="DispatcherQueuePriority.Low"/> so rendering and input get
/// a turn.
/// </para>
/// <para>
/// A batch is never invoked from <see cref="Schedule"/> itself, and a refused post — including the yield post
/// during dispatcher shutdown — leaves no pending batch behind: the pump runs the remaining batches inline.
/// </para>
/// </remarks>
public sealed class ViewScheduler : IViewScheduler<DispatcherQueuePriority>
{
    private readonly ViewBatchPump<DispatcherQueuePriority>? _pump;

    // 面板的 DispatcherQueue 由 ViewSourcePipeline 传入；脱离 UI 线程时可能为 null。
    internal ViewScheduler(DispatcherQueue? queue)
    {
        if (queue is not null)
        {
            _pump = new ViewBatchPump<DispatcherQueuePriority>(
                (priority, action) => queue.TryEnqueue(priority, () => action()),
                DispatcherQueuePriority.Low);
        }
    }

    /// <inheritdoc />
    public DispatcherQueuePriority BatchPriority => DispatcherQueuePriority.Normal;

    /// <inheritdoc />
    public bool Schedule(Action batch, DispatcherQueuePriority priority)
        => _pump is not null && _pump.Schedule(batch, priority);
}
