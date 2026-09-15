namespace VeloxDev.TransitionSystem
{
    /// <summary>
    /// A capability a UI-thread inspector may additionally implement: naming the UI thread a specific target
    /// belongs to.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="IUIThreadInspectorCore"/> rather than a member of it. This is a
    /// capability a host opts into, not something every inspector has to carry: Core multi-targets down to
    /// <c>netstandard2.0</c>, where there is no default interface member to put an optional one in, so adding a
    /// member would break every existing implementor at source level — including the hand-written doubles in
    /// <c>VeloxDev.Core.Test</c>.
    /// <para>
    /// The handle comes back as <see cref="object"/> rather than as a type parameter. Core cannot name any host's
    /// UI-thread type — a <c>Dispatcher</c>, a <c>DispatcherQueue</c>, an <c>IDispatcher</c> — and the only consumer,
    /// an adapter's interpreter, is handed the inspector as <see cref="IUIThreadInspectorCore"/> and so has to match
    /// it by type either way. A type parameter would only add a way to get this silently wrong: an inspector that
    /// declared itself <c>IUIThreadAffinity&lt;object&gt;</c> would fail the match and the symptom would be a null
    /// pacer, with the loop quietly left on the thread pool.
    /// </para>
    /// <para>
    /// <b>Null is a valid and important answer</b>, meaning no thread owns this target — the loop then belongs on
    /// the thread-pool pacer. Never answer with <c>Dispatcher.CurrentDispatcher</c> or an equivalent that mints a
    /// dispatcher for the calling thread: a dispatcher whose message loop is never pumped strands the loop with no
    /// exception and no frame, which is precisely the failure this method exists to avoid.
    /// </para>
    /// <para>
    /// Must be callable from any thread, and <b>must not throw</b>. It is consulted from the write path on every
    /// frame, where an exception is indistinguishable from an animation failure: <c>SamplerSet.Apply</c> runs under
    /// the sampling loop's catch, which treats one as the animation being over and cancels it.
    /// </para>
    /// <para>
    /// A platform with no publicly reachable way to name a dispatcher simply does not implement this, and that is a
    /// supported shape rather than an oversight — Avalonia, WinForms and Razor do not. An inspector without affinity
    /// costs its host nothing beyond what it already had: <c>TransitionInterpreterCore.CreateFramePacer</c> falls
    /// back to an application-level thread. Where a thread <em>is</em> named, the ordering rules on
    /// <c>UIThreadInspectorBase</c> apply.
    /// </para>
    /// </remarks>
    public interface IUIThreadAffinity
    {
        /// <summary>
        /// The host's UI-thread handle for <paramref name="target"/>, or null when it has none.
        /// </summary>
        public object? ThreadFor(object target);
    }
}
