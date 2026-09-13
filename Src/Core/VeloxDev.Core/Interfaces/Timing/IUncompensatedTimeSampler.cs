namespace VeloxDev.Timing;

/// <summary>
/// Best-effort sampling: how much time has passed since the previous sample, with no guarantee about how many
/// samples should have happened. Late is merely late.
/// </summary>
/// <remarks>
/// The right mode whenever the consumer positions itself against the source rather than accumulating — an animation
/// reads the absolute position and is correct whatever the sampling cadence. Nothing is carried between calls, so a
/// missed sample loses nothing.
/// </remarks>
public interface IUncompensatedTimeSampler : ITimeSampler
{
    /// <summary>
    /// Reads the source and reports the interval since the previous sample.
    /// </summary>
    /// <returns>
    /// A sample whose <see cref="TimeSample.Delta"/> is <see cref="System.TimeSpan.Zero"/> when the source has not
    /// advanced since the last call, which is the caller's signal not to push a frame.
    /// </returns>
    TimeSample Sample();
}
