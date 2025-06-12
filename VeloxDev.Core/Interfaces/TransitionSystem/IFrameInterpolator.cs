namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameInterpolator<TOutput, TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public TOutput Interpolate(
            object? start,
            object? end,
            ITransitionEffect<TPriority> effect);
    }
}