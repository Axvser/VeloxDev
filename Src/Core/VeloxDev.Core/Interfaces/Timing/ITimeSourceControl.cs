namespace VeloxDev.Timing;

/// <summary>
/// The control face of a time source: pausing, resuming, re-rating, seeking, and nudging a paused consumer.
/// </summary>
/// <remarks>
/// Separate from <see cref="ITimeSource"/> so that the read-only face is what a sampler and a passive consumer
/// receive. Control calls are safe from any thread and are serialised internally.
/// </remarks>
public interface ITimeSourceControl : ITimeSource
{
    /// <summary>Freezes the source. Time spent paused is excluded by construction — nothing accrues.</summary>
    void Pause();

    /// <summary>Lets the source run again, at the rate it was last set to.</summary>
    void Resume();

    /// <summary>
    /// Changes how fast the source runs, without moving its position. Zero freezes it without pausing it; a resume
    /// then does nothing, and only a non-zero rate starts it again.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is negative.</exception>
    void SetRate(double rate);

    /// <summary>Moves the source to <paramref name="position"/>, keeping the rate.</summary>
    /// <remarks>
    /// A consumer holding an accumulator must treat this as a discontinuity. While paused there is no frame left
    /// to pick the new position up, so this also nudges parked consumers to redraw.
    /// </remarks>
    void Seek(TimeSpan position);

    /// <summary>
    /// Wakes a parked consumer once without resuming. A no-op when the source is running.
    /// </summary>
    /// <remarks>
    /// Needed for two things that are not a resume: redrawing a position changed while paused, and letting a
    /// cancellation reach a consumer whose parked wait knows nothing about the cancellation token. An
    /// implementation must replace the parked signal rather than complete it in place — completing it and leaving
    /// it installed would let the next wait return immediately, spinning out frames.
    /// </remarks>
    void Wake();
}
