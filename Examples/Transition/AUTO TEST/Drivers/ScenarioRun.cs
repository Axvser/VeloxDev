using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// What one scenario run produced: every payload observed between the click landing and the demo saying it is done,
/// plus the payload that said so.
/// </summary>
/// <remarks>
/// The samples are kept rather than reduced on the fly because the two assertions a suite makes need different things
/// from them. The peak is a maximum over the whole flight — a UI observer only ever sees the curve at grid points, and
/// the maximum of what it saw is the best evidence it can offer. The settle value is read from exactly one sample, the
/// first with <c>done=1</c>, and from no other, because the curves re-cross their target mid-flight.
/// </remarks>
internal sealed class ScenarioRun
{
    private readonly IReadOnlyList<StatePayload> _flight;

    internal ScenarioRun(ScenarioSpec spec, IReadOnlyList<StatePayload> flight, StatePayload settled)
    {
        Spec = spec;
        _flight = flight;
        Settled = settled;
    }

    /// <summary>The scenario that was run.</summary>
    internal ScenarioSpec Spec { get; }

    /// <summary>The payload that first reported <c>done=1</c> — the only one a settle assertion may use.</summary>
    internal StatePayload Settled { get; }

    /// <summary>Every payload read between the click landing and the demo reporting the end.</summary>
    internal IReadOnlyList<StatePayload> Flight => _flight;

    /// <summary>How many payloads were read during the flight.</summary>
    internal int SampleCount => _flight.Count;

    /// <summary>The largest value any observed payload reported for a field; NaN when no payload held a number there.</summary>
    internal double Max(string key)
    {
        var max = double.NaN;
        foreach (var payload in _flight)
        {
            if (!payload.Has(key)) continue;

            double value;
            try
            {
                value = payload.Number(key);
            }
            catch (FormatException)
            {
                // 非数值字段（例如渐变画刷的类型名）交给 MaxColor，不是这里的事。
                continue;
            }

            max = double.IsNaN(max) ? value : Math.Max(max, value);
        }

        return max;
    }

    /// <summary>
    /// The per-channel maximum of a field the demos write as <c>#rrggbb</c>. Per channel rather than by sample: every
    /// channel of a solid fill moves on one shared progress, so the per-channel maximum is that colour at its peak.
    /// </summary>
    internal RgbColor? MaxColor(string key)
    {
        RgbColor? max = null;
        foreach (var payload in _flight)
        {
            if (!payload.Has(key) || !RgbColor.TryParse(payload.Text(key), out var color)) continue;

            max = max is null
                ? color
                : new RgbColor(Math.Max(max.Value.R, color.R), Math.Max(max.Value.G, color.G), Math.Max(max.Value.B, color.B));
        }

        return max;
    }

    /// <summary>A one-line summary of the run for a failure message: how long it ran, what it started at, peaked at and settled on.</summary>
    internal string Summary(string key)
    {
        var first = _flight.Count > 0 ? _flight[0].Text(key) : "n/a";
        var max = Max(key);
        var peak = double.IsNaN(max) ? MaxColor(key)?.ToString() ?? "n/a" : max.ToString("F3");

        return $"{SampleCount} samples over {Settled.ElapsedMs:F0}ms: first {first}, max {peak}, settled {Settled.Text(key)}";
    }
}
