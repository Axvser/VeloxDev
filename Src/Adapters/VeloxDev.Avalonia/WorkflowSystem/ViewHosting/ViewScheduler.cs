using Avalonia.Threading;
using System;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Posts materialization batches to Avalonia's UI dispatcher at Background priority.</summary>
public sealed class ViewScheduler : IViewScheduler<DispatcherPriority>
{
    /// <inheritdoc />
    public DispatcherPriority BatchPriority => DispatcherPriority.Background;

    /// <inheritdoc />
    public bool Schedule(Action batch, DispatcherPriority priority)
    {
        Dispatcher.UIThread.Post(batch, priority);
        return true;
    }
}
