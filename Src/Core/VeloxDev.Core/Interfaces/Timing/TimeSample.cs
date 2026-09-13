namespace VeloxDev.Timing;

/// <summary>
/// One reading of a time source: how much time this call covers, how much has been accounted for since the last
/// <see cref="ITimeSampler.Reset"/>, and which rebase epoch the reading was taken under.
/// </summary>
/// <remarks>
/// Deliberately carries no absolute position. A sampler's caller already holds the source and can read
/// <see cref="ITimeSource.Ticks"/> when it needs the absolute value; putting both an absolute and a relative
/// quantity in one value invites reading the wrong one, and for the compensating sampler the two are meant to
/// disagree (the remainder under one step is excluded from <see cref="Total"/> by design).
/// </remarks>
public readonly struct TimeSample
{
    public TimeSample(TimeSpan delta, TimeSpan total, long step, long epoch)
    {
        Delta = delta;
        Total = total;
        Step = step;
        Epoch = epoch;
    }

    /// <summary>
    /// The time this sample covers. For an uncompensated sampler this is the measured interval since the previous
    /// sample; for a compensating one it is the fixed <see cref="ICompensatingTimeSampler.Step"/>, because a fixed
    /// step is what the consumer must integrate with. <see cref="TimeSpan.Zero"/> means nothing advanced and the
    /// caller should not push a frame.
    /// </summary>
    public TimeSpan Delta { get; }

    /// <summary>
    /// Time accounted for since the last <see cref="ITimeSampler.Reset"/>. For a compensating sampler this is
    /// derived from the delivered step count, so it is exact and never absorbs a sub-step remainder.
    /// </summary>
    public TimeSpan Total { get; }

    /// <summary>The number of steps delivered so far; zero for an uncompensated sampler.</summary>
    public long Step { get; }

    /// <summary>
    /// The source's <see cref="ITimeSource.Epoch"/> at the moment of sampling. A change means the source was
    /// rebased (paused, resumed, reseeked or re-rated) and any accumulated state is no longer comparable.
    /// </summary>
    public long Epoch { get; }
}
