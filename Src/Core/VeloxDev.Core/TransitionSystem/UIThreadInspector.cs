namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class UIThreadInspectorCore<TPriorityCore> : UIThreadInspectorBase, IUIThreadInspector<TPriorityCore>
{
    public abstract bool ProtectedInvoke(object target, Action action, TPriorityCore priority);

    public virtual Task<bool> ProtectedInvokeAsync(object target, Action action, TPriorityCore priority)
        => DispatchAsync(queue => ProtectedInvoke(target, queue, priority), IsUIThread(), action);
}

public abstract class UIThreadInspectorCore : UIThreadInspectorBase, IUIThreadInspector<NonPriority>
{
    public abstract bool ProtectedInvoke(object target, Action action);

    /// <summary>Runs <paramref name="action"/> on the UI thread, discarding the priority — this inspector has none.</summary>
    public virtual bool ProtectedInvoke(object target, Action action, NonPriority priority) => ProtectedInvoke(target, action);

    public virtual Task<bool> ProtectedInvokeAsync(object target, Action action, NonPriority priority)
        => DispatchAsync(queue => ProtectedInvoke(target, queue), IsUIThread(), action);
}

/// <summary>
/// The marshaling contract every adapter's inspector implements, in one place: what the members mean is
/// <see cref="IUIThreadInspectorCore"/>'s, and this type adds only the dispatch that awaits.
/// </summary>
/// <remarks>
/// <b>Naming the thread for a target.</b> Where an inspector resolves a thread, the order is the target's own, then
/// the application's, and only then the calling thread's.
/// <list type="bullet">
/// <item>A target takes priority because UI objects carry their owning dispatcher and can be marshaled from any
/// thread. That is what lets an animation started on a background thread reach the UI at all.</item>
/// <item>The application's thread outranks the calling thread's because when it exists it is by definition a running
/// message loop, whereas a dispatcher merely reachable from the current thread may have nothing pumping it. A loop
/// pinned to a dispatcher that never pumps is stranded with no exception and no frame, and the failure is invisible
/// from the host side.</item>
/// <item>Never <em>mint</em> a thread for the caller — WPF's <c>Dispatcher.CurrentDispatcher</c> does — because that
/// manufactures exactly the never-pumped dispatcher above. Look the thread up, and let the answer be null.</item>
/// </list>
/// Null is a real answer: it means no thread owns this target, and the sampling loop keeps the thread-pool pacer.
/// <para>
/// <b>Shutting down.</b> An inspector whose thread is going away declines rather than marshals — but only on the
/// branch that would actually hop threads, never before the "already on the right thread" check. Running in place is
/// always safe; declining early would throw away writes that still work.
/// </para>
/// </remarks>
public abstract class UIThreadInspectorBase : IUIThreadInspectorCore
{
    public abstract bool IsAppAlive();
    public abstract bool IsUIThread();
    public abstract object? ProtectedGetValue(object target, ITransitionProperty property);

    /// <summary>
    /// Queues <paramref name="action"/> and, unless this is already the UI thread, waits for it to have run.
    /// </summary>
    /// <remarks>
    /// The wait only ever starts when <paramref name="enqueue"/> reports the action was accepted. That report is
    /// what makes awaiting safe: an action the host silently dropped would never complete its completion source, and
    /// a caller waiting on it would hold the scheduler's gate for the rest of the process. A false return lets the
    /// caller give up instead.
    /// </remarks>
    protected static async Task<bool> DispatchAsync(Func<Action, bool> enqueue, bool onUIThread, Action action)
    {
        if (onUIThread)
        {
            return enqueue(action);
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = enqueue(() =>
        {
            try
            {
                action();
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        if (!accepted)
        {
            return false;
        }

        await completion.Task.ConfigureAwait(false);
        return true;
    }
}
