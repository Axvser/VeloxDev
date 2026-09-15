#nullable enable

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : UIThreadInspectorCore<DispatcherQueuePriority>, IUIThreadAffinity
    {
        private static DispatcherQueue? _dispatcherQueue;
        private static volatile bool _isAppAlive = true;

        /// <summary>Captures the current thread's queue. Only needed for a non-<see cref="DependencyObject"/> target.</summary>
        public static void CaptureUIThread()
        {
            var current = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("CaptureUIThread must be called on the WinUI UI thread.");
            Interlocked.CompareExchange(ref _dispatcherQueue, current, null);
        }

        internal static DispatcherQueue? CapturedQueue => Volatile.Read(ref _dispatcherQueue);

        private static DispatcherQueue? EnsureQueue()
        {
            var queue = Volatile.Read(ref _dispatcherQueue);
            if (queue != null) return queue;

            var current = DispatcherQueue.GetForCurrentThread();
            if (current != null)
                Interlocked.CompareExchange(ref _dispatcherQueue, current, null);
            return _dispatcherQueue;
        }

        internal static DispatcherQueue? QueueFor(object target)
        {
            if (target is DependencyObject dependencyObject && dependencyObject.DispatcherQueue is { } queue)
                return queue;
            return EnsureQueue();
        }

        public object? ThreadFor(object target) => QueueFor(target);

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

        public override bool ProtectedInvoke(object target, Action action, DispatcherQueuePriority priority)
        {
            var queue = QueueFor(target);
            if (queue != null)
            {
                if (queue.HasThreadAccess) { action(); return true; }
                if (queue.TryEnqueue(priority, () =>
                {
                    try { action(); }
                    catch { }
                }))
                    return true;
                _isAppAlive = false;
                return false;
            }
            if (!IsUIThread()) return false;
            action();
            return true;
        }
    }
}
