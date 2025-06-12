namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionScheduler<TTarget, TOutput, TPriority> where TOutput : IFrameSequence<TPriority>
    {
        public void Execute(IFrameInterpolator<TTarget, TOutput, TPriority> interpolator, IFrameState<TTarget, TOutput, TPriority> state, ITransitionEffect<TPriority> effect);
        public void Exit();
    }
}
