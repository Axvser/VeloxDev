using System;
using Jalium.UI.Controls;
using Jalium.UI.Threading;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Posts materialization batches to the host's dispatcher at <see cref="DispatcherPriority.Background"/>.</summary>
public sealed class ViewScheduler : IViewScheduler<DispatcherPriority>
{
    private readonly Dispatcher _dispatcher;

    /// <summary>Creates a scheduler bound to <paramref name="host"/>'s dispatcher.</summary>
    /// <param name="host">The panel whose dispatcher receives the batches.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public ViewScheduler(Panel host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _dispatcher = host.Dispatcher;
    }

    /// <inheritdoc />
    public DispatcherPriority BatchPriority => DispatcherPriority.Background;

    /// <inheritdoc />
    public bool Schedule(Action batch, DispatcherPriority priority)
    {
        _dispatcher.BeginInvoke(priority, batch);
        return true;
    }
}
