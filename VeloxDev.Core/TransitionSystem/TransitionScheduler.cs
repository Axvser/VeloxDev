using System.Runtime.CompilerServices;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class TransitionSchedulerBase<TUpdator, TOutput, TPriority> : ITransitionScheduler<TOutput, TPriority>
        where TUpdator : IFrameUpdator<TPriority>
        where TOutput : IFrameSequence<TPriority>
    {
        public abstract void Execute(
            IFrameInterpolator<TOutput, TPriority> interpolator,
            ITransitionEffect<TPriority> effect);

        public abstract void Exit();
    }
}
