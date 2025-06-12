using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class LinearInterpolatorBase<TTarget, TOutput, TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public abstract TOutput Interpolate(
            TTarget? start,
            TTarget? end,
            ITransitionEffect<TPriority> effect);
    }
}
