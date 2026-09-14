using System.Collections.Concurrent;
using System.Threading;
using VeloxDev.TimeLine;

namespace Demo;

/// <summary>
/// The constants the two pumps and the window have to agree on.
/// </summary>
internal static class DemoChannel
{
    /// <summary>
    /// The channel the window registers on. One channel, started once.
    /// </summary>
    /// <remarks>
    /// The previous version of this demo drove three components that registered on the default channel and then
    /// called <c>SetTargetFPS(30, "game")</c> — a second channel nothing ever started, so the call had no reader
    /// and the frame rate it configured never existed. A channel is named here, started here, and read back on
    /// screen; that is what makes the numbers below mean what they say.
    /// </remarks>
    public const string Name = MonoBehaviourManager.DEFAULT_CHANNEL;

    /// <summary>Gravity the balls integrate under, m/s². Chosen so one fall takes about 1.6 s.</summary>
    public const double Gravity = 1.2;

    /// <summary>The line the balls fall to, in metres.</summary>
    public const double FallHeight = 1.5;

    /// <summary>Stage scale, fixed so that a distance on screen converts back into a length.</summary>
    public const double PixelsPerMetre = 200.0;

    /// <summary>How much the mirror bar below the balls exaggerates the distance between them.</summary>
    public const double Mirror = 10.0;

    /// <summary>Canvas height in pixels: the fall plus a little headroom above the start line.</summary>
    public const double StageHeight = FallHeight * PixelsPerMetre + 20;

    /// <summary>Where the ground line sits, in stage pixels.</summary>
    public const double GroundY = FallHeight * PixelsPerMetre;
}

/// <summary>
/// One ball, integrating under <see cref="DemoChannel.Gravity"/>.
/// </summary>
/// <remarks>
/// Both pumps run this same type through this same <see cref="Step"/>. The comparison is entirely in the dt handed
/// to it — there is no second integration rule for a difference to hide in, so what the two balls disagree about
/// is the loop and nothing else.
/// <para>
/// No locking anywhere: exactly one pump thread owns each instance and the window never touches one. What crosses
/// threads is a <see cref="BallReport"/>, published whole.
/// </para>
/// </remarks>
internal sealed class BallBody(double gravity)
{
    /// <summary>Metres above the ground.</summary>
    public double Height;

    /// <summary>Metres per second, downward positive.</summary>
    public double Velocity;

    /// <summary>Seconds into the fall in progress. Restarts with the fall, because the closed form is compared against this.</summary>
    public double FallTime;

    /// <summary>Seconds fed in since the channel started. Never restarted by a landing.</summary>
    /// <remarks>
    /// Each pump's own record of how much time it has been given, so it is what the unspent-time readout is measured
    /// against: the bus's position minus this is the part of the timeline that pump has not driven yet. A landing must
    /// not clear it — with two balls landing at different moments, a clock that restarted with each fall would only
    /// be comparable between two of them.
    /// </remarks>
    public double DriveTime;

    private double _dtSum;
    private double _dtSquares;

    /// <summary>How many times the ball has been put back on the line.</summary>
    public int Falls { get; private set; }

    /// <summary>The dt this ball was really driven at: <c>Σdt² / Σdt</c>.</summary>
    /// <remarks>
    /// Not the arithmetic mean, and the difference is the point rather than precision. Expanding semi-implicit
    /// Euler from rest gives <c>Height - (g/2)t² = -(g/2)·Σdt²</c> exactly, for any dt sequence at all (see
    /// <see cref="Drift"/>), so this is the dt the integration can be reconstructed from. A plain mean only agrees
    /// when the steps are evenly spaced — and the update pump's are precisely the ones that are not.
    /// </remarks>
    public double EffectiveDt => _dtSum > 0 ? _dtSquares / _dtSum : 0;

    /// <summary>Metres below the closed form <c>(g/2)t²</c>. Always negative, and exactly <c>-(g/2)Σdt²</c>.</summary>
    public double Drift => -0.5 * gravity * _dtSquares;

    /// <summary>Advances the ball by one interval, however the pump measured it.</summary>
    public void Step(double seconds)
    {
        Velocity += gravity * seconds;
        Height += Velocity * seconds;
        FallTime += seconds;
        DriveTime += seconds;
        _dtSum += seconds;
        _dtSquares += seconds * seconds;
    }

    /// <summary>True once the ball has reached the fall height and <see cref="RestartFall"/> is due.</summary>
    public bool HasLanded => Height >= DemoChannel.FallHeight;

    /// <summary>Puts the ball back on the line, at rest, with the fall's accumulation cleared.</summary>
    /// <remarks>
    /// The step that crossed the line is discarded whole rather than trimmed to the crossing instant. Trimming
    /// would need that instant, and every reader of this class only needs a fall to start from rest at a known
    /// height. What it costs is honest and visible: a coarser dt overshoots further, so the update ball spends a
    /// little more time off the stage than the fixed one.
    /// </remarks>
    public void RestartFall()
    {
        Height = 0;
        Velocity = 0;
        FallTime = 0;
        _dtSum = 0;
        _dtSquares = 0;
        Falls++;
    }
}

/// <summary>Which hook an entry came from.</summary>
internal enum HookKind
{
    Awake,
    Start,
    Update,
    LateUpdate,
    FixedUpdate,
}

/// <summary>Something about a hook call that is worth a line even when the log is not recording every frame.</summary>
internal enum HookNote
{
    None,

    /// <summary>This call slept on purpose. Update owes a frame; FixedUpdate owes steps.</summary>
    Slept,

    /// <summary>This call is the one carrying the consequence of the previous call's sleep.</summary>
    AfterSleep,
}

/// <summary>One recorded hook call.</summary>
/// <remarks>
/// A struct, and built without touching a string: at the default frame rate a channel runs about 120 hooks a
/// second, and a log that allocated per call would be a GC cost the demo imposed on the very loop it is measuring.
/// </remarks>
internal readonly record struct HookEntry(
    HookKind Kind,
    int Index,
    double DtMilliseconds,
    int Batch,
    bool Handled,
    HookNote Note,
    long WallMs);

/// <summary>
/// An immutable snapshot of one ball and the pump that drove it, published by the hook that owns it.
/// </summary>
/// <remarks>
/// One reference, written once per hook call, so a reader takes the whole record or none of it. Writing the fields
/// in place would let the window show a position from one frame beside a dt from the next — which is how a
/// cross-thread bug turns into "sometimes the ball jumps" and never reproduces.
/// <para>
/// This is the reason the report is a class: a struct that size is not published atomically, and
/// <c>Volatile.Write</c> has no overload for one.
/// </para>
/// </remarks>
internal sealed record BallReport(
    double Height,
    double FallTime,
    double DriveTime,
    double EffectiveDt,
    double Drift,
    double DtMilliseconds,
    double UnspentMs,
    int Index,
    int Batch,
    int Falls,
    string Pump,
    HookNote Note,
    long WallMs)
{
    /// <summary>What a reader sees before the pump has run once.</summary>
    public static readonly BallReport Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "—", HookNote.None, 0);
}

/// <summary>
/// Everything the two pump threads write and the window reads.
/// </summary>
/// <remarks>
/// The rules, since none of this is enforced by a type:
/// <list type="bullet">
/// <item>Counters are incremented by one writer with <see cref="Interlocked"/> and read with <see cref="Volatile"/>.
/// They are never reset — they say what has happened since the channel started.</item>
/// <item>Balls are owned outright by one pump thread each and never leave their hook.</item>
/// <item>Flags run the other way: the window writes, a pump reads. One writer each, so a volatile int is enough.</item>
/// <item>The log is a <see cref="ConcurrentQueue{T}"/>, which is what the engine itself uses for every hand-off
/// between these threads.</item>
/// </list>
/// </remarks>
internal sealed class DemoState
{
    /// <summary>Driven by the window's <c>Update</c> hook — variable dt, one call per frame.</summary>
    public readonly BallBody UpdateBall = new(DemoChannel.Gravity);

    /// <summary>Driven by the window's <c>FixedUpdate</c> hook — a constant dt, one call per owed step.</summary>
    public readonly BallBody FixedBall = new(DemoChannel.Gravity);

    private BallReport _updateReport = BallReport.Empty;
    private BallReport _fixedReport = BallReport.Empty;

    /// <summary>The update ball and its pump, as of that pump's last call.</summary>
    public BallReport UpdateReport => Volatile.Read(ref _updateReport);

    /// <summary>The fixed ball and its pump, as of that pump's last call.</summary>
    public BallReport FixedReport => Volatile.Read(ref _fixedReport);

    public void PublishUpdate(BallReport report) => Volatile.Write(ref _updateReport, report);

    public void PublishFixed(BallReport report) => Volatile.Write(ref _fixedReport, report);

    public int AwakeCount;
    public int StartCount;
    public int UpdateCount;
    public int LateUpdateCount;
    public int FixedUpdateCount;

    /// <summary>Frames where LateUpdate ran without that frame's Update having run first. Must stay zero.</summary>
    public int OrderViolations;

    /// <summary>Frames where LateUpdate was skipped because Update set <c>e.Handled</c>.</summary>
    public int SkippedLateUpdates;

    /// <summary>
    /// <see cref="UpdateCount"/> as it stood when Awake, then Start, ran.
    /// </summary>
    /// <remarks>
    /// Zero is the whole evidence for "the lifecycle runs before the first frame body": they are driven from the
    /// loop's main-thread drain, which happens before the first frame's Update.
    /// </remarks>
    public int AwakeAtUpdateCount = -1;

    public int StartAtUpdateCount = -1;

    /// <summary>Frames completed when Awake ran, straight from the engine rather than from a counter of ours.</summary>
    public long AwakeFrameOrdinal = -1;

    /// <summary>Wall time of the last LateUpdate. A stale value means the hook stopped being called.</summary>
    public long LateUpdateWallMs;

    /// <summary>Where the last LateUpdate put the follower ring, in metres. Frozen means LateUpdate is not running.</summary>
    public double LateUpdateHeight;

    /// <summary>Window → Update. Non-zero asks the next Update to set <c>e.Handled</c>.</summary>
    public int HandledRequested;

    /// <summary>Window → Update: milliseconds to sleep inside the next Update, once.</summary>
    public int UpdateHitchMs;

    /// <summary>Window → FixedUpdate: milliseconds to sleep inside the next FixedUpdate, once.</summary>
    public int FixedHitchMs;

    /// <summary>Window → both pumps: a bump asks each to put its own ball back on the line.</summary>
    public int RestartGeneration;

    /// <summary>Window → both pumps: non-zero records every hook call instead of only the notable ones.</summary>
    public int RecordEveryHook;

    /// <summary>Hook calls not written to the log. Shown, so a filtered log is never mistaken for a quiet loop.</summary>
    public int OmittedCount;

    public readonly ConcurrentQueue<HookEntry> Log = new();

    public void Record(HookKind kind, int index, double dtMilliseconds, int batch, bool handled, HookNote note)
        => Log.Enqueue(new HookEntry(kind, index, dtMilliseconds, batch, handled, note, Environment.TickCount64));
}
