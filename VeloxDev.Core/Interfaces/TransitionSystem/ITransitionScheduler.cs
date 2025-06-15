namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionScheduler<TPriorityCore> : ITransitionSchedulerCore
    {
        public void Execute(
            IFrameInterpolator<TPriorityCore> interpolator,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect);
        public void Exit();
    }

    public interface ITransitionScheduler : ITransitionSchedulerCore
    {
        public void Execute(
            IFrameInterpolator interpolator,
            IFrameState state, 
            ITransitionEffectCore effect);
        public void Exit();
    }

    public interface ITransitionSchedulerCore
    {

    }
}
