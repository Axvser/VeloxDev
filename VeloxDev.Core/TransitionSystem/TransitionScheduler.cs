using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class TransitionSchedulerBase<TTarget, TOutput, TPriority> : ITransitionScheduler<TTarget, TOutput, TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public abstract void Execute(
            IFrameInterpolator<TTarget, TOutput, TPriority> interpolator,
            IFrameState<TTarget, TOutput, TPriority> state,
            ITransitionEffect<TPriority> effect);

        public abstract void Exit();
    }
}
