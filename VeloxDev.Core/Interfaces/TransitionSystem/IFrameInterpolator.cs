namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameInterpolator<TOutput, TPriority> : IFrameInterpolator
        where TOutput : IFrameSequence<TPriority>
    {
        public TOutput Interpolate(
            object target,
            IFrameState<TOutput, TPriority> state,
            ITransitionEffect<TPriority> effect);
    }

    public interface IFrameInterpolator
    {

    }
}