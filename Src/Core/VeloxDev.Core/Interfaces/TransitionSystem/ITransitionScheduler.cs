using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.TransitionSystem
{
    /// <summary>A frame scheduler whose priority type is <typeparamref name="TPriorityCore"/>.</summary>
    public interface ITransitionScheduler<TPriorityCore> : ITransitionSchedulerCore
    {
        /// <summary>Runs <paramref name="producer"/> against <paramref name="state"/> under <paramref name="effect"/>.</summary>
        /// <param name="producer">The interpolator that produces frames.</param>
        /// <param name="state">The frame state the run reads and writes.</param>
        /// <param name="effect">The effect that drives the run.</param>
        /// <param name="externCts">An optional external cancellation source.</param>
        public Task Execute(
            InterpolatorCore producer,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect,
            CancellationTokenSource? externCts = default);
    }

    /// <summary>The scheduler view without its priority type argument.</summary>
    public interface ITransitionScheduler : ITransitionSchedulerCore
    {
    }

    /// <summary>The core scheduling contract: run an interpolator against a frame state, and stop.</summary>
    public interface ITransitionSchedulerCore
    {
        /// <summary>Runs <paramref name="producer"/> against <paramref name="state"/> under <paramref name="effect"/>.</summary>
        /// <param name="producer">The interpolator that produces frames.</param>
        /// <param name="state">The frame state the run reads and writes.</param>
        /// <param name="effect">The effect that drives the run.</param>
        /// <param name="externCts">An optional external cancellation source.</param>
        public Task Execute(
            InterpolatorCore producer,
            IFrameState state,
            ITransitionEffectCore effect,
            CancellationTokenSource? externCts = default);

        /// <summary>Stops the scheduler.</summary>
        public void Exit();
    }
}
