namespace VeloxDev.TransitionSystem
{
    public partial class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect>
    {
        protected override FramePacerCore? CreateFramePacer(object target, IUIThreadInspectorCore inspector)
        {
            if (inspector is IUIThreadAffinity affinity)
            {
                return affinity.ThreadFor(target) is IDispatcher dispatcher ? new DispatcherFramePacer(dispatcher) : null;
            }

            return UIThreadInspector.ApplicationDispatcher is { } fallback
                ? new DispatcherFramePacer(fallback)
                : null;
        }

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
