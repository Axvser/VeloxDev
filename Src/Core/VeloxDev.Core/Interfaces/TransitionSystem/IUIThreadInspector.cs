namespace VeloxDev.TransitionSystem
{
    public interface IUIThreadInspector<TPriorityCore> : IUIThreadInspectorCore
    {
        /// <summary>
        /// Hands <paramref name="action"/> to the UI thread, fire-and-forget. Returns false when the action could not
        /// be queued at all — the host's dispatcher is gone, or the target has no queue yet — which is the only way
        /// a caller can tell a dropped action from a queued one.
        /// </summary>
        /// <remarks>
        /// <b>Must not block and must not throw.</b> It is called once per animated property per frame, from inside
        /// <c>SamplerSet.Apply</c>, whose caller treats any exception as the animation having failed — and it discards
        /// the return value, so one transient refusal that surfaced as an exception would end the animation outright.
        /// Reporting the failure through the return value is the whole point of it.
        /// </remarks>
        public bool ProtectedInvoke(object target, Action action, TPriorityCore priority);

        /// <summary>
        /// Same as <see cref="ProtectedInvoke"/>, but completes only once <paramref name="action"/> has actually run.
        /// </summary>
        /// <remarks>
        /// For the one call per animation that has to happen before the frames start — the effect's Awake — rather
        /// than for the frames themselves, which stay fire-and-forget. A false return means the action was never
        /// queued, so there is nothing to wait for.
        /// </remarks>
        public Task<bool> ProtectedInvokeAsync(object target, Action action, TPriorityCore priority);
    }

    /// <summary>
    /// Answers which thread a host's UI work belongs on, and marshals to it.
    /// </summary>
    /// <remarks>
    /// Three questions, asked by three different callers, and deliberately not interchangeable. <see cref="IsUIThread"/>
    /// is asked per dispatch to choose inline-versus-queue. <c>ProtectedInvoke</c> and
    /// <see cref="ProtectedGetValue"/> carry one write or read across. <see cref="IUIThreadAffinity.ThreadFor"/>
    /// answers which thread owns <em>this</em> target, and is what the sampling loop's frame pacer is derived from.
    /// </remarks>
    public interface IUIThreadInspectorCore
    {
        /// <summary>Whether the host's application is still running; false keeps the scheduler from starting new work.</summary>
        public bool IsAppAlive();

        /// <summary>
        /// Whether the calling thread is <em>the</em> UI thread — the one this inspector marshals to.
        /// </summary>
        /// <remarks>
        /// Narrow on purpose. This is the <c>onUIThread</c> argument to <c>UIThreadInspectorBase.DispatchAsync</c>,
        /// which chooses between running an action inline and queueing it and reporting completion. Widening it to
        /// "the calling thread owns some dispatcher" would let an action whose target belongs to a different
        /// dispatcher take the inline branch and be reported as done without having run. A per-target answer belongs
        /// in <see cref="IUIThreadAffinity.ThreadFor"/> instead — a different question, with a different cost of
        /// being wrong.
        /// </remarks>
        public bool IsUIThread();

        /// <summary>
        /// Reads <paramref name="property"/> from <paramref name="target"/>, marshaling to the UI thread when the
        /// calling thread is not it. Returns <c>default</c> when the read cannot be performed at all.
        /// </summary>
        /// <remarks>
        /// This blocks the calling thread when marshaling is what costs, so a host may only ever block on work it has
        /// actually queued. An action a host silently dropped would never complete its completion source, and the
        /// caller would wait on it for the rest of the process — <c>UIThreadInspectorBase.DispatchAsync</c> states the
        /// same rule for the await path. A queue that refuses the work must produce this same <c>default</c> answer,
        /// exactly as having no thread at all does.
        /// <para>
        /// <b>Must not throw.</b> It runs once per animated property per frame, from inside <c>SamplerSet.Apply</c>,
        /// which the sampling loop executes under a catch that treats any exception as the animation having failed —
        /// so a single throwing lookup ends the animation. A platform API that throws rather than returning null for
        /// an unattached or not-yet-built object has to be guarded in the implementation, and the guard has to hold on
        /// every call, not just the first: a lookup that does not cache its failure throws again on the next frame.
        /// </para>
        /// </remarks>
        public object? ProtectedGetValue(object target, ITransitionProperty property);
    }
}
