using System;
using System.Windows.Threading;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Posts materialization batches to a WPF dispatcher at Background priority.</summary>
public sealed class ViewScheduler : IViewScheduler<DispatcherPriority>
{
    private readonly Dispatcher _dispatcher;

    /// <summary>Creates a scheduler that posts to <paramref name="dispatcher"/>.</summary>
    /// <param name="dispatcher">The host panel's dispatcher.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <see langword="null"/>.</exception>
    public ViewScheduler(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public DispatcherPriority BatchPriority => DispatcherPriority.Background;

    /// <inheritdoc />
    public bool Schedule(Action batch, DispatcherPriority priority)
    {
        _dispatcher.BeginInvoke(batch, priority);
        return true;
    }
}
