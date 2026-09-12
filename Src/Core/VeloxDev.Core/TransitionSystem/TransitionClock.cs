using System.Diagnostics;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>
/// The one time source the whole transition system reads.
/// </summary>
/// <remarks>
/// Time is raw <see cref="Stopwatch"/> ticks — an integer, not milliseconds as a <see cref="double"/>. Precision is
/// not the reason (a double holds exact integer ticks for decades); the reason is that the whole clock state can
/// then be a set of <see cref="long"/> fields, which are individually atomic on 32-bit as well as 64-bit and cost
/// nothing to swap.
/// <para>
/// A control call runs on whatever thread the caller is on, so it and the sampling loop have to read the same
/// clock.
/// </para>
/// </remarks>
internal static class TransitionTime
{
    internal static long Now => Stopwatch.GetTimestamp();

    internal static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    internal static long MsToTicks(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);
}

/// <summary>
/// An absolute virtual timeline: where it is, how fast it is moving, and the gate that parks the sampling loops
/// while it is paused.
/// </summary>
/// <remarks>
/// Absolute, never restarted — which is what lets several animations be anchored to one instance and share a
/// transport. A per-animation pass is expressed as an <em>anchor</em> into this timeline (see
/// <see cref="TransitionRun.PassAnchor"/>), not by resetting it, so starting a pass on one animation cannot move
/// any other.
/// <para>
/// Reading the position is lock-free: the coherent fields are published under a sequence counter, and because the
/// only writers are control calls — pause, resume, rate, seek — a reader effectively never retries. Writing takes
/// the write gate, which no reader ever touches.
/// </para>
/// </remarks>
public sealed class TransitionTimeline
{
    /// <summary>
    /// Playback rates are held as integers scaled by this, so the clock's state is integral throughout. Four
    /// decimal places is far finer than any animation needs, and it leaves the multiply in
    /// <see cref="Now"/> an overflow margin of years even at a high rate.
    /// </summary>
    private const long Scale = 10_000L;

    private const long OneRate = Scale;

    /// <summary>Serialises writers, so the sequence-counter protocol below cannot be corrupted by two of them.</summary>
    private readonly object _writeGate = new();

    private readonly long _origin;

    /// <summary>Even when stable, odd while a writer is halfway through publishing.</summary>
    private long _version;

    private long _anchorReal;
    private long _anchorVirtual;
    private long _speed;
    private long _rate;

    /// <summary>
    /// Non-null exactly while paused. It is also the wake mechanism: a resume clears it and completes it, and a
    /// control call that has to be seen while paused replaces it and completes the old one, so a parked loop wakes
    /// once per change and re-reads the state instead of polling.
    /// </summary>
    private TaskCompletionSource<bool>? _pauseGate;

    public TransitionTimeline()
    {
        var now = TransitionTime.Now;
        _origin = now;
        _anchorReal = now;
        _anchorVirtual = now;
        _speed = OneRate;
        _rate = OneRate;
    }

    /// <summary>The timeline's current position, in virtual time since it started.</summary>
    public TimeSpan Position => TimeSpan.FromMilliseconds(TransitionTime.TicksToMs(Now - _origin));

    /// <summary>True while the timeline is paused.</summary>
    public bool IsPaused => Volatile.Read(ref _pauseGate) is not null;

    /// <summary>
    /// How fast the timeline runs. Never negative; zero means paused, and <see cref="Resume"/> turns it back on.
    /// </summary>
    public double Rate
    {
        get => Volatile.Read(ref _rate) / (double)OneRate;
        set => SetRate(value);
    }

    /// <summary>Freezes the timeline. The time spent paused is excluded by construction — nothing accrues.</summary>
    /// <remarks>Idempotent: pausing an already paused timeline changes nothing and wakes nobody.</remarks>
    public void Pause()
    {
        lock (_writeGate)
        {
            if (_pauseGate is not null) return;

            _pauseGate = NewGate();
            Rebase(speed: 0L, rate: null, virtualTarget: null);
        }
    }

    /// <summary>Lets the timeline run again, at the rate it was last set to.</summary>
    public void Resume()
    {
        TaskCompletionSource<bool>? gate;
        lock (_writeGate)
        {
            gate = _pauseGate;
            if (gate is null) return;

            _pauseGate = null;
            Rebase(speed: _rate, rate: null, virtualTarget: null);
        }

        // Outside the write gate: completing a gate runs its continuations, and a continuation that re-entered a
        // control call would deadlock on the non-reentrant lock.
        gate.TrySetResult(true);
    }

    /// <summary>
    /// Changes how fast the timeline runs, without moving its position. Zero pauses.
    /// </summary>
    /// <remarks>
    /// Time only ever moves forwards. There is no reverse playback, and a negative rate is a caller mistake rather
    /// than something to clamp: clamping it to a pause would leave an animation silently not running, and clamping it
    /// to a forward speed would ignore what was asked for.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is negative.</exception>
    public void SetRate(double rate)
    {
        if (rate < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "A transition timeline does not run backwards.");
        }

        var scaled = (long)(rate * Scale);

        lock (_writeGate)
        {
            // A rate given while paused is remembered but does not start playback — only Resume does that.
            Rebase(speed: _pauseGate is null ? scaled : null, rate: scaled, virtualTarget: null);
        }

        Wake();
    }

    /// <summary>Moves the timeline to <paramref name="position"/>, keeping the transport.</summary>
    public void Seek(TimeSpan position)
    {
        var target = _origin + TransitionTime.MsToTicks(position.TotalMilliseconds);
        lock (_writeGate)
        {
            Rebase(speed: null, rate: null, virtualTarget: target);
        }

        // While paused there is no frame left to pick the new position up, so nudge: a parked loop wakes, draws the
        // position it was just moved to, and parks again. Without this a seek while paused would be invisible until
        // the animation was resumed.
        Wake();
    }

    /// <summary>The timeline's raw position. Absolute virtual ticks, for the loop and the per-run anchors.</summary>
    internal long Now
    {
        get
        {
            var spin = new SpinWait();
            while (true)
            {
                var before = Volatile.Read(ref _version);
                if ((before & 1) != 0)
                {
                    spin.SpinOnce(); // a writer is halfway through publishing
                    continue;
                }

                var virtualTicks = Volatile.Read(ref _anchorVirtual);
                var anchorReal = Volatile.Read(ref _anchorReal);
                var speed = Volatile.Read(ref _speed);

                if (Volatile.Read(ref _version) != before)
                {
                    spin.SpinOnce(); // torn: the fields came from either side of a write
                    continue;
                }

                return virtualTicks + (TransitionTime.Now - anchorReal) * speed / Scale;
            }
        }
    }

    /// <summary>The pause gate, or null when running.</summary>
    internal TaskCompletionSource<bool>? PauseGate => Volatile.Read(ref _pauseGate);

    /// <summary>
    /// Wakes a parked loop once, without resuming. A no-op when the timeline is running.
    /// </summary>
    /// <remarks>
    /// Called for a position changed while paused — so the new one is drawn — and by a cancelled animation, whose
    /// parked loop has to wake to notice its token before it can stop.
    /// <para>
    /// The gate is <em>replaced</em> rather than completed in place: completing it and leaving it installed would
    /// let the loop's next wait return immediately, over and over, spinning out frames.
    /// </para>
    /// </remarks>
    internal void Wake()
    {
        TaskCompletionSource<bool>? gate;
        lock (_writeGate)
        {
            gate = _pauseGate;
            if (gate is null) return; // running: the next frame picks the change up on its own
            _pauseGate = NewGate();
        }

        gate.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> NewGate()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Rebase(long? speed, long? rate, long? virtualTarget)
    {
        var realNow = TransitionTime.Now;

        // The new position before the rebase is where the timeline is at this instant. Holding the write gate means
        // no other writer can be between its two version bumps, so the sequence protocol below holds.
        long position;
        if (virtualTarget is not null)
        {
            position = virtualTarget.Value;
        }
        else
        {
            var virtualTicks = Volatile.Read(ref _anchorVirtual);
            var anchorReal = Volatile.Read(ref _anchorReal);
            var current = Volatile.Read(ref _speed);
            position = virtualTicks + (realNow - anchorReal) * current / Scale;
        }

        Interlocked.Increment(ref _version); // odd: readers retry
        Volatile.Write(ref _anchorVirtual, position);
        Volatile.Write(ref _anchorReal, realNow);
        if (speed is not null) Volatile.Write(ref _speed, speed.Value);
        if (rate is not null) Volatile.Write(ref _rate, rate.Value);
        Interlocked.Increment(ref _version); // even: stable
    }
}

/// <summary>
/// One running animation: the token that ends it, the timeline it is anchored to, and where in that timeline the
/// pass it is currently playing started.
/// </summary>
/// <remarks>
/// Registered on the target's scheduler for the whole animation — across segments too, not only while a sampling
/// loop is running — so an <see cref="TransitionCore.Exit{T}"/> or a control call reaches it even while it sits in
/// the gap between two segments.
/// <para>
/// Both the anchor and the pass counter are single <see cref="long"/> fields: the loop advances the counter and
/// replaces the anchor, a seek moves them, and neither ever needs a coherent view of the other, so one atomic
/// operation each is enough.
/// </para>
/// </remarks>
internal sealed class TransitionRun
{
    private long _passAnchor;
    private long _cycle;

    internal TransitionRun(TransitionTimeline timeline)
    {
        Cts = new CancellationTokenSource();
        Timeline = timeline;
        _passAnchor = timeline.Now;
    }

    internal CancellationTokenSource Cts { get; }

    internal TransitionTimeline Timeline { get; }

    /// <summary>
    /// Where in the timeline the running pass starts. A seek replaces it; the loop only reads it.
    /// </summary>
    internal long PassAnchor
    {
        get => Interlocked.Read(ref _passAnchor);
        set => Interlocked.Exchange(ref _passAnchor, value);
    }

    /// <summary>
    /// Which pass is playing.
    /// </summary>
    /// <remarks>
    /// An integer, and necessarily so: a pass with a zero duration consumes no time at all, so an absolute timeline
    /// cannot tell it apart from its neighbour. This counter is the only thing that can, which is why the loop reads
    /// it instead of holding it privately — a seek has to be able to move it.
    /// </remarks>
    internal long Cycle
    {
        get => Interlocked.Read(ref _cycle);
        set => Interlocked.Exchange(ref _cycle, value);
    }

    internal void NextCycle() => Interlocked.Increment(ref _cycle);
}
