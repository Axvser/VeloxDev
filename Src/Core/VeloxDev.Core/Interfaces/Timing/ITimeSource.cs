namespace VeloxDev.Timing;

/// <summary>
/// The one time source a consumer reads: where it is, how fast it is moving, and a way to wait for it to move again.
/// </summary>
/// <remarks>
/// Absolute, in the source's own tick unit, and shared — several consumers can be anchored to one instance and
/// share a transport, which is what lets a single pause stop a frame loop and the animations running over it.
/// <para>
/// This is the read-only face: an <see cref="ITimeSampler"/> takes this and cannot pause the world. Control lives
/// on <see cref="ITimeSourceControl"/>.
/// </para>
/// <para>
/// Implementations must allow readers on any thread and must be safe to read while a writer is mid-update.
/// </para>
/// </remarks>
public interface ITimeSource
{
    /// <summary>
    /// The tick unit of <see cref="Ticks"/>. A consumer converts with this and never reads a framework clock's
    /// frequency directly: an injected source may not be <c>Stopwatch</c>-based (a browser's <c>Performance.now()</c>,
    /// a host's composition clock), and the conversion would then be silently wrong.
    /// </summary>
    long TicksPerSecond { get; }

    /// <summary>
    /// The absolute position, in <see cref="TicksPerSecond"/> units. This is the value for anchor arithmetic;
    /// differences between two readings are meaningful, the magnitude itself is not.
    /// </summary>
    long Ticks { get; }

    /// <summary>The position relative to this source's origin, as a convenience view of <see cref="Ticks"/>.</summary>
    TimeSpan Position { get; }

    /// <summary>How fast the source runs. Never negative; zero means it is not advancing.</summary>
    double Rate { get; }

    /// <summary>True while the source has been paused with <see cref="ITimeSourceControl.Pause"/>.</summary>
    bool IsPaused { get; }

    /// <summary>
    /// True while the position is moving.
    /// </summary>
    /// <remarks>
    /// This, and not <see cref="IsPaused"/>, is the predicate a consumer parks on. The two differ because a rate of
    /// zero freezes the source without pausing it: a loop that checked only <see cref="IsPaused"/> would keep
    /// running with a frozen clock, and a loop that checked <see cref="IsAdvancing"/> but parked on the wrong signal
    /// would spin.
    /// <para>
    /// An invariant, not a formula over <see cref="IsPaused"/> and <see cref="Rate"/>: <b>true implies the position
    /// is moving</b>, whatever the reason it might not be. A source whose position is advanced by a host rather than
    /// computed from a clock at read time must report false whenever that host has stopped feeding it — a player
    /// loop suspended, a decoder buffer empty, a tab in the background — even though nobody called
    /// <see cref="ITimeSourceControl.Pause"/>. The two agree on the default source only because a
    /// <see cref="System.Diagnostics.Stopwatch"/> never stops, and that is a property of that source rather than of
    /// this contract.
    /// </para>
    /// <para>
    /// Getting it wrong is silent, and is the one mistake this property exists to make impossible. A source that
    /// reports true while frozen never lets its consumers park, so every loop anchored to it goes on writing
    /// properties at its sampling rate for a frame that is not changing: no exception, no log, no frame — just work
    /// that never stops.
    /// </para>
    /// </remarks>
    bool IsAdvancing { get; }

    /// <summary>
    /// Increases by one every time the source is rebased — paused, resumed, reseeked or re-rated. A consumer
    /// holding accumulated state compares this to detect that its basis is no longer valid.
    /// </summary>
    long Epoch { get; }

    /// <summary>
    /// Completes when the source stops being stalled, or immediately when it is already advancing.
    /// </summary>
    /// <remarks>
    /// The awaitable form of <see cref="IsAdvancing"/> — "stalled" means exactly <c>!IsAdvancing</c>, so a pause and
    /// a rate of zero both park here. That pairing is the whole point: <c>while (!IsAdvancing) await
    /// WaitWhileStalledAsync()</c> must not return while the predicate is still false, or the consumer spins a core
    /// hot. A paused or frozen consumer therefore costs no wake-ups at all rather than polling.
    /// <para>
    /// It also returns on a nudge — a seek applied while stalled — so the new position can be drawn. Callers
    /// re-read the state after every wake instead of treating completion as "resumed".
    /// </para>
    /// <para>
    /// A source whose position a host advances owes this signal to that host rather than to a control call. The
    /// stall that has to be reported is the host falling silent, and no <see cref="ITimeSourceControl"/> method is
    /// called when that happens — nothing would install the signal, a parked consumer would be released the instant
    /// it parked, and the loop would spin. Such an implementation installs the signal when its feed stops and
    /// releases it when the feed resumes, by the same rule as every other implementation: installed exactly while
    /// <see cref="IsAdvancing"/> is false, whichever side made it false.
    /// </para>
    /// <para>
    /// Implementations must not take a write gate here, must complete continuations asynchronously rather than
    /// inline from the control call, must return an already-completed task when advancing, and must observe
    /// <paramref name="cancellationToken"/> — a consumer's stop has to reach it even though nothing is going to
    /// resume the source on its behalf.
    /// </para>
    /// </remarks>
    Task WaitWhileStalledAsync(CancellationToken cancellationToken = default);
}
