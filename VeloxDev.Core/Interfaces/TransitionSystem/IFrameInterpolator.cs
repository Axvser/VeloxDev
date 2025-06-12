namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameInterpolator<TTarget, TOutput, TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public TOutput Interpolate(
            TTarget target,
            IFrameState<TTarget, TOutput, TPriority> state,
            ITransitionEffect<TPriority> effect);
    }
}