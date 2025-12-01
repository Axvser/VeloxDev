namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionScheduler<TPriorityCore> : ITransitionSchedulerCore
    {
        public Task Execute(
            IFrameInterpolator<TPriorityCore> interpolator,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect,
            CancellationTokenSource? externCts = default);
    }

    public interface ITransitionScheduler : ITransitionSchedulerCore
    {
        public Task Execute(
            IFrameInterpolator interpolator,
            IFrameState state,
            ITransitionEffectCore effect,
            CancellationTokenSource? externCts = default);
    }

    public interface ITransitionSchedulerCore
    {
        public Task Execute(
            IFrameInterpolatorCore interpolator,
            IFrameState state,
            ITransitionEffectCore effect,
            CancellationTokenSource? externCts = default);
        public void Exit();
    }
}
