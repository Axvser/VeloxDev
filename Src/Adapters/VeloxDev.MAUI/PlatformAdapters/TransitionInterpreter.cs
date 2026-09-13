namespace VeloxDev.TransitionSystem
{
    public partial class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect>
    {
        /// <summary>
        /// Waits each frame on the app's dispatcher, so the sampling loop — and the effect's
        /// <c>Update</c>/<c>LateUpdate</c> callbacks — run on the UI thread.
        /// </summary>
        /// <remarks>
        /// Null before the application exists, which leaves the thread-pool pacer in place rather than creating a
        /// timer that could never tick.
        /// </remarks>
        protected override FramePacerCore? CreateFramePacer()
            => Application.Current?.Dispatcher is { } dispatcher ? new DispatcherFramePacer(dispatcher) : null;

        /// <summary>
        /// A one-shot wait on the dispatcher: nothing is dispatched, so no dispatch operation is allocated per
        /// frame.
        /// </summary>
        private sealed class DispatcherFramePacer(IDispatcher dispatcher) : FramePacerCore
        {
            private IDispatcherTimer? _timer;

            protected override void Arm(TimeSpan interval)
            {
                var timer = _timer ??= CreateTimer();
                timer.Interval = interval;
                timer.Start();
            }

            protected override void Disarm() => _timer?.Stop();

            private IDispatcherTimer CreateTimer()
            {
                var timer = dispatcher.CreateTimer();
                timer.IsRepeating = false;
                timer.Tick += OnTick;
                return timer;
            }

            private void OnTick(object? sender, EventArgs e) => Fire();
        }
    }
}
