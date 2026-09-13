using VeloxDev.Timing;

namespace VeloxDev.Core.Test.Timing;

/// <summary>
/// A time source whose position moves only when a test moves it.
/// </summary>
/// <remarks>
/// The default source cannot be the rig for the compensating sampler: the only way to move it forward is
/// <c>Seek</c>, and every seek is a rebase, which the sampler is required to treat as a discontinuity and forget.
/// A source that can be driven forward without a rebase is what makes the step-count assertions exact rather than
/// approximate — no sleeping, no ratios, no tolerance.
/// <para>
/// It has a second job: it implements <see cref="ITimeSourceControl"/> outside Core, which is exactly what a
/// platform is expected to supply, so every test that uses it also proves the contract is implementable without
/// Core's own type.
/// </para>
/// </remarks>
internal class FakeTimeSource : ITimeSourceControl
{
    /// <summary>One tick per <see cref="TimeSpan"/> tick, so a test's milliseconds are a round number of ticks.</summary>
    private const long Frequency = TimeSpan.TicksPerSecond;

    private long _ticks;
    private TaskCompletionSource<bool>? _gate;

    public long TicksPerSecond => Frequency;

    public long Ticks => _ticks;

    public TimeSpan Position => TimeConversion.TicksToTimeSpan(_ticks, Frequency);

    public double Rate { get; private set; } = 1d;

    public bool IsPaused => _gate is not null;

    public bool IsAdvancing => !IsPaused && Rate > 0d;

    public long Epoch { get; private set; }

    /// <summary>Moves the position without rebasing — the drive path a sampler must accumulate over.</summary>
    public void Advance(TimeSpan amount) => _ticks += amount.Ticks;

    /// <summary>Moves the position by whole steps, so the call site reads as the number of steps it owes.</summary>
    public void AdvanceSteps(long steps, TimeSpan step) => Advance(TimeSpan.FromTicks(step.Ticks * steps));

    public void Pause()
    {
        if (_gate is not null) return;
        _gate = NewGate();
        Epoch++;
    }

    public void Resume()
    {
        var gate = _gate;
        if (gate is null) return;
        _gate = null;
        Epoch++;
        gate.TrySetResult(true);
    }

    public void SetRate(double rate)
    {
        Rate = rate;
        Epoch++;
    }

    public void Seek(TimeSpan position)
    {
        _ticks = position.Ticks;
        Epoch++;
        Wake();
    }

    public void Wake()
    {
        var gate = _gate;
        if (gate is null) return;
        _gate = NewGate();
        gate.TrySetResult(true);
    }

    /// <remarks>
    /// A simplified contract: it reports an already-cancelled token but does not race a cancellation against the
    /// parked wait. The racing case is a property of the default source, and is pinned there instead.
    /// </remarks>
    public Task WaitWhileStalledAsync(CancellationToken cancellationToken = default)
    {
        var gate = _gate;
        if (gate is null) return Task.CompletedTask;

        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : gate.Task;
    }

    private static TaskCompletionSource<bool> NewGate()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
