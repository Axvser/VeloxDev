namespace VeloxDev.Timing;

/// <summary>
/// Default <see cref="ICompensatingTimeSampler"/>: a fixed-step accumulator whose delivered step count equals
/// <c>floor(elapsed / <see cref="Step"/>)</c> with no drift.
/// </summary>
/// <remarks>
/// Three quantities, kept apart on purpose, because collapsing any two of them is what makes a fixed-step loop
/// drift:
/// <list type="bullet">
/// <item><c>_acc</c> — time not yet worth a whole step. Carried, never rounded away; this is the no-drift part.</item>
/// <item><c>_earned</c> — steps time has paid for. Under a cap this runs ahead of what has been handed out.</item>
/// <item><c>_delivered</c> — steps actually reported. The virtual clock is this times <see cref="Step"/>, so it
/// stays a function of the integer count and never of a measured interval.</item>
/// </list>
/// <para>
/// All arithmetic is in the source's integer ticks: no float, so no accumulation error however long it runs, and
/// the count comparison is exact rather than approximate.
/// </para>
/// </remarks>
public sealed class CompensatingTimeSampler : TimeSamplerCore, ICompensatingTimeSampler
{
    /// <summary>Catch-up burst allowed per call when nothing else is specified.</summary>
    public const int DefaultMaxStepsPerCall = 8;

    /// <summary>
    /// Debt allowed to accumulate when nothing else is specified — about a second at a 16 ms step. A hitch, a slow
    /// frame or a briefly stalled thread stays far inside it; reaching it means the consumer cannot keep up at all.
    /// </summary>
    public const int DefaultMaxPendingSteps = 64;

    private long _stepTicks;
    private TimeSpan _step;

    private int _maxStepsPerCall = DefaultMaxStepsPerCall;
    private int _maxPendingSteps = DefaultMaxPendingSteps;

    private long _lastTicks;
    private long _epoch;
    private long _acc;
    private long _earned;
    private long _delivered;
    private long _dropped;

    public CompensatingTimeSampler(ITimeSource source, TimeSpan step) : base(source)
    {
        Step = step;
        Reset();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Changing the step drops the sub-step remainder, which was measured as a fraction of the old step and is not
    /// a fraction of the new one. Steps already delivered are untouched, so the count stays continuous.
    /// </remarks>
    public TimeSpan Step
    {
        get => _step;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A fixed step must be positive.");
            }

            _step = value;
            _stepTicks = ToTicks(value);
            if (_stepTicks <= 0) _stepTicks = 1; // 总线单位比 TimeSpan 的 100ns 还粗时，一步至少占一个 tick
            _acc = 0;
        }
    }

    /// <inheritdoc />
    public int MaxStepsPerCall
    {
        get => _maxStepsPerCall;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "At least one step per call must be allowed.");
            }

            _maxStepsPerCall = value;
        }
    }

    /// <inheritdoc />
    public int MaxPendingSteps
    {
        get => _maxPendingSteps;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "At least one step of debt must be allowed.");
            }

            _maxPendingSteps = value;
        }
    }

    /// <inheritdoc />
    public long PendingSteps => _earned - _delivered;

    /// <inheritdoc />
    public long DroppedSteps => _dropped;

    /// <inheritdoc />
    public TimeSpan TimeToNextStep
        => _earned > _delivered ? TimeSpan.Zero : ToTimeSpan(_stepTicks - _acc);

    /// <inheritdoc />
    public void Reset()
    {
        _lastTicks = Source.Ticks;
        _epoch = Source.Epoch;
        _acc = 0;
        _earned = 0;
        _delivered = 0;
        _dropped = 0;
    }

    /// <inheritdoc />
    public int Advance(out TimeSample sample)
    {
        var epoch = Source.Epoch;
        var now = Source.Ticks;

        // 总线 rebase（pause/resume/seek/SetRate）后累计基准失效：欠账按 epoch 丢弃，绝不事后补还。
        // 向后 seek 若照常结算会还出负账，向前 seek 会凭空补出一串从未发生过的步。
        // 暂停也走这条路，于是「暂停期间一步不欠」是构造性的，不需要额外判断。
        if (epoch != _epoch)
        {
            _epoch = epoch;
            _lastTicks = now;
            _acc = 0;
            _earned = _delivered;
            sample = default;
            return 0;
        }

        var elapsed = now - _lastTicks;
        if (elapsed > 0)
        {
            _lastTicks = now;
            _acc += elapsed;

            // 先整步后余数：中间量都不超过累计值本身。
            _earned += _acc / _stepTicks;
            _acc %= _stepTicks;
        }

        var pending = _earned - _delivered;
        if (pending > _maxPendingSteps)
        {
            // 顶到上限：宽恕超出部分并计数。不宽恕的话，被挂起过的机器会欠下几百万步，
            // 而按 MaxStepsPerCall 限速偿还期间消费者要比直接宽恕落后得更久。
            var forgiven = pending - _maxPendingSteps;
            _delivered += forgiven;
            _dropped += forgiven;
            pending = _maxPendingSteps;
        }

        var count = (int)Math.Min(pending, _maxStepsPerCall);
        _delivered += count;

        // Total 由交付步数派生，永远不吃余数——这正是它比「实测累计时间」更适合做积分基准的原因。
        sample = count > 0
            ? new TimeSample(_step, TimeSpan.FromTicks(_delivered * _step.Ticks), _delivered, epoch)
            : default;
        return count;
    }
}
