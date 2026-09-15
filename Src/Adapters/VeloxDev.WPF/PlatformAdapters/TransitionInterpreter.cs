using System.Windows.Threading;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect, DispatcherPriority>
    {
        protected override FramePacerCore? CreateFramePacer(object target, IThreadAffinity affinity)
            => affinity.ThreadFor(target).TryGet<Dispatcher>(out var dispatcher)
                ? new DispatcherFramePacer(dispatcher)
                : null;

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

            public override void Dispose()
            {
                base.Dispose();

                // WPF 的定时器在它所属的 dispatcher 上排队，不停表就会在动画结束后继续占着它。
                _timer?.Stop();
                _timer = null;
            }

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
