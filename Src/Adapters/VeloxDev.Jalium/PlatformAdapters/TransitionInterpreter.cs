using System.Threading;
using Jalium.UI;
using Jalium.UI.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect, DispatcherPriority>
    {
        protected override FramePacerCore? CreateFramePacer(object target, IUIThreadInspectorCore inspector)
        {
            if (inspector is IUIThreadAffinity affinity)
            {
                return affinity.ThreadFor(target) is Dispatcher dispatcher ? new DispatcherFramePacer(dispatcher) : null;
            }

            return (Application.Current?.Dispatcher ?? Dispatcher.MainDispatcher) is { } appDispatcher
                ? new DispatcherFramePacer(appDispatcher)
                : null;
        }

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
