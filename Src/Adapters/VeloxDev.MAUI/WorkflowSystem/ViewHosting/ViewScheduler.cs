using System;
using System.Diagnostics;
using VeloxDev.Threading;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Carries one materialization batch to the MAUI dispatcher asynchronously.</summary>
/// <remarks>
/// <para>
/// MAUI has no dispatcher priority, so <see cref="BatchPriority"/> is <see cref="NonPriority"/>. The
/// pool already slices work by its time budget; the scheduler only has to keep that work out of the
/// collection-change call stack.
/// </para>
/// <para>
/// A batch is posted to the dispatcher instead of being started from a frame timer. A frame timer on
/// Windows is quantized to the display period, so a batch that exhausts its budget would still idle a
/// full frame before the next one starts; while a large visible set materializes, that idle time is a
/// large share of the stage. Consecutive batches are posted directly, but after
/// <see cref="ContinuousBudgetMilliseconds"/> of continuous batch work the next one goes back on the
/// frame timer so rendering and input still get a turn.
/// </para>
/// </remarks>
public sealed class ViewScheduler : IViewScheduler<NonPriority>, IDisposable
{
    // 连续投递的上限：超过就把下一批放回帧计时器，避免大批物化时整段窗口不刷新。
    private const double ContinuousBudgetMilliseconds = 32d;

    private readonly Layout _host;
    private Action? _batch;
    private double _continuousMilliseconds;
    private int _generation;
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

        var generation = ++_generation;
        _batch = batch;

        try
        {
            if (_continuousMilliseconds < ContinuousBudgetMilliseconds)
            {
                // 调度器投递是异步的，不会在集合变更调用栈里内联执行。
                if (_host.Dispatcher.Dispatch(() => Run(generation)))
                {
                    return true;
                }

                _batch = null;
                return false;
            }

            _continuousMilliseconds = 0d;
            return ScheduleOnFrameTimer(generation);
        }
        catch (Exception)
        {
            _batch = null;
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _batch = null;
        _generation++;
        _continuousMilliseconds = 0d;
    }

    private bool ScheduleOnFrameTimer(int generation)
    {
        var timer = _host.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16d); // ≈1 frame
        timer.IsRepeating = false;

        void OnTick(object? sender, EventArgs e)
        {
            if (sender is IDispatcherTimer dispatcherTimer)
            {
                dispatcherTimer.Stop();
                dispatcherTimer.Tick -= OnTick;
            }

            Run(generation);
        }

        timer.Tick += OnTick;

        try
        {
            timer.Start();
            return true;
        }
        catch (Exception)
        {
            timer.Tick -= OnTick;
            _batch = null;
            return false;
        }
    }

    private void Run(int generation)
    {
        if (_disposed || generation != _generation)
        {
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var batch = _batch;
        _batch = null;
        try
        {
            batch?.Invoke();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // MAUI 不捕获 IDispatcherTimer.Tick 的异常，它会冒到 WinUI 的 UnhandledException（dotnet/maui #12245）。
            Debug.WriteLine($"[ViewScheduler] Batch error: {exception}");
        }
        finally
        {
            _continuousMilliseconds += (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        }
    }
}
