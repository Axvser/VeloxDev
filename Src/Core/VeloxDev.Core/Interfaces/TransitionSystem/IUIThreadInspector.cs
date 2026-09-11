namespace VeloxDev.TransitionSystem
{
    public interface IUIThreadInspector<TPriorityCore> : IUIThreadInspectorCore
    {
        /// <summary>
        /// Hands <paramref name="action"/> to the UI thread, fire-and-forget. Returns false when the action could not
        /// be queued at all — the host's dispatcher is gone, or the target has no queue yet — which is the only way
        /// a caller can tell a dropped action from a queued one.
        /// </summary>
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

    public interface IUIThreadInspectorCore
    {
        public bool IsAppAlive();
        public bool IsUIThread();
        public object? ProtectedGetValue(object target, ITransitionProperty property);
    }
}
