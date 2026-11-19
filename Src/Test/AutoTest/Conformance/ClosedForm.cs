namespace VeloxDev.AT.Conformance;

/// <summary>
/// The arithmetic the closed forms are built from — an independent restatement of the rules the library follows, not a
/// call into it.
/// </summary>
/// <remarks>
/// This project deliberately references none of the product adapters, so nothing here can accidentally be answered by
/// the sampler under test. That is the whole point: the expectation has to be written from the rule.
/// </remarks>
internal static class ClosedForm
{
    internal static double Lerp(double start, double end, double t) => start + (end - start) * t;

    internal static double Clamp01(double value) => value <= 0d ? 0d : value >= 1d ? 1d : value;

    /// <summary>
    /// A group of channels sharing one progress: the group moves together and stops at whichever channel reaches a
    /// limit first, so a later channel can never loosen it. Bounds are <c>[0, maximum]</c>.
    /// </summary>
    /// <param name="maximum">The group's upper bound — 255 for an 8-bit colour channel, <c>+∞</c> for a size.</param>
    internal static double SharedProgress(double t, double maximum, params (double Start, double End)[] channels)
    {
        var progress = t;
        foreach (var (start, end) in channels)
        {
            var delta = end - start;
            if (delta == 0d) continue;

            var byMaximum = (maximum - start) / delta;
            var byMinimum = (0d - start) / delta;
            var lower = Math.Min(byMaximum, byMinimum);
            var upper = Math.Max(byMaximum, byMinimum);

            if (upper < progress) progress = upper;
            if (lower > progress) progress = lower;
        }

        return progress;
    }

    /// <summary>
    /// An 8-bit colour channel: saturate, never wrap — and truncate rather than round, which is what the framework's
    /// own byte conversion does.
    /// </summary>
    internal static double Channel(double value)
        => value <= 0d ? 0d : value >= 255d ? 255d : Math.Truncate(value);

    /// <summary>
    /// A produced string as its character code points.
    /// </summary>
    /// <remarks>
    /// A sampler whose product is a string — the Razor adapter's is a CSS colour — still has to fit a payload whose
    /// values are numbers, and CSS colours contain commas, so writing them raw would break the field separators.
    /// Code points are unambiguous, need no escaping, and stay exact character for character.
    /// </remarks>
    internal static double[] CodePoints(string text) => [.. text.Select(character => (double)character)];

    /// <summary>
    /// An ARGB colour interpolated the way every adapter's colour sampler does it: R/G/B share one <c>[0,255]</c>
    /// progress so an overshoot cannot shift the hue, alpha keeps the full eased time on its own.
    /// </summary>
    internal static double[] ColorAt(double t, (int A, int R, int G, int B) from, (int A, int R, int G, int B) to)
    {
        var progress = SharedProgress(t, 255d, (from.R, to.R), (from.G, to.G), (from.B, to.B));

        return
        [
            Channel(Lerp(from.A, to.A, t)),
            Channel(Lerp(from.R, to.R, progress)),
            Channel(Lerp(from.G, to.G, progress)),
            Channel(Lerp(from.B, to.B, progress)),
        ];
    }
}
