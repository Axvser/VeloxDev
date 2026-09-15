using System;
using System.Threading;
using Avalonia.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect, DispatcherPriority>
    {
        // The one adapter that cannot answer IUIThreadAffinity — see Avalonia's UIThreadInspector — so this asks the
        // inspector rather than repeating its expression.
        protected override FramePacerCore? CreateFramePacer(object target, IUIThreadInspectorCore inspector)
            => inspector.IsUIThread() ? new UiThreadFramePacer() : null;

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
