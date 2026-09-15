using System;
using Avalonia.Threading;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect, DispatcherPriority>
    {
        protected override FramePacerCore? CreateFramePacer(object target, IThreadAffinity affinity)
            => affinity.ThreadFor(target).IsNone ? null : new UiThreadFramePacer();

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

            public override void Dispose()
            {
                base.Dispose();

                // Avalonia 的 DispatcherTimer 持有平台定时器，只停表不够。
                if (_timer is not null)
                {
                    _timer.Stop();
                    _timer.Tick -= OnTick;
                    _timer = null;
                }
            }

            private DispatcherTimer CreateTimer()
            {
                // Avalonia 只有一个 UI dispatcher，无参构造就绑到它。
                var timer = new DispatcherTimer();
                timer.Tick += OnTick;
                return timer;
            }

            private void OnTick(object? sender, EventArgs e) => Fire();
        }
    }
}
