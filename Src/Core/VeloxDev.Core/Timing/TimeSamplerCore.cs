namespace VeloxDev.Timing;

/// <summary>
/// The read handle and the tick arithmetic the default samplers share.
/// </summary>
/// <remarks>
/// Deliberately holds no sampling state and declares no reset: a base constructor runs before the derived field
/// initializers, so a reset invoked from here would read fields the derived type has not initialised yet. Each
/// sampler primes itself from its own constructor instead.
/// </remarks>
public abstract class TimeSamplerCore
{
    protected TimeSamplerCore(ITimeSource source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>The source this sampler reads.</summary>
    protected ITimeSource Source { get; }

    /// <summary>The source's unit. Never <c>Stopwatch.Frequency</c>, which an injected source may not share.</summary>
    protected long TicksPerSecond => Source.TicksPerSecond;

    protected TimeSpan ToTimeSpan(long ticks) => TimeConversion.TicksToTimeSpan(ticks, TicksPerSecond);

    protected long ToTicks(TimeSpan span) => TimeConversion.SpanToTicks(span, TicksPerSecond);
}
