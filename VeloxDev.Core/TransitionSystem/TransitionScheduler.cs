using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class TransitionSchedulerBase<TTarget, TOutput, TPriority> : ITransitionScheduler<TTarget, TOutput, TPriority>
        where TTarget : class
        where TOutput : IFrameSequence<TPriority>
    {
        public abstract Task Execute(
            IFrameInterpolator<TOutput, TPriority> interpolator,
            IFrameState<TOutput, TPriority> state,
            ITransitionEffect<TPriority> effect);

        public abstract void Exit();
    }
}
