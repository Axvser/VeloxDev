namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionScheduler<TPriorityCore> : ITransitionSchedulerCore
    {
        public Task Execute(
            IFrameInterpolator<TPriorityCore> interpolator,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect);
    }

    public interface ITransitionScheduler : ITransitionSchedulerCore
    {
        public Task Execute(
            IFrameInterpolator interpolator,
            IFrameState state,
            ITransitionEffectCore effect);
    }

    public interface ITransitionSchedulerCore
    {
        public Task Execute(
            IFrameInterpolatorCore interpolator,
            IFrameState state,
            ITransitionEffectCore effect);
        public void Exit();
    }
}
