using System.Diagnostics;

namespace VeloxDev.Timing;

/// <summary>
/// Tick arithmetic in the unit a source publishes, so nothing outside a source reads a framework clock's frequency.
/// </summary>
/// <remarks>
/// Every member takes the unit rather than assuming one: an injected source may count in a browser's microseconds
/// or a host's composition ticks, and a conversion that quietly used <see cref="Stopwatch.Frequency"/> would be
/// wrong by orders of magnitude with no symptom. <see cref="DefaultTicksPerSecond"/> is the unit of the default
/// source, and the only place <see cref="Stopwatch"/> is named.
/// </remarks>
public static class TimeConversion
{
    /// <summary>The tick unit of the default source: <see cref="Stopwatch.Frequency"/>, which is not portable.</summary>
    public static long DefaultTicksPerSecond => Stopwatch.Frequency;

    public static TimeSpan TicksToTimeSpan(long ticks, long ticksPerSecond)
        => TimeSpan.FromTicks(TicksToSpanTicks(ticks, ticksPerSecond));

    public static long TicksToSpanTicks(long ticks, long ticksPerSecond)
        => (long)(ticks * (double)TimeSpan.TicksPerSecond / ticksPerSecond);

    public static long SpanToTicks(TimeSpan span, long ticksPerSecond)
        => (long)(span.Ticks * (double)ticksPerSecond / TimeSpan.TicksPerSecond);

    public static double TicksToMilliseconds(long ticks, long ticksPerSecond)
        => ticks * 1000.0 / ticksPerSecond;

    public static double TicksToSeconds(long ticks, long ticksPerSecond)
        => ticks / (double)ticksPerSecond;

    public static long MillisecondsToTicks(double milliseconds, long ticksPerSecond)
        => (long)(milliseconds * ticksPerSecond / 1000.0);
}

/// <summary>
/// The default time source: an absolute virtual timeline — where it is, how fast it is moving, and the gate that
/// parks its consumers while it is paused.
/// </summary>
/// <remarks>
/// Also the base a host that owns time derives from. Two seams, one per shape of host: a <em>pull</em> host answers
/// "what time is it now" and supplies the stamp through the protected constructor, while a <em>push</em> host whose
/// position arrives in a callback returns the last value it was given and reports its feed through
/// <see cref="SetHostFeeding"/>. Everything either of them would otherwise have to re-derive — the anchor
/// arithmetic, the epoch protocol, the overflow guard, the park signal — stays here.
/// <para>
/// Time is raw integer ticks in the source's own unit — not milliseconds as a <see cref="double"/>. Precision is
/// not the reason (a double holds exact integer ticks for decades); the reason is that the whole clock state can
/// then be a set of <see cref="long"/> fields, which are individually atomic on 32-bit as well as 64-bit and cost
/// nothing to swap. The unit is the machine clock's by default; a host counting elsewhere declares its own, because
/// every conversion a consumer makes divides by it.
/// </para>
/// <para>
/// Absolute, never restarted — which is what lets several consumers be anchored to one instance and share a
/// transport. A per-consumer pass is expressed as an <em>anchor</em> into this timeline, not by resetting it, so
/// starting a pass on one consumer cannot move any other.
/// </para>
/// <para>
/// Reading the position is lock-free: the coherent fields are published under a sequence counter, and because the
/// only writers are control calls — pause, resume, rate, seek, and a host's feed report — a reader effectively never
/// retries. Writing takes the write gate, which no reader ever touches.
/// </para>
/// </remarks>
public class TimeSourceCore : ITimeSourceControl
{
    /// <summary>
    /// Playback rates are held as integers scaled by this, so the clock's state is integral throughout. Four
    /// decimal places is far finer than any animation needs, and it leaves the multiply in <see cref="Now"/> an
    /// overflow margin of years even at a high rate.
    /// </summary>
    private const long Scale = 10_000L;

    private const long OneRate = Scale;

    /// <summary>Serialises writers, so the sequence-counter protocol below cannot be corrupted by two of them.</summary>
    private readonly object _writeGate = new();

    private readonly long _origin;

    /// <summary>Even when stable, odd while a writer is halfway through publishing.</summary>
    private long _version;

    /// <summary>
    /// Bumped once per rebase. The sequence counter above is bumped twice per write to bracket it, so it cannot
    /// serve as an identity a consumer can compare — this one can.
    /// </summary>
    private long _epoch;

    private long _anchorReal;
    private long _anchorVirtual;
    private long _speed;
    private long _rate;

    /// <summary>True while <see cref="Pause"/> was called and no <see cref="Resume"/> has followed.</summary>
    private bool _paused;

    /// <summary>
    /// True while whoever owns the clock behind this source is delivering. True by default, and left alone by every
    /// control call: a source that computes its position from the machine clock is being fed by definition, and only
    /// a pushed host — one whose position arrives in a callback — ever clears it.
    /// </summary>
    /// <remarks>
    /// It exists because <see cref="IsAdvancing"/> cannot be derived from <see cref="IsPaused"/> and
    /// <see cref="Rate"/> alone. A host that stops feeding freezes the position without pausing anything, and a
    /// source that reported advancing through that would never let its consumers park — the failure
    /// <see cref="ITimeSource.IsAdvancing"/> describes, and the reason the predicate is stated as an invariant rather
    /// than a formula.
    /// </remarks>
    private bool _hostFeeding = true;

    /// <summary>
    /// Non-null exactly while the clock is <em>not advancing</em> — that is, while <see cref="IsAdvancing"/> is
    /// false, whether that is because of a pause or a rate of zero. Bound to the predicate a consumer parks on
    /// rather than to the pause, so the two can never disagree and leave a consumer spinning on
    /// <c>while (!IsAdvancing)</c>: an installed gate means a wait parks, and a cleared one means it does not.
    /// <para>
    /// It is also the wake mechanism: leaving the stalled state clears it and completes it, and a control call that
    /// has to be seen while stalled replaces it and completes the old one, so a parked consumer wakes once per
    /// change and re-reads the state instead of polling.
    /// </para>
    /// </summary>
    private TaskCompletionSource<bool>? _parkGate;

    /// <summary>
    /// The machine clock, and the stamp of the public constructor. A cached delegate rather than a method group, so
    /// the default path allocates nothing beyond the source itself.
    /// </summary>
    private static readonly Func<long> MachineStamp = static () => Stopwatch.GetTimestamp();

    /// <summary>
    /// Where the position comes from.
    /// </summary>
    /// <remarks>
    /// A delegate rather than a virtual method, deliberately. The stamp is read once from the constructor, and a
    /// virtual would run the override before the subclass's own constructor had assigned anything it might read —
    /// the classic construction-order trap, here with a clock for a symptom.
    /// </remarks>
    private readonly Func<long> _nowStamp;

    public TimeSourceCore() : this(MachineStamp, TimeConversion.DefaultTicksPerSecond)
    {
    }

    /// <summary>
    /// A source whose position comes from somewhere other than the machine clock: the seam a host that owns time
    /// uses, instead of reimplementing the timeline.
    /// </summary>
    /// <param name="nowStamp">
    /// Reads the host's clock, in <paramref name="ticksPerSecond"/> units. Everything else is inherited unchanged —
    /// the anchor arithmetic, the epoch protocol, the overflow guard in <see cref="Advance"/> and the park signal all
    /// stay here, and only "what time is it" moves to the host.
    /// <para>
    /// A host that <em>pushes</em> the position from a callback rather than answering with the current time returns
    /// the last value it was given, and calls <see cref="SetHostFeeding"/> as its feed starts and stops. Supplying
    /// this without that is the one combination that breaks: a frozen stamp behind an <see cref="IsAdvancing"/> that
    /// still reads true.
    /// </para>
    /// </param>
    /// <param name="ticksPerSecond">
    /// The unit <paramref name="nowStamp"/> counts in, and what <see cref="TicksPerSecond"/> reports. Defaults to
    /// <see cref="Stopwatch.Frequency"/> for the machine clock, which is not portable — a host counting elsewhere
    /// must say so, or every conversion a consumer makes is quietly wrong by the ratio between the two.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="nowStamp"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksPerSecond"/> is not positive.</exception>
    protected TimeSourceCore(Func<long> nowStamp, long ticksPerSecond)
    {
        // 不用 ThrowIfNull / ThrowIfLessThan：Core 多目标到 netstandard2.0 与 netframework4.6.1，那两个助手在
        // 这些目标上不存在。
        if (nowStamp is null) throw new ArgumentNullException(nameof(nowStamp));
        if (ticksPerSecond < 1L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticksPerSecond), ticksPerSecond, "A tick unit is the divisor of every conversion and must be positive.");
        }

        _nowStamp = nowStamp;
        TicksPerSecond = ticksPerSecond;

        var stamp = nowStamp();
        _origin = stamp;
        _anchorReal = stamp;
        _anchorVirtual = stamp;
        _speed = OneRate;
        _rate = OneRate;
    }

    /// <inheritdoc />
    public long TicksPerSecond { get; }

    /// <inheritdoc />
    public long Ticks => Now;

    /// <summary>The timeline's current position, in virtual time since it started.</summary>
    public TimeSpan Position => TimeConversion.TicksToTimeSpan(Now - _origin, TicksPerSecond);

    /// <summary>True while the timeline has been paused and not resumed.</summary>
    public bool IsPaused => Volatile.Read(ref _paused);

    /// <summary>
    /// True while the position is moving. A rate of zero freezes the clock without pausing it, and a host that has
    /// stopped feeding freezes it without doing anything at all, so this — not <see cref="IsPaused"/> — is what a
    /// consumer parks on.
    /// </summary>
    public bool IsAdvancing => Advances;

    /// <summary>
    /// The one place the three flags are combined, so the property and the park signal cannot disagree.
    /// </summary>
    private bool Advances
        => Volatile.Read(ref _hostFeeding) && !Volatile.Read(ref _paused) && Volatile.Read(ref _rate) > 0L;

    /// <inheritdoc />
    public long Epoch => Volatile.Read(ref _epoch);

    /// <summary>
    /// How fast the timeline runs. Never negative; zero freezes it without pausing it, and a non-zero rate starts
    /// it again.
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
        TaskCompletionSource<bool>? woken;
        lock (_writeGate)
        {
            if (_paused) return;

            _paused = true;
            Rebase(speed: 0L, rate: null, virtualTarget: null);

            // 这里只会装上一个 gate、不会清掉任何 gate（暂停只会让时钟停下），所以 woken 恒为 null。
            // 仍然照统一形式取回来，是为了让「谁清掉 gate 谁负责 complete」这条规矩在四个控制调用上一致，
            // 将来谁改了 Pause 的语义也不会悄悄漏掉一次唤醒。
            woken = RefreshParkGate();
        }

        woken?.TrySetResult(true);
    }

    /// <summary>
    /// Lets the timeline run again, at the rate it was last set to.
    /// </summary>
    /// <remarks>
    /// After a rate of zero this deliberately does not make the clock advance: the pause is lifted but the rate is
    /// still zero, so <see cref="IsPaused"/> becomes false while <see cref="IsAdvancing"/> stays false and
    /// <see cref="WaitWhileStalledAsync"/> keeps waiting. Only a non-zero rate starts playback. Conflating the two
    /// would be the easy mistake — and it is what lets <c>SetTimeScale(0)</c> freeze a frame loop instead of
    /// leaving it spinning at its sampling interval.
    /// </remarks>
    public void Resume()
    {
        TaskCompletionSource<bool>? woken;
        lock (_writeGate)
        {
            if (!_paused) return;

            _paused = false;
            Rebase(speed: _rate, rate: null, virtualTarget: null);
            woken = RefreshParkGate();
        }

        // Outside the write gate: completing a gate runs its continuations, and a continuation that re-entered a
        // control call would deadlock on the non-reentrant lock.
        woken?.TrySetResult(true);
    }

    /// <summary>
    /// Changes how fast the timeline runs, without moving its position. Zero freezes it without pausing it.
    /// </summary>
    /// <remarks>
    /// Time only ever moves forwards. There is no reverse playback, and a negative rate is a caller mistake rather
    /// than something to clamp: clamping it to a pause would leave an animation silently not running, and clamping
    /// it to a forward speed would ignore what was asked for.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is negative.</exception>
    public void SetRate(double rate)
    {
        if (rate < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "A transition timeline does not run backwards.");
        }

        var scaled = (long)(rate * Scale);

        TaskCompletionSource<bool>? woken;
        lock (_writeGate)
        {
            // A rate given while paused is remembered but does not start playback — only Resume does that.
            Rebase(speed: _paused ? null : scaled, rate: scaled, virtualTarget: null);

            // 从 rate 0 恢复成非 0 会清掉 park 信号：冻结的时钟重新走起来了，停在上面的消费者必须被放行。
            woken = RefreshParkGate();
        }

        woken?.TrySetResult(true);
    }

    /// <summary>Moves the timeline to <paramref name="position"/>, keeping the transport.</summary>
    public void Seek(TimeSpan position)
    {
        var target = _origin + TimeConversion.SpanToTicks(position, TicksPerSecond);
        TaskCompletionSource<bool>? woken;
        lock (_writeGate)
        {
            Rebase(speed: null, rate: null, virtualTarget: target);
            woken = RefreshParkGate(); // Seek 不改停摆状态，所以这里实际总是 null；形式统一是为了不漏唤醒
        }

        woken?.TrySetResult(true);

        // While paused there is no frame left to pick the new position up, so nudge: a parked consumer wakes, draws
        // the position it was just moved to, and parks again. Without this a seek while paused would be invisible
        // until playback was resumed.
        Wake();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The gate is <em>replaced</em> rather than completed in place: completing it and leaving it installed would
    /// let a parked consumer's next wait return immediately, over and over, spinning out frames.
    /// </remarks>
    public void Wake()
    {
        TaskCompletionSource<bool>? gate;
        lock (_writeGate)
        {
            gate = _parkGate;
            if (gate is null) return; // advancing: the next frame picks the change up on its own
            _parkGate = NewGate();
        }

        gate.TrySetResult(true);
    }

    /// <summary>
    /// Reports that whoever feeds this source has stopped or resumed delivering.
    /// </summary>
    /// <remarks>
    /// The seam a pushed host uses. Its feed falling silent freezes the position without anyone calling
    /// <see cref="Pause"/>, and <see cref="IsAdvancing"/> has to say so or every loop anchored here goes on sampling
    /// a frame that is not changing — see <see cref="ITimeSource.IsAdvancing"/> for why that failure is worth a
    /// method of its own.
    /// <para>
    /// A method here rather than an <c>IsAdvancing</c> a host overrides, for the reason
    /// <see cref="TransitionSystem.FramePacerCore"/> gives about its own base: the predicate and the park signal
    /// have to move together, the host cannot see whether they did, and getting it wrong is invisible from the
    /// host's side. Routing the change through the one place that already maintains that pairing is cheaper than
    /// asking every host to re-derive it.
    /// </para>
    /// <para>
    /// Not a rebase: the position picks up exactly where it stopped, so an accumulator's basis stays valid and
    /// <see cref="Epoch"/> does not move. A host whose own clock keeps running while its feed is silent is
    /// describing a jump rather than a stall, and says so with <see cref="Seek"/> instead.
    /// </para>
    /// <para>
    /// Idempotent, and callable from any thread — including the host's own, which is usually not the UI thread.
    /// </para>
    /// </remarks>
    protected void SetHostFeeding(bool feeding)
    {
        TaskCompletionSource<bool>? woken;
        lock (_writeGate)
        {
            if (_hostFeeding == feeding) return;

            _hostFeeding = feeding;
            woken = RefreshParkGate();
        }

        // 与 Resume 同样的理由放在写闸之外：完成一个 gate 会同步跑它的续体，而续体若重入控制调用，
        // 会死在不可重入的锁上。
        woken?.TrySetResult(true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The cancellation arm exists because a parked consumer's wait knows nothing about its own token: without it
    /// a stop issued while stalled would leave the consumer waiting for a resume that is never coming. The
    /// registration is created only when the clock is actually stalled, so an advancing consumer pays nothing.
    /// </remarks>
    public Task WaitWhileStalledAsync(CancellationToken cancellationToken = default)
    {
        var gate = Volatile.Read(ref _parkGate);
        if (gate is null) return Task.CompletedTask;

        return cancellationToken.CanBeCanceled
            ? WaitOnGateAsync(gate.Task, cancellationToken)
            : gate.Task;
    }

    /// <summary>
    /// Reconciles the park signal with <see cref="IsAdvancing"/>, returning the gate it cleared — the one a caller
    /// must complete — or null when it cleared nothing.
    /// </summary>
    /// <remarks>
    /// Called under the write gate from every control call, so the invariant lives in exactly one place: a gate is
    /// installed while the clock is stalled and cleared while it advances. It never completes a gate it leaves
    /// installed, which would hand the next wait an already-finished task and let the consumer spin; and it
    /// completes the cleared one outside the gate, for the same reentrancy reason as <see cref="Resume"/>.
    /// </remarks>
    private TaskCompletionSource<bool>? RefreshParkGate()
    {
        if (Advances)
        {
            var gate = _parkGate;
            _parkGate = null;
            return gate;
        }

        // 停摆时必须有一个未完成的 gate 装着：否则 while (!IsAdvancing) await WaitWhileStalledAsync() 会立刻返回、
        // 立刻重进，把一个核心热转掉。这就是 park 信号必须绑定 IsAdvancing 而不是绑定「暂停」的原因。
        if (_parkGate is null) _parkGate = NewGate();
        return null;
    }

    /// <summary>The timeline's raw position. Absolute virtual ticks, for anchors and samplers.</summary>
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

                return Advance(virtualTicks, _nowStamp() - anchorReal, speed);
            }
        }
    }

    private static async Task WaitOnGateAsync(Task gate, CancellationToken cancellationToken)
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
        {
            var winner = await Task.WhenAny(gate, cancelled.Task).ConfigureAwait(false);
            // Thrown rather than returned: a consumer that looped on a normal return would spin forever once
            // cancelled, since nothing is going to resume the source on its behalf.
            if (!ReferenceEquals(winner, gate)) cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static TaskCompletionSource<bool> NewGate()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Moves a virtual position on by <paramref name="elapsedTicks"/> of real time at <paramref name="speed"/>.
    /// </summary>
    /// <remarks>
    /// The whole part is taken out before anything is multiplied, and that ordering is the entire point of the
    /// method. The obvious <c>elapsed * speed / Scale</c> overflows at <c>long.MaxValue / speed</c>: with a speed of
    /// 10 000 that is about 2.9 years of real time on Windows, but 10.7 <em>days</em> on Linux, where a Stopwatch
    /// tick is a nanosecond. C# arithmetic is unchecked, so the failure is not an exception — it is a silent wrap to
    /// a negative position, which leaves a consumer that was never paused and never seeked jammed for good.
    /// Dividing first keeps every intermediate at the size of the answer, and the answer is the only thing that has
    /// to fit.
    /// <para>
    /// A rebase — pause, resume, rate change, seek — is what resets the elapsed interval, so this only bounds a
    /// consumer left running untouched for that long. It is a loop that never ends, not a long transition, that
    /// gets there.
    /// </para>
    /// <para>
    /// Split from the one-line form rather than merely written longer, and measured so nobody "optimises" it back:
    /// BenchmarkDotNet puts the multiply-first expression at 0.23 ns and this at 0.61 ns, so the safety costs about
    /// 0.38 ns — and this runs once per animation per frame, which is 2.3 µs per second for a hundred animations
    /// running at 60 FPS. The division is not even a division: the divisor is a constant, so the JIT
    /// strength-reduces it, and the two operations share it.
    /// </para>
    /// </remarks>
    internal static long Advance(long position, long elapsedTicks, long speed)
    {
        // 单调时钟不会倒走；真出现负值时保持不动，也好过把一个往回跳的位置交给采样循环。
        if (elapsedTicks <= 0L) return position;

        var whole = elapsedTicks / Scale;
        var rest = elapsedTicks % Scale;

        return position + whole * speed + rest * speed / Scale;
    }

    private void Rebase(long? speed, long? rate, long? virtualTarget)
    {
        var realNow = _nowStamp();

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
            position = Advance(virtualTicks, realNow - anchorReal, current);
        }

        Interlocked.Increment(ref _version); // odd: readers retry
        Volatile.Write(ref _anchorVirtual, position);
        Volatile.Write(ref _anchorReal, realNow);
        if (speed is not null) Volatile.Write(ref _speed, speed.Value);
        if (rate is not null) Volatile.Write(ref _rate, rate.Value);

        // Inside the brackets, and under the write gate: a consumer must never observe a new epoch alongside the
        // fields of the previous one.
        Interlocked.Increment(ref _epoch);
        Interlocked.Increment(ref _version); // even: stable
    }
}
