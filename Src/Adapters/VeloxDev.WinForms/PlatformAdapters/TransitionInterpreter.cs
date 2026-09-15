using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect>
    {
        // A Forms.Timer can only be built on the thread it will tick on, so the pacer is taken only when the caller
        // is already on the target's thread.
        protected override FramePacerCore? CreateFramePacer(object target, IThreadAffinity affinity)
            => affinity.IsCurrent(target) ? new FormsFramePacer() : null;

        private sealed class FormsFramePacer : FramePacerCore
        {
            private System.Windows.Forms.Timer? _timer;

            protected override void Arm(TimeSpan interval)
            {
                var timer = _timer ??= CreateTimer();
                // WinForms 的间隔是 int 毫秒，不是 TimeSpan，而且要大于零。
                timer.Interval = (int)Math.Max(1d, interval.TotalMilliseconds);
                timer.Start();
            }

            protected override void Disarm() => _timer?.Stop();

            private System.Windows.Forms.Timer CreateTimer()
            {
                var timer = new System.Windows.Forms.Timer();
                timer.Tick += OnTick;
                return timer;
            }

            private void OnTick(object? sender, EventArgs e) => Fire();

            public override void Dispose()
            {
                base.Dispose();

                // WinForms 的定时器是可释放的，而且只有在这里才能确定不再需要它——基类只管停表与放行续体。
                if (_timer is not null)
                {
                    _timer.Tick -= OnTick;
                    _timer.Dispose();
                    _timer = null;
                }
            }
        }
    }
}
