namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionScheduler<TTarget, TOutput, TPriority> : ITransitionScheduler
        where TTarget : class
        where TOutput : IFrameSequence<TPriority>
    {
        public Task Execute(IFrameInterpolator<TOutput, TPriority> interpolator, IFrameState<TOutput, TPriority> state, ITransitionEffect<TPriority> effect);
        public void Exit();
    }

    public interface ITransitionScheduler
    {

    }
}
