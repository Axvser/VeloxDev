using System;
using System.Diagnostics;
using VeloxDev.Threading;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Carries one materialization batch to the MAUI dispatcher through a 16 ms timer.</summary>
/// <remarks>
/// <para>
/// MAUI has no dispatcher priority, so <see cref="BatchPriority"/> is <see cref="NonPriority"/> and the
/// one-frame timer is what keeps materialization out of the collection-change call stack.
/// </para>
/// <para>
/// A MAUI non-repeating timer cannot be restarted after it fires, so every accepted batch creates a new
/// one. Only one timer is alive per system; the pool guarantees that at most one batch is pending.
/// </para>
/// </remarks>
public sealed class ViewScheduler : IViewScheduler<NonPriority>, IDisposable
{
    private readonly Layout _host;
    private IDispatcherTimer? _timer;
    private Action? _batch;
    private bool _disposed;

    /// <summary>Creates a scheduler that posts batches through <paramref name="host"/>'s dispatcher.</summary>
    /// <param name="host">The layout whose dispatcher carries the batch.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public ViewScheduler(Layout host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc />
    public NonPriority BatchPriority => default;

    /// <inheritdoc />
    public bool Schedule(Action batch, NonPriority priority)
    {
        if (_disposed)
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(batch);

        _timer?.Stop();
        _batch = batch;

        var timer = _host.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16d); // ≈1 frame
        timer.IsRepeating = false;
        timer.Tick += OnTick;
        _timer = timer;

        try
        {
            timer.Start();
            return true;
        }
        catch (Exception)
        {
            timer.Tick -= OnTick;
            _timer = null;
            _batch = null;
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _batch = null;

        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (sender is IDispatcherTimer timer)
            {
                timer.Stop();
                timer.Tick -= OnTick;
            }

            if (ReferenceEquals(_timer, sender))
            {
                _timer = null;
            }

            var batch = _batch;
            _batch = null;
            batch?.Invoke();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // MAUI 不捕获 IDispatcherTimer.Tick 的异常，它会冒到 WinUI 的 UnhandledException（dotnet/maui #12245）。
            Debug.WriteLine($"[ViewScheduler] Batch error: {exception}");
        }
    }
}
