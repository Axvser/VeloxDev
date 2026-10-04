namespace VeloxDev.TransitionSystem;

/// <summary>
/// The shared progress of a group of channels that have to stay inside a range, capped at the eased time.
/// </summary>
/// <remarks>
/// An easing curve may overshoot past 1, and a group of channels has to move by one progress or the value is
/// distorted rather than pushed past its target: interpolating colour channels one by one lets red saturate while
/// green keeps climbing, which shifts the hue. This finds the largest progress that keeps every added channel inside
/// <c>[minimum, maximum]</c> and never lets a later channel loosen it — the group rises as a whole and stops at the
/// first channel to reach a limit.
/// <para>
/// The remaining overshoot is dropped for that group, and that is unavoidable rather than a compromise: a value
/// that keeps its proportions cannot go past a limit without leaving the range its type can represent. A channel
/// with no limit is simply not added, so it keeps the full eased time. Alpha is the usual example: it is its own
/// single-channel range, and letting it into this group would let an opacity that is already opaque truncate the
/// colour's overshoot.
/// </para>
/// <para>
/// For an eased time in [0,1] the result is always that same time: interpolating between two in-range endpoints
/// stays in range, so no channel can exit early. Only an overshoot is affected.
/// </para>
/// </remarks>
public struct BoundedProgress
{
    private readonly double _minimum;
    private readonly double _maximum;
    private double _progress;

    /// <summary>
    /// Starts a group at <paramref name="t"/>, bounded to <c>[minimum, maximum]</c>. Add the channels that share the
    /// bound with <see cref="Add"/>.
    /// </summary>
    public BoundedProgress(double t, double minimum = 0d, double maximum = 1d)
    {
        _minimum = minimum;
        _maximum = maximum;
        _progress = t;
    }

    /// <summary>
    /// Adds a channel to the group. Only ever tightens the progress, so the order channels are added in does not
    /// matter.
    /// </summary>
    /// <remarks>
    /// An endpoint that is already outside the range on its own — possible for the float-based colour types, where
    /// nothing stops a caller from passing a value above 1 — pulls the group back to the limit rather than letting
    /// the invalid value through.
    /// </remarks>
    public void Add(double start, double end)
    {
        var delta = end - start;
        if (delta == 0d) return;

        // Both ends of the range bound the progress, not just the far one: a channel leaves by the maximum on an
        // overshoot and by the minimum on an anticipation, and the group has to stop at whichever comes first. A
        // channel whose delta is positive is bounded above by the maximum and below by the minimum; a negative
        // delta swaps the two. Taking the tighter of the pair, then clamping, keeps the result independent of the
        // order channels are added in.
        var byMaximum = (_maximum - start) / delta;
        var byMinimum = (_minimum - start) / delta;
        var lower = Math.Min(byMaximum, byMinimum);
        var upper = Math.Max(byMaximum, byMinimum);

        if (upper < _progress) _progress = upper;
        if (lower > _progress) _progress = lower;
    }

    /// <summary>The progress the whole group moves by.</summary>
    public readonly double Progress => _progress;

    /// <summary>Interpolates one of the group's channels at <see cref="Progress"/>.</summary>
    public readonly double At(double start, double end) => start + (end - start) * _progress;
}
