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

    public virtual bool IsCurrent(object target) => IsCurrentFor(target, ThreadFor(target));

    /// <summary>
    /// Whether the calling thread owns <paramref name="target"/>, given the thread already resolved for it.
    /// </summary>
    /// <remarks>
    /// The form the write path uses, so the lookup happens once: a host that can answer more precisely for a target
    /// overrides this rather than <see cref="IsCurrent"/>, and <see cref="PostCore"/> is handed the same answer.
    /// </remarks>
    protected virtual bool IsCurrentFor(object target, ThreadRef thread) => IsCurrentThread(thread);

    /// <summary>Whether the calling thread is the one <paramref name="thread"/> names.</summary>
    protected abstract bool IsCurrentThread(ThreadRef thread);

    /// <summary>
    /// Queues <paramref name="action"/> on the target's thread — the one already resolved from it — and returns
    /// whether it was accepted.
    /// </summary>
    /// <remarks>
    /// Must not block. A host that needs the target rather than the thread (WinForms posts through the Control it was
    /// given) is free to ignore <paramref name="thread"/>.
    /// </remarks>
    protected abstract bool PostCore(object target, ThreadRef thread, Action action, TPriorityCore priority);

    /// <remarks>
    /// Defaulted rather than required because <c>default(NonPriority)</c> is the whole story for a host that has no
    /// priority. A host that has one must override it: <c>default(DispatcherPriority)</c> is <c>Inactive</c>, which
    /// would park a blocking read behind every normal message.
    /// </remarks>
    protected virtual TPriorityCore InternalPriority => default!;

    public bool Post(object target, Action action, TPriorityCore priority)
    {
        var thread = ThreadFor(target);
        return IsCurrentFor(target, thread) ? RunInline(action) : PostCore(target, thread, action, priority);
    }

    public async Task<bool> PostAsync(object target, Action action, TPriorityCore priority)
    {
        var thread = ThreadFor(target);
        if (IsCurrentFor(target, thread)) return RunInline(action);

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = PostCore(target, thread, () =>
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
        var thread = ThreadFor(target);
        if (IsCurrentFor(target, thread)) return body();

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!PostCore(target, thread, () =>
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
