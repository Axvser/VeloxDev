using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Padding), new PaddingSampler());
            RegisterInterpolator(typeof(Thickness), new ThicknessSampler());
        }

        /// <inheritdoc />
        public override TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect)
            => effect is ITransitionEffect<NonPriority>
                ? (TransitionSchedulerCore)TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, NonPriority>.FindOrCreate(target)
                : null;
    }
}
