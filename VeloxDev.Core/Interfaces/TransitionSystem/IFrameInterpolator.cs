namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameInterpolator<TTransitionEffectCore, TStateCore, TOutputCore, TPriorityCore> : IFrameInterpolatorCore
        where TStateCore : IFrameState<TStateCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TOutputCore : IFrameSequence<TPriorityCore>
    {
        public TOutputCore Interpolate(
            object target,
            IFrameState<TStateCore> state,
            ITransitionEffect<TTransitionEffectCore, TPriorityCore> effect);
    }

    public interface IFrameInterpolator<TTransitionEffectCore, TStateCore, TOutputCore> : IFrameInterpolatorCore
        where TStateCore : IFrameState<TStateCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TOutputCore : IFrameSequence
    {
        public TOutputCore Interpolate(
            object target,
            IFrameState<TStateCore> state,
            ITransitionEffect<TTransitionEffectCore> effect);
    }

    public interface IFrameInterpolatorCore
    {

    }
}