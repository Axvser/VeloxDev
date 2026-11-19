namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer that carries one materialization batch to the host's thread at a platform
/// priority.
/// </summary>
/// <typeparam name="TPriorityCore">The platform's dispatcher-priority type, or <c>NonPriority</c>.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Schedule"/> must post the batch and return; it must not invoke the batch synchronously.
/// The pool uses that callback boundary to keep materialization out of the collection-change call
/// stack. This differs from <c>IThreadDispatcher.Post</c>, which may run inline when already on the
/// target thread.
/// </para>
/// <para>
/// Return <see langword="false"/> when the platform refused the request; the pool will schedule again
/// on the next visible-set change.
/// </para>
/// </remarks>
public interface IViewScheduler<TPriorityCore>
{
    /// <summary>Gets the priority the pool uses for its materialization batches.</summary>
    TPriorityCore BatchPriority { get; }

    /// <summary>Posts one materialization batch to the host's thread.</summary>
    /// <param name="batch">The batch to run later. The scheduler must not invoke it synchronously.</param>
    /// <param name="priority">The priority <see cref="BatchPriority"/> selected for this batch.</param>
    /// <returns><see langword="true"/> when the batch was accepted; otherwise <see langword="false"/>.</returns>
    bool Schedule(Action batch, TPriorityCore priority);
}
