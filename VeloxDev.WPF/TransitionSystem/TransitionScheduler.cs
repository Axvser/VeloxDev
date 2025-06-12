using System.Windows.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public class TransitionScheduler(object target) : TransitionSchedulerBase<InterpolatorOutput, DispatcherPriority>
    {
        private WeakReference<object>? targetref = new(target);
        private CancellationTokenSource? cts = null;

        public override void Execute(IFrameInterpolator<InterpolatorOutput, DispatcherPriority> interpolator, IFrameState<InterpolatorOutput, DispatcherPriority> state, ITransitionEffect<DispatcherPriority> effect)
        {
            if (targetref is null || !targetref.TryGetTarget(out var target))
            {
                targetref = null;
                return;
            }
            Exit();
            var type = targetref.GetType();
        }

        public override void Exit()
        {
            var oldCts = Interlocked.Exchange(ref cts, null);
            oldCts?.Cancel();
        }
    }
}
