#nullable enable

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.TransitionSystem
{
    /// <summary>
    /// WinUI 3 的 UI 线程检测 / 编组实现。
    ///
    /// 目标对象（<see cref="DependencyObject"/>)优先：任意 <see cref="DependencyObject"/>
    /// 都携带它所属的 <see cref="DispatcherQueue"/>（其定义允许从非 UI 线程访问该对象），
    /// 因此动画目标只要是个 UI 元素，后台线程首次启动也能直接编组到它的 UI 线程，无需任何捕获。
    /// 非 DependencyObject 目标回退到惰性捕获的全局队列；也可用 <see cref="CaptureUIThread"/> 显式预捕获。
    /// </summary>
    public class UIThreadInspector : UIThreadInspectorCore<DispatcherQueuePriority>
    {
        private static DispatcherQueue? _dispatcherQueue;
        private static volatile bool _isAppAlive = true;

        /// <summary>
        /// 显式在 UI 线程上捕获 <see cref="DispatcherQueue"/>（可选）。非 DependencyObject
        /// 目标且动画从后台线程首次启动时才需要。
        /// </summary>
        public static void CaptureUIThread()
        {
            var current = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("CaptureUIThread must be called on the WinUI UI thread.");
            Interlocked.CompareExchange(ref _dispatcherQueue, current, null);
        }

        /// <summary>
        /// 惰性获取全局 DispatcherQueue：在 UI 线程上首次调用时自动捕获。
        /// 后台线程调用 GetForCurrentThread() 只会拿到 null，无副作用。
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
        /// 目标对象派生的 DispatcherQueue：目标若是 <see cref="DependencyObject"/>，
        /// 直接用它所属的队列；否则回退到全局捕获。
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

        public override List<object?> ProtectedInterpolate(object target, Func<List<object?>> interpolate)
        {
            var queue = QueueFor(target);
            if (queue != null)
            {
                if (queue.HasThreadAccess) return interpolate();

                var tcs = new TaskCompletionSource<List<object?>>();
                if (queue.TryEnqueue(() =>
                {
                    try { tcs.SetResult(interpolate()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                }))
                    return tcs.Task.GetAwaiter().GetResult() ?? [];

                _isAppAlive = false;
                return [];
            }
            return IsUIThread() ? interpolate() : [];
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
