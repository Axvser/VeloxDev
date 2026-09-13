using VeloxDev.Adapters.NativeSamplers;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(Padding), new PaddingSampler());
        }

        public override TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect)
            => effect is ITransitionEffect<NonPriority>
                ? (TransitionSchedulerCore)TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, NonPriority>.FindOrCreate(target)
                : null;
    }
}
