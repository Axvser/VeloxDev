namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionScheduler<TTarget, TUIThreadInspector, TOutput, TPriority> : ITransitionScheduler
        where TTarget : class
        where TUIThreadInspector : new()
        where TOutput : IFrameSequence<TPriority>
    {
        public void Execute(IFrameInterpolator<TOutput, TPriority> interpolator, IFrameState state, ITransitionEffect<TPriority> effect);
    }

    public interface ITransitionScheduler
    {
        public void Exit();
    }
}
