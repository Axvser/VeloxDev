using System;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace VeloxDev.TransitionSystem
{
    public partial class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect, DispatcherQueuePriority>
    {
        /// <summary>
        /// Waits each frame on the UI thread's queue, so the sampling loop — and the effect's
        /// <c>Update</c>/<c>LateUpdate</c> callbacks — run there.
        /// </summary>
        /// <remarks>
        /// The current thread's queue first, then the one the inspector captured, so an animation started from a
        /// background thread still lands on the UI thread. Null — leaving the thread-pool pacer in place — only
        /// when neither is known, which is the case for a background start that never touched the UI thread.
        /// </remarks>
        protected override FramePacerCore? CreateFramePacer()
            => (DispatcherQueue.GetForCurrentThread() ?? UIThreadInspector.CapturedQueue) is { } queue
                ? new DispatcherQueueFramePacer(queue)
                : null;

        /// <summary>
        /// A one-shot wait on the queue: nothing is posted, so no queue operation is allocated per frame.
        /// </summary>
        private sealed class DispatcherQueueFramePacer(DispatcherQueue queue) : FramePacerCore
        {
            private DispatcherQueueTimer? _timer;

            protected override void Arm(TimeSpan interval)
            {
                var timer = _timer ??= CreateTimer();
                timer.Interval = interval;
                timer.Start();
            }

            protected override void Disarm() => _timer?.Stop();

            private DispatcherQueueTimer CreateTimer()
            {
                var timer = queue.CreateTimer();
                timer.IsRepeating = false;
                timer.Tick += OnTick;
                return timer;
            }

            private void OnTick(DispatcherQueueTimer sender, object args) => Fire();
        }
    }
}
