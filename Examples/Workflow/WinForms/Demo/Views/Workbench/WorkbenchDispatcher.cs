using Easel.Visuals;

namespace Demo.Views.Workbench;

/// <summary>
/// The one UI-thread pump for the shell. Cards and nested run tasks raise events from background threads; they
/// funnel through the hosting <see cref="VisualHost.Post"/> before touching a visual.
/// </summary>
internal static class WorkbenchDispatcher
{
    private static VisualHost? _host;

    /// <summary>Binds the dispatcher to the surface host.</summary>
    public static void Initialize(VisualHost host) => _host = host;

    /// <summary>Runs <paramref name="action"/> on the UI thread; dropped once the host is gone.</summary>
    public static void Post(Action action)
    {
        if (_host is { IsDisposed: false, Disposing: false } host)
        {
            host.Post(action);
        }
    }
}
