using Microsoft.UI.Dispatching;
using System;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Posts materialization batches to the WinUI dispatcher at <see cref="DispatcherQueuePriority.Low"/>.</summary>
public sealed class ViewScheduler : IViewScheduler<DispatcherQueuePriority>
{
    private readonly DispatcherQueue? _queue;

    // 面板的 DispatcherQueue 由 ViewSourcePipeline 传入；脱离 UI 线程时可能为 null。
    internal ViewScheduler(DispatcherQueue? queue)
    {
        _queue = queue;
    }

    /// <inheritdoc />
    public DispatcherQueuePriority BatchPriority => DispatcherQueuePriority.Low;

    /// <inheritdoc />
    public bool Schedule(Action batch, DispatcherQueuePriority priority)
    {
        // TryEnqueue 只排队、绝不内联；队列正在关闭时返回 false，由池记录并等下一次可见集变化。
        return _queue is not null && _queue.TryEnqueue(priority, () => batch());
    }
}
