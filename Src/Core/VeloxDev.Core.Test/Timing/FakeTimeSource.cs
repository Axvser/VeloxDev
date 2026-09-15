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
/// Core's own type. That includes the half a <em>pulled</em> clock never exercises — <see cref="Stall"/> models the
/// host that owns the clock falling silent, which freezes the position without pausing anything, and is the case
/// <see cref="ITimeSource.IsAdvancing"/> is stated as an invariant to cover.
/// </para>
/// </remarks>
internal class FakeTimeSource : ITimeSourceControl
{
    /// <summary>One tick per <see cref="TimeSpan"/> tick, so a test's milliseconds are a round number of ticks.</summary>
    private const long Frequency = TimeSpan.TicksPerSecond;

    private long _ticks;
    private bool _paused;
    private bool _feeding = true;
    private TaskCompletionSource<bool>? _gate;

    public long TicksPerSecond => Frequency;

    public long Ticks => _ticks;

    public TimeSpan Position => TimeConversion.TicksToTimeSpan(_ticks, Frequency);

    public double Rate { get; private set; } = 1d;

    public bool IsPaused => _paused;

    public bool IsAdvancing => _feeding && !_paused && Rate > 0d;

    public long Epoch { get; private set; }

    /// <summary>Moves the position without rebasing — the drive path a sampler must accumulate over.</summary>
    public void Advance(TimeSpan amount) => _ticks += amount.Ticks;

    /// <summary>Moves the position by whole steps, so the call site reads as the number of steps it owes.</summary>
    public void AdvanceSteps(long steps, TimeSpan step) => Advance(TimeSpan.FromTicks(step.Ticks * steps));

    /// <summary>
    /// The host that owns the clock stops delivering: the position freezes, and nobody has called
    /// <see cref="Pause"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not a rebase — the position picks up where it stopped, so <see cref="Epoch"/> does not move.
    /// </remarks>
    public void Stall()
    {
        if (!_feeding) return;
        _feeding = false;
        RefreshGate();
    }

    /// <summary>The host starts delivering again.</summary>
    public void Feed()
    {
        if (_feeding) return;
        _feeding = true;
        RefreshGate();
    }

    public void Pause()
    {
        if (_paused) return;
        _paused = true;
        Epoch++;
        RefreshGate();
    }

    public void Resume()
    {
        if (!_paused) return;
        _paused = false;
        Epoch++;
        RefreshGate();
    }

    public void SetRate(double rate)
    {
        Rate = rate;
        Epoch++;

        // 速率归零会让时钟冻住，和暂停一样必须让 park 信号跟上——契约要求这两者永不互相矛盾。
        RefreshGate();
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

    /// <summary>
    /// Installs or clears the park signal from <see cref="IsAdvancing"/>, the rule the default source keeps in one
    /// place. Cleared on the way out, never on the way in: a gate completed but left installed would hand the next
    /// wait an already-finished task and let a parked consumer spin.
    /// </summary>
    private void RefreshGate()
    {
        if (IsAdvancing)
        {
            var gate = _gate;
            _gate = null;
            gate?.TrySetResult(true);
            return;
        }

        // 停摆时必须有一个未完成的 gate 装着：否则 while (!IsAdvancing) await WaitWhileStalledAsync() 会立刻返回、
        // 立刻重进，把一整核热转掉。
        _gate ??= NewGate();
    }

    private static TaskCompletionSource<bool> NewGate()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
