#nullable enable

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Threading;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : TransitionHostBase<DispatcherQueuePriority>
    {
        private static DispatcherQueue? _dispatcherQueue;

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

        public override ThreadRef ThreadFor(object target) => ThreadRef.From(QueueFor(target));

        protected override bool IsCurrentThread(ThreadRef thread)
            => thread.TryGet<DispatcherQueue>(out var queue) && queue.HasThreadAccess;

        protected override DispatcherQueuePriority InternalPriority => DispatcherQueuePriority.Normal;

        protected override bool PostCore(object target, ThreadRef thread, Action action, DispatcherQueuePriority priority)
        {
            if (!thread.TryGet<DispatcherQueue>(out var queue)) return false;

            // 队列拒绝说明这个应用在退出，接纳说明它还活着：两个方向都报，一次瞬时拒绝不会永久判死。
            var accepted = queue.TryEnqueue(priority, () => action());
            Lifetime.SetAlive(accepted);
            return accepted;
        }
    }
}
