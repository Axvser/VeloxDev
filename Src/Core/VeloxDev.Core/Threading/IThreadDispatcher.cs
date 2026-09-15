namespace VeloxDev.Threading;

/// <summary>Which thread owns a target, and whether the caller is on it.</summary>
/// <remarks>
/// Every member is target-relative, deliberately: asking "am I the UI thread" globally and "which thread owns this
/// target" separately agrees whenever a host has one UI thread and disagrees exactly when it does not.
/// <para>
/// Nothing here may throw. These members are reached from a write path that runs once per frame, where an exception
/// is indistinguishable from the work having failed; a refusal is reported through the return value.
/// </para>
/// </remarks>
public interface IThreadAffinity
{
    /// <remarks>
    /// Never mint one for the caller. <c>Dispatcher.CurrentDispatcher</c> and its equivalents manufacture a dispatcher
    /// for the calling thread, pinning the consumer to a message pump nobody drives — with nothing to report it.
    /// </remarks>
    ThreadRef ThreadFor(object target);

    bool IsCurrent(object target);
}

/// <summary>Carrying work to a target's thread.</summary>
public interface IThreadDispatcher<TPriorityCore> : IThreadAffinity
{
    /// <remarks>
    /// Must not block. The return value is the only way to tell a dropped action from a queued one, and an optimistic
    /// <c>true</c> for work that never runs hangs whoever waits on it.
    /// </remarks>
    bool Post(object target, Action action, TPriorityCore priority);

    /// <summary>Same as <see cref="Post"/>, but completes once <paramref name="action"/> has actually run.</summary>
    Task<bool> PostAsync(object target, Action action, TPriorityCore priority);

    /// <remarks>
    /// May block, and only ever on work this dispatcher actually queued. Returns <c>default</c> when it could not
    /// queue it — <typeparamref name="T"/> is unconstrained, so a value type's failure answer is its zero.
    /// </remarks>
    T Run<T>(object target, Func<T> body);
}
