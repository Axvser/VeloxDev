namespace VeloxDev.AT.Theory;

/// <summary>Which easing curve a scenario animated under.</summary>
internal enum EaseKind
{
    BackOut,
    ElasticOut,
}

/// <summary>
/// An independent implementation of the two easing curves the demos animate under, together with the bound a
/// UI-level observation has to clear to count as an overshoot.
/// </summary>
/// <remarks>
/// Written from the curve definitions rather than by calling into VeloxDev: an oracle that asks the library what
/// the answer is proves nothing about the library.
/// </remarks>
internal static class OvershootCurve
{
    private const double C1 = 1.70158d;
    private const double C3 = C1 + 1d;
    private const double C4 = 2d * Math.PI / 3d;

    /// <summary>The curve, as the library defines it, so a scenario's expected values can be derived here.</summary>
    internal static double Ease(EaseKind kind, double t) => kind switch
    {
        EaseKind.BackOut => 1d + C3 * Math.Pow(t - 1d, 3d) + C1 * Math.Pow(t - 1d, 2d),
        EaseKind.ElasticOut => t == 0d ? 0d : t == 1d ? 1d : Math.Pow(2d, -10d * t) * Math.Sin((t * 10d - 0.75d) * C4) + 1d,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The largest value the curve reaches, and where.</summary>
    /// <remarks>
    /// Scanned rather than solved in closed form: a scan yields both the peak and its position, and its error is
    /// far below anything this decision depends on.
    /// </remarks>
    internal static (double Peak, double At) Scan(EaseKind kind, int steps = 200_000)
    {
        var (peak, at) = (double.NegativeInfinity, 0d);
        for (var index = 0; index <= steps; index++)
        {
            var t = (double)index / steps;
            var value = Ease(kind, t);
            if (value > peak) (peak, at) = (value, t);
        }

        return (peak, at);
    }

    /// <summary>
    /// How much of the peak a UI observer can miss: the demo refreshes its readout on a fixed interval, so the
    /// largest value it can record is the largest the curve held at a grid point, which falls short of the true
    /// peak whenever the peak sits between two of them.
    /// </summary>
    /// <remarks>
    /// The shortfall is taken as the larger of the two one-sided gaps rather than the idealised half-interval one.
    /// The grid is driven by a timer and is therefore not aligned to the animation's start, and understating the
    /// loss is the failure mode that makes a test pass when it should not. Both choices still leave the threshold
    /// above the target, which is checked separately as an anti-vacuity guard.
    /// </remarks>
    internal static double SamplingLoss(EaseKind kind, TimeSpan interval, TimeSpan duration)
    {
        var (peak, at) = Scan(kind);
        var step = interval.TotalSeconds / duration.TotalSeconds;

        var byLeadingSample = peak - Ease(kind, Math.Max(0d, at - step));
        var byTrailingSample = peak - Ease(kind, Math.Min(1d, at + step));
        return Math.Max(byLeadingSample, byTrailingSample);
    }

    /// <summary>The value a UI observer must at least see for the scenario to have genuinely overshot its target.</summary>
    /// <exception cref="InvalidOperationException">
    /// The bound is too loose to tell an overshoot from no movement at all, so the assertion would pass even if the
    /// animation never ran past its target.
    /// </exception>
    internal static double Threshold(EaseKind kind, double start, double target, TimeSpan interval, TimeSpan duration)
    {
        var (peak, _) = Scan(kind);
        var threshold = start + (target - start) * (peak - SamplingLoss(kind, interval, duration));

        if (threshold <= target)
        {
            throw new InvalidOperationException(
                $"A {kind} run from {start} to {target} clears only {threshold:F3} on a {interval.TotalMilliseconds:F0}ms readout over {duration.TotalMilliseconds:F0}ms, "
                + $"which does not clear the target of {target:F3}. The assertion would pass without an overshoot, so the readout interval has to come down or the scenario needs more headroom.");
        }

        return threshold;
    }
}
