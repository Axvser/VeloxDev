using System;
using System.Threading;
using Avalonia.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect, DispatcherPriority>
    {
        /// <summary>
        /// Waits each frame on the UI thread, so the sampling loop — and the effect's
        /// <c>Update</c>/<c>LateUpdate</c> callbacks — run there.
        /// </summary>
        /// <remarks>
        /// Only when the loop is already on the UI thread, and that conservatism is forced by the framework:
        /// Avalonia's <see cref="DispatcherTimer"/> has neither a constructor that takes a <see cref="Dispatcher"/>
        /// nor a <see cref="Dispatcher"/> property, so there is no way to name the target thread and no way to
        /// check afterwards which one was picked. The other platforms name it explicitly — an application
        /// dispatcher, a queue, a message loop — and so can also cover an animation started from a background
        /// thread. Here a background first start keeps the thread-pool pacer, which is what it had before.
        /// </remarks>
        protected override FramePacerCore? CreateFramePacer()
            => Dispatcher.UIThread?.CheckAccess() == true ? new UiThreadFramePacer() : null;

        /// <summary>
        /// A one-shot wait on the UI dispatcher: nothing is posted, so no dispatch operation is allocated per frame.
        /// </summary>
        private sealed class UiThreadFramePacer : FramePacerCore
        {
            private DispatcherTimer? _timer;

            protected override void Arm(TimeSpan interval)
            {
                var timer = _timer ??= CreateTimer();
                timer.Interval = interval;
                timer.Start();
            }

            protected override void Disarm() => _timer?.Stop();

            private DispatcherTimer CreateTimer()
            {
                var timer = new DispatcherTimer();
                timer.Tick += OnTick;
                return timer;
            }

            private void OnTick(object? sender, EventArgs e) => Fire();
        }
    }
}
