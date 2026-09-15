using Microsoft.Maui.Dispatching;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public partial class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect>
    {
        protected override FramePacerCore? CreateFramePacer(object target, IThreadAffinity affinity)
            => affinity.ThreadFor(target).TryGet<IDispatcher>(out var dispatcher)
                ? new DispatcherFramePacer(dispatcher)
                : null;

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

            public override void Dispose()
            {
                base.Dispose();

                if (_timer is not null)
                {
                    _timer.Stop();
                    _timer.Tick -= OnTick;
                    _timer = null;
                }
            }

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
