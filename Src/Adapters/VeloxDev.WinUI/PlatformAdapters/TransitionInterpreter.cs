using System;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace VeloxDev.TransitionSystem
{
    public partial class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect, DispatcherQueuePriority>
    {
        protected override FramePacerCore? CreateFramePacer(object target, IUIThreadInspectorCore inspector)
        {
            if (inspector is IUIThreadAffinity affinity)
            {
                return affinity.ThreadFor(target) is DispatcherQueue queue ? new DispatcherQueueFramePacer(queue) : null;
            }

            return (DispatcherQueue.GetForCurrentThread() ?? UIThreadInspector.CapturedQueue) is { } fallback
                ? new DispatcherQueueFramePacer(fallback)
                : null;
        }

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
