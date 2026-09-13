using System.Threading;

namespace VeloxDev.TransitionSystem;

/// <summary>
/// Decides when the sampling loop's next frame happens, and owns the bookkeeping that keeps it safe.
/// </summary>
/// <remarks>
/// The default waits on a thread-pool timer, so the continuation resumes on whatever thread that timer fired on: a
/// custom awaiter is not marshalled back to a <see cref="SynchronizationContext"/> — see
/// <c>TransitionInterpreterCore.FrameWait</c>, which measures it. A host that owns a UI thread overrides
/// <see cref="Arm"/> and <see cref="Disarm"/> to wait on that thread instead, so the continuation is invoked there
/// in the first place. That pays off twice: the effect's <c>Update</c>/<c>LateUpdate</c> callbacks belong on the UI
/// thread, and the property writes reach the target directly — a loop running on a background thread made every one
/// of them queue a dispatch operation.
/// <para>
/// Posting the continuation back would be the obvious alternative, and costs a dispatch per frame, which is the one
/// thing the sampling path is built to avoid.
/// </para>
/// <para>
/// Subclassing rather than implementing an interface is what keeps the bookkeeping in one place. A pacer that
/// invoked its continuation twice would double-sample; one that never invoked it would park the loop for good, with
/// no exception and no frame. Neither failure is visible from the host side, and neither is worth re-deriving per
/// platform. Waiting on an existing central frame loop rather than on a timer of one's own is the same shape: arm
/// means registering with that loop and disarm means leaving it.
/// </para>
/// </remarks>
public abstract class FramePacerCore : IDisposable
{
    private Action? _pending;

    /// <summary>
    /// Arranges for <paramref name="continuation"/> to run once, no earlier than <paramref name="interval"/> from
    /// now, or as soon as <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// One loop owns one pacer and arms it again after every frame, so a pending continuation is replaced rather
    /// than queued.
    /// <para>
    /// A subclass generally cannot observe a later cancellation without allocating a registration per frame, and is
    /// not required to: letting the interval elapse first is safe, because the loop re-reads its token on waking and
    /// a frame that lands after a cancellation cannot corrupt anything — <c>SamplerSet.Apply</c> discards it. The
    /// cost is that a stop takes up to one interval to be noticed.
    /// </para>
    /// </remarks>
    public void Schedule(Action continuation, TimeSpan interval, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            continuation();
            return;
        }

        // 先挂续体再起表：反过来的话，一个立刻到期的定时器可能在续体挂上之前就 tick 了。
        Volatile.Write(ref _pending, continuation);
        Arm(interval);
    }

    /// <summary>
    /// Starts, or re-arms, the wait so that it completes once after <paramref name="interval"/>.
    /// </summary>
    /// <remarks>
    /// Called once per frame, always after the continuation has been published. The interval is re-read every time
    /// because an effect's FPS may change mid-animation.
    /// </remarks>
    protected abstract void Arm(TimeSpan interval);

    /// <summary>
    /// Ends the wait. Must tolerate being called when nothing is armed.
    /// </summary>
    /// <remarks>
    /// Called once per frame, so it must not allocate — a host timer's tick handler, for instance, belongs attached
    /// for the life of the pacer rather than re-attached per frame.
    /// </remarks>
    protected abstract void Disarm();

    /// <summary>
    /// Invokes the pending continuation, if any, exactly once.
    /// </summary>
    /// <remarks>
    /// Call this when the wait completes — from the host timer's tick, or from the central loop's own frame. Disarms
    /// first, so a repeating wait cannot complete again before the loop has armed the next frame.
    /// </remarks>
    protected void Fire()
    {
        Disarm();
        Interlocked.Exchange(ref _pending, null)?.Invoke();
    }

    /// <summary>
    /// Ends the wait and releases any pending continuation.
    /// </summary>
    /// <remarks>
    /// Releasing matters: a loop waiting on a continuation that is never invoked is stranded for good. An
    /// implementation that owns a disposable resource overrides this, releases it, and calls the base last.
    /// </remarks>
    public virtual void Dispose()
    {
        Disarm();

        // 放行挂着的续体——这正是「释放一个 pacer」在语义上必须做的事。
        Interlocked.Exchange(ref _pending, null)?.Invoke();

        GC.SuppressFinalize(this);
    }
}
