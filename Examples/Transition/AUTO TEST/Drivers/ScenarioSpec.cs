using VeloxDev.AT.Engine;
using VeloxDev.AT.Theory;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Everything a suite needs to know about one demo scenario: how to start it, how long it runs, and — where it
/// animates a scalar — the numbers it has to produce.
/// </summary>
/// <remarks>
/// The scenarios live on the driver, beside the automation ids they click, because they describe what the demo does
/// rather than what a test hopes for. A suite reads them and asserts; it never re-states them, so the numbers have one
/// home and cannot drift between a demo and its test.
/// </remarks>
internal sealed class ScenarioSpec
{
    /// <summary>Which scenario this is.</summary>
    internal required ScenarioId Id { get; init; }

    /// <summary>The automation id of the button that starts the scenario.</summary>
    internal required string ButtonAutomationId { get; init; }

    /// <summary>The token the demo reports in its payload's <c>scen</c> field while this scenario runs.</summary>
    internal required string PayloadToken { get; init; }

    /// <summary>How long the demo animates for; the payload's <c>done</c> is derived from it.</summary>
    internal required TimeSpan Duration { get; init; }

    /// <summary>Set when the scenario animates a scalar the demo tracks a running maximum for.</summary>
    internal ScalarScenario? Scalar { get; init; }

    /// <summary>Set for the colour scenario.</summary>
    internal ColorSpan? Color { get; init; }

    /// <summary>Set for the non-solid fill scenario: the payload field that reports the brush.</summary>
    internal string? BrushKey { get; init; }

    /// <summary>
    /// The value an observed peak has to reach for the run to count as an overshoot rather than as movement.
    /// </summary>
    /// <remarks>
    /// Derived from the curve instead of written down, and it throws when it cannot separate an overshoot from no
    /// movement at all — the anti-vacuity guard, so a run whose bound sits below its own target fails loudly here
    /// rather than passing a bound that means nothing.
    /// </remarks>
    internal double OvershootThreshold => Scalar is null
        ? throw new InvalidOperationException($"Scenario '{Id}' has no scalar target, so it has no overshoot bound.")
        : OvershootCurve.Threshold(Scalar.Ease, Scalar.Start, Scalar.Target, Scalar.ReadoutInterval, Duration);

    public override string ToString()
        => $"{Id} (click {ButtonAutomationId}, {Duration.TotalMilliseconds:F0}ms, token '{PayloadToken}')";
}

/// <summary>The scalar half of a scenario: the curve, the range, and where the demo reports it.</summary>
internal sealed class ScalarScenario
{
    /// <summary>Which curve the demo animates this scalar under.</summary>
    internal required EaseKind Ease { get; init; }

    /// <summary>The value the demo puts the target back to before starting, so a second click is meaningful.</summary>
    internal required double Start { get; init; }

    /// <summary>The value the animation ends on.</summary>
    internal required double Target { get; init; }

    /// <summary>The payload field holding the value's running maximum, for example <c>t0.peak</c>.</summary>
    internal required string PeakKey { get; init; }

    /// <summary>The payload field holding the value itself, for example <c>t0.cur</c>.</summary>
    internal required string CurrentKey { get; init; }

    /// <summary>
    /// How often the demo refreshes its readout. The peak bound is derived from this: a 40ms grid can miss 0.0036 of
    /// Back's peak but 0.1353 of Elastic's, which is why Elastic is held to a looser bound and why tightening it means
    /// shortening the demo's interval — never loosening the assertion.
    /// </summary>
    internal required TimeSpan ReadoutInterval { get; init; }
}

/// <summary>The two ends of the colour scenario.</summary>
internal sealed record ColorSpan(RgbColor Start, RgbColor Target, string CurrentKey);
