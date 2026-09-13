namespace VeloxDev.Timing;

/// <summary>
/// Fixed-step sampling that repays what it owes: over any interval the number of steps delivered equals
/// <c>floor(elapsed / <see cref="Step"/>)</c>, with no drift and no step silently lost. For consumers whose step
/// count must be exact — physics, fixed-rate integration.
/// </summary>
/// <remarks>
/// Differs from <see cref="IUncompensatedTimeSampler"/> in what it does with the two leftovers. A sub-step
/// remainder is carried to the next call, never rounded away; whole steps that
/// <see cref="MaxStepsPerCall"/> would not let through this call stay owed rather than being discarded, so the
/// count still comes out right and only the burst is spread.
/// <para>
/// The one bound is <see cref="MaxPendingSteps"/>: past it the debt is dropped and counted in
/// <see cref="DroppedSteps"/>. Without a bound the contract is unsatisfiable — a machine suspended for hours owes
/// millions of steps, and repaying them at a capped rate takes hours, during which the consumer is further behind
/// than if the debt had been forgiven. Every hitch a real frame loop sees is far inside the default bound; only
/// suspend, a debugger break or a fully stalled process reaches it, and those are counted rather than silent.
/// </para>
/// <para>
/// A rebase of the source (see <see cref="ITimeSource.Epoch"/>) discards the debt instead of repaying it: a
/// backward seek must never produce negative pushes, and a forward one must not inject a burst that never happened.
/// </para>
/// </remarks>
public interface ICompensatingTimeSampler : ITimeSampler
{
    /// <summary>
    /// The fixed interval each step covers. Also what <see cref="TimeSample.Delta"/> reports, because a fixed step
    /// is what the consumer integrates with — the measured interval is deliberately not exposed.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    TimeSpan Step { get; set; }

    /// <summary>
    /// The most steps a single <see cref="Advance"/> may deliver. The excess stays owed, so this bounds the
    /// catch-up burst without affecting the total.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    int MaxStepsPerCall { get; set; }

    /// <summary>
    /// How many steps may be owed at once before the debt is forgiven. Reaching it means the consumer cannot keep
    /// up with the source's rate at all, so repaying in full would leave it permanently behind; the excess is
    /// dropped and added to <see cref="DroppedSteps"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    int MaxPendingSteps { get; set; }

    /// <summary>
    /// Steps owed but not yet delivered. Reading it after an <see cref="Advance"/> is the only way to tell a cap
    /// truncation (non-zero) from the consumer simply having reached the source's present (zero).
    /// </summary>
    long PendingSteps { get; }

    /// <summary>
    /// Steps forgiven by <see cref="MaxPendingSteps"/>. Non-zero means the delivered step count and the source's
    /// elapsed time have diverged, by exactly this many steps.
    /// </summary>
    long DroppedSteps { get; }

    /// <summary>
    /// How long until the next step is owed — <see cref="TimeSpan.Zero"/> while steps already are.
    /// </summary>
    /// <remarks>
    /// What a loop waits out between pushes. Sleeping exactly to the boundary is the point of exposing it: the
    /// alternative is waking on a fraction of the step and re-asking, which is both late and a wake-up per poll.
    /// The sub-step remainder is what this reports the complement of, so it is the same quantity the accumulator
    /// carries rather than a second one computed beside it.
    /// </remarks>
    TimeSpan TimeToNextStep { get; }

    /// <summary>
    /// Reports how many steps the caller should push now.
    /// </summary>
    /// <returns>
    /// The step count, at most <see cref="MaxStepsPerCall"/>. Zero when less than one step is owed. The caller
    /// pushes that many times, each with the same fixed <see cref="TimeSample.Delta"/>.
    /// </returns>
    int Advance(out TimeSample sample);
}
