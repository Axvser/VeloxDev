namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameInterpolator<TPriorityCore> : IFrameInterpolatorCore
    {
        public IFrameSequence<TPriorityCore> Interpolate(
            object target,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect);
    }

    public interface IFrameInterpolator : IFrameInterpolatorCore
    {
        public IFrameSequence Interpolate(
            object target,
            IFrameState state,
            ITransitionEffectCore effect);
    }

    public interface IFrameInterpolatorCore
    {

    }
}