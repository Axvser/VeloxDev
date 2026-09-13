using System.Threading;
using Jalium.UI;
using Jalium.UI.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect, DispatcherPriority>
    {
        /// <summary>
        /// Waits each frame on the main dispatcher's thread, so the sampling loop — and the effect's
        /// <c>Update</c>/<c>LateUpdate</c> callbacks — run there.
        /// </summary>
        /// <remarks>
        /// Falls back the way the inspector does: the application's dispatcher, then the static main one, and only
        /// then null — which keeps the thread-pool pacer rather than minting a dispatcher on whichever thread first
        /// armed the loop.
        /// </remarks>
        protected override FramePacerCore? CreateFramePacer()
            => (Application.Current?.Dispatcher ?? Dispatcher.MainDispatcher) is { } dispatcher
                ? new DispatcherFramePacer(dispatcher)
                : null;

        /// <summary>
        /// A one-shot wait on the main dispatcher: nothing is posted, so no dispatch operation is allocated per
        /// frame.
        /// </summary>
        private sealed class DispatcherFramePacer(Dispatcher dispatcher) : FramePacerCore
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
                var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
                timer.Tick += OnTick;
                return timer;
            }

            private void OnTick(object? sender, EventArgs e) => Fire();
        }
    }
}
