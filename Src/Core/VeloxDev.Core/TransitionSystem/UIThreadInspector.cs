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
