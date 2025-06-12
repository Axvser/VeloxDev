using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class LinearInterpolatorBase<TTarget, TOutput, TPriority> : IFrameInterpolator<TTarget, TOutput, TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public abstract TOutput Interpolate(TTarget target, IFrameState<TTarget, TOutput, TPriority> state, ITransitionEffect<TPriority> effect);
    }
}
