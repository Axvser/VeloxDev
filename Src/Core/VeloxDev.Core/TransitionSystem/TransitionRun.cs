using VeloxDev.Timing;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>
/// One running animation: the token that ends it, the time source it is anchored to, and where in that source the
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
internal sealed class TransitionRun : IDisposable
{
    private long _passAnchor;
    private long _cycle;

    internal TransitionRun(ITimeSourceControl timeline)
    {
        Cts = new CancellationTokenSource();
        Timeline = timeline;
        _passAnchor = timeline.Ticks;
    }

    internal CancellationTokenSource Cts { get; }

    internal ITimeSourceControl Timeline { get; }

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

    /// <summary>
    /// Releases the token source. Called by whoever owns the run, once the animation is over.
    /// </summary>
    /// <remarks>
    /// Deliberately not done by <c>Untrack</c>, and deliberately after the last reader rather than at the first
    /// opportunity: afterwards <c>Cancel</c> and <c>Register</c> throw while <c>IsCancellationRequested</c> keeps
    /// answering, which is what lets a drain that arrives late stay quiet and a frame that lands late still see the
    /// cancellation it was posted under.
    /// </remarks>
    public void Dispose() => Cts.Dispose();
}
