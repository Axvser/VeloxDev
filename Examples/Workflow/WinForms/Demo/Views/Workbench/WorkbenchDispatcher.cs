using System.Windows.Forms;

namespace Demo.Views.Workbench;

/// <summary>
/// The one UI-thread pump for the shell. Easel elements have no <c>BeginInvoke</c>; the host does, and every
/// background callback (Agent tools, MCP status, run catalogs) funnels through it before touching a visual.
/// </summary>
internal static class WorkbenchDispatcher
{
    private static Control? _host;

    /// <summary>Binds the dispatcher to the surface host.</summary>
    public static void Initialize(Control host) => _host = host;

    /// <summary>Runs <paramref name="action"/> on the UI thread, now when already there.</summary>
    public static void Post(Action action)
    {
        var host = _host;
        if (host is null || host.IsDisposed || host.Disposing)
        {
            return;
        }

        if (!host.InvokeRequired)
        {
            action();
            return;
        }

        try
        {
            host.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The host lost its handle while the callback was in flight.
        }
    }

    /// <summary>True when the calling thread is the host's UI thread (or no host is bound yet).</summary>
    public static bool IsUiThread => _host is null || !_host.InvokeRequired;
}
