namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionScheduler<TTarget, TUIThreadInspector, TOutput, TPriority> : ITransitionScheduler
        where TTarget : class
        where TUIThreadInspector : new()
        where TOutput : IFrameSequence<TPriority>
    {
        public void Execute(IFrameInterpolator<TOutput, TPriority> interpolator, IFrameState<TOutput, TPriority> state, ITransitionEffect<TPriority> effect);
        public void Exit();
    }

    public interface ITransitionScheduler
    {

    }
}
