namespace VeloxDev.Timing;

/// <summary>
/// Shared state contract of the two sampling modes. Not used directly — pick
/// <see cref="IUncompensatedTimeSampler"/> or <see cref="ICompensatingTimeSampler"/>.
/// </summary>
/// <remarks>
/// A sampler holds per-consumer state and has exactly <b>one logical owner</b> — one loop, one sampler. It takes no
/// lock: the state is a few integers touched once per frame, and a lock there would cost more than the arithmetic
/// it guards. An implementation must not be shared between threads, and <see cref="Reset"/> is not an exception.
/// </remarks>
public interface ITimeSampler
{
    /// <summary>
    /// Re-anchors to the source's present and clears every accumulated quantity, so the next sample behaves like
    /// the first one after construction.
    /// </summary>
    /// <remarks>
    /// Called when a loop (re)starts, never while its sampling call is in flight. It does not touch the source.
    /// </remarks>
    void Reset();
}
