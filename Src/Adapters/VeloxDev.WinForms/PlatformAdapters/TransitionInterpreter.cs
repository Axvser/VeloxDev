namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect>
    {
        /// <summary>
        /// Waits each frame on the UI thread, so the sampling loop — and the effect's
        /// <c>Update</c>/<c>LateUpdate</c> callbacks — run there.
        /// </summary>
        /// <remarks>
        /// Only when the loop is already on the UI thread, recognised the same way the inspector recognises it. A
        /// <see cref="System.Windows.Forms.Timer"/> posts its tick to the thread that created it, so one built on a
        /// background thread would never fire and would strand the loop; leaving the thread-pool pacer in place
        /// instead is merely less ideal, not broken. A background first start therefore stays on the pool until the
        /// animation is restarted from the UI thread.
        /// </remarks>
        protected override FramePacerCore? CreateFramePacer()
            => SynchronizationContext.Current?.GetType().Name == "WindowsFormsSynchronizationContext"
                ? new FormsFramePacer()
                : null;

        /// <summary>
        /// A one-shot wait on the UI thread's message queue.
        /// </summary>
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
