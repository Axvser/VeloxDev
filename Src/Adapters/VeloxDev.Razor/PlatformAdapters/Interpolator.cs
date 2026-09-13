using VeloxDev.Adapters.NativeSamplers;

namespace VeloxDev.TransitionSystem
{
    public class Interpolator : InterpolatorCore
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(string), new StringSampler());
        }

        public override TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect)
            => effect is ITransitionEffect<NonPriority>
                ? (TransitionSchedulerCore)TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, NonPriority>.FindOrCreate(target)
                : null;
    }
}
