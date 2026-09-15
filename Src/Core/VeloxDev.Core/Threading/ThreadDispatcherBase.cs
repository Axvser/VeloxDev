namespace VeloxDev.Threading;

/// <summary>
/// The derived surface of <see cref="IThreadDispatcher{TPriorityCore}"/>, implemented once so no host can derive it
/// differently from another.
/// </summary>
/// <remarks>
/// A host supplies <see cref="ThreadFor"/>, <see cref="IsCurrentThread"/> and <see cref="PostCore"/>. Everything else
/// lives here.
/// </remarks>
public abstract class ThreadDispatcherBase<TPriorityCore> : IThreadDispatcher<TPriorityCore>
{
    public abstract ThreadRef ThreadFor(object target);

    /// <remarks>
    /// Defaults to the target's own thread, which is correct for any GUI: a view must be created on the UI thread, so
    /// the thread a target is on and the application's UI thread are the same one. A host that can answer more
    /// precisely overrides this — WinForms reads it off the Control, the only way to be right for a second UI thread.
    /// </remarks>
    public virtual bool IsCurrent(object target) => IsCurrentThread(ThreadFor(target));

    /// <summary>Whether the calling thread is the one <paramref name="thread"/> names.</summary>
    protected abstract bool IsCurrentThread(ThreadRef thread);

    /// <summary>Queues <paramref name="action"/> on the target's thread. Must not block; false means it was refused.</summary>
    protected abstract bool PostCore(object target, Action action, TPriorityCore priority);

    /// <remarks>
    /// Defaulted rather than required because <c>default(NonPriority)</c> is the whole story for a host that has no
    /// priority. A host that has one must override it: <c>default(DispatcherPriority)</c> is <c>Inactive</c>, which
    /// would park a blocking read behind every normal message.
    /// </remarks>
    protected virtual TPriorityCore InternalPriority => default!;

    public bool Post(object target, Action action, TPriorityCore priority)
        => IsCurrent(target) ? RunInline(action) : PostCore(target, action, priority);

    public async Task<bool> PostAsync(object target, Action action, TPriorityCore priority)
    {
        if (IsCurrent(target)) return RunInline(action);

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = PostCore(target, () =>
        {
            try { action(); completion.TrySetResult(null); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }, priority);

        // 只有真的排进队列才等：宿主静默丢掉的动作永远不会完成它的 TCS，等下去就是等一辈子。
        if (!accepted) return false;

        await completion.Task.ConfigureAwait(false);
        return true;
    }

    public virtual T Run<T>(object target, Func<T> body)
    {
        if (IsCurrent(target)) return body();

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!PostCore(target, () =>
            {
                try { completion.TrySetResult(body()); }
                catch (Exception exception) { completion.TrySetException(exception); }
            }, InternalPriority))
        {
            return default!;
        }

        return (T)completion.Task.GetAwaiter().GetResult()!;
    }

    private static bool RunInline(Action action)
    {
        action();
        return true;
    }
}
