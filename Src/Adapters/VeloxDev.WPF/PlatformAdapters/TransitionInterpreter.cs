using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect, DispatcherPriority>
    {
        /// <summary>
        /// Waits each frame on the dispatcher's own thread, so the sampling loop — and the effect's
        /// <c>Update</c>/<c>LateUpdate</c> callbacks — run there.
        /// </summary>
        /// <remarks>
        /// Null when there is no application dispatcher, which falls back to the thread-pool pacer. That is the
        /// honest answer: <see cref="Dispatcher.CurrentDispatcher"/> would mint a dispatcher on whatever thread
        /// first armed the loop and park the animation there.
        /// </remarks>
        protected override FramePacerCore? CreateFramePacer()
            => Application.Current?.Dispatcher is { } dispatcher ? new DispatcherFramePacer(dispatcher) : null;

        /// <summary>
        /// A one-shot wait on the dispatcher: nothing is posted, so no
        /// <see cref="System.Windows.Threading.DispatcherOperation"/> is allocated per frame.
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
