using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.TransitionSystem
{
    public interface ITransitionScheduler<TPriorityCore> : ITransitionSchedulerCore
    {
        public Task Execute(
            InterpolatorCore producer,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect,
            CancellationTokenSource? externCts = default);
    }

    public interface ITransitionScheduler : ITransitionSchedulerCore
    {
    }

    public interface ITransitionSchedulerCore
    {
        public Task Execute(
            InterpolatorCore producer,
            IFrameState state,
            ITransitionEffectCore effect,
            CancellationTokenSource? externCts = default);
        public void Exit();
    }
}
