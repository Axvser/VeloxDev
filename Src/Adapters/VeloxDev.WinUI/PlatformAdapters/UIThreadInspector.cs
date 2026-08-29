#nullable enable

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.TransitionSystem
{
    /// <summary>
    /// WinUI 3 UI-thread detection/marshaling implementation.
    ///
    /// The target object (<see cref="DependencyObject"/>) takes priority: every <see cref="DependencyObject"/>
    /// carries its owning <see cref="DispatcherQueue"/> (accessible from a non-UI thread), so if the animation
    /// target is a UI element, a background-thread first start marshals directly to its UI thread with no capture.
    /// Non-DependencyObject targets fall back to the lazily captured global queue; <see cref="CaptureUIThread"/> can pre-capture explicitly.
    /// </summary>
    public class UIThreadInspector : UIThreadInspectorCore<DispatcherQueuePriority>
    {
        private static DispatcherQueue? _dispatcherQueue;
        private static volatile bool _isAppAlive = true;

        /// <summary>
        /// Optionally capture the <see cref="DispatcherQueue"/> on the UI thread. Only needed for non-DependencyObject
        /// targets when the animation first starts from a background thread.
        /// </summary>
        public static void CaptureUIThread()
        {
            var current = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("CaptureUIThread must be called on the WinUI UI thread.");
            Interlocked.CompareExchange(ref _dispatcherQueue, current, null);
        }

        /// <summary>
        /// Lazily gets the global DispatcherQueue: captures it automatically the first time it is called on the UI thread.
        /// Calling GetForCurrentThread() from a background thread only returns null, with no side effects.
        /// </summary>
        private static DispatcherQueue? EnsureQueue()
        {
            var queue = Volatile.Read(ref _dispatcherQueue);
            if (queue != null) return queue;

            var current = DispatcherQueue.GetForCurrentThread();
            if (current != null)
                Interlocked.CompareExchange(ref _dispatcherQueue, current, null);
            return _dispatcherQueue;
        }

        /// <summary>
        /// DispatcherQueue derived from the target object: if the target is a <see cref="DependencyObject"/>,
        /// uses its owning queue directly; otherwise falls back to the globally captured one.
        /// </summary>
        private static DispatcherQueue? QueueFor(object target)
        {
            if (target is DependencyObject dependencyObject && dependencyObject.DispatcherQueue is { } queue)
                return queue;
            return EnsureQueue();
        }

        public override bool IsAppAlive() => _isAppAlive;

        public override bool IsUIThread()
        {
            var queue = EnsureQueue();
            return queue?.HasThreadAccess ?? false;
        }

        public override object? ProtectedGetValue(object target, ITransitionProperty property)
        {
            var queue = QueueFor(target);
            if (queue != null)
            {
                if (queue.HasThreadAccess) return property.GetValue(target);

                var tcs = new TaskCompletionSource<object?>();
                if (queue.TryEnqueue(() =>
                {
                    try { tcs.SetResult(property.GetValue(target)); }
                    catch (Exception ex) { tcs.SetException(ex); }
                }))
                    return tcs.Task.GetAwaiter().GetResult();

                _isAppAlive = false;
                return default;
            }
            return IsUIThread() ? property.GetValue(target) : default;
        }

        public override void ProtectedInvoke(object target, Action action, DispatcherQueuePriority priority)
        {
            var queue = QueueFor(target);
            if (queue != null)
            {
                if (queue.HasThreadAccess) { action(); return; }
                if (queue.TryEnqueue(priority, () =>
                {
                    try { action(); }
                    catch { }
                }))
                    return;
                _isAppAlive = false;
                return;
            }
            if (IsUIThread()) action();
        }
    }
}
