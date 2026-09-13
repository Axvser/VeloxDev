namespace VeloxDev.Timing;

/// <summary>
/// Default <see cref="IUncompensatedTimeSampler"/>: the measured interval since the previous sample, carried over
/// from nothing.
/// </summary>
/// <remarks>
/// The whole state is two integers, and it stays correct because it holds no accumulator: a late sample reports a
/// larger interval and a missed one reports a larger one still, but nothing is ever owed. Time spent paused is
/// excluded for free — a paused source does not advance, so the next sample after a resume covers only the frames
/// since the resume rather than the pause. That is why the frame loop's post-resume delta spike does not exist in
/// this design; it was an artefact of measuring against a wall clock while a separate flag said "don't count".
/// </remarks>
public sealed class UncompensatedTimeSampler : TimeSamplerCore, IUncompensatedTimeSampler
{
    private long _lastTicks;
    private long _originTicks;

    public UncompensatedTimeSampler(ITimeSource source) : base(source) => Reset();

    /// <inheritdoc />
    public void Reset()
    {
        _originTicks = _lastTicks = Source.Ticks;
    }

    /// <inheritdoc />
    public TimeSample Sample()
    {
        var now = Source.Ticks;
        var elapsed = now - _lastTicks;

        // 总线没有前进（同一次 tick 内重复采样、暂停中、或 rate 为 0）：不推进基准，交回 default 让调用方跳过这一帧。
        if (elapsed <= 0) return default;

        _lastTicks = now;
        return new TimeSample(ToTimeSpan(elapsed), ToTimeSpan(now - _originTicks), 0, Source.Epoch);
    }
}
