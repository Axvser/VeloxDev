using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class LinearInterpolatorBase<TOutput, TPriority> : IFrameInterpolator<TOutput, TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public abstract TOutput Interpolate(object target, IFrameState<TOutput, TPriority> state, ITransitionEffect<TPriority> effect);
    }
}
