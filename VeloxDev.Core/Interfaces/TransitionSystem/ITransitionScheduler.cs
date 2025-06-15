namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionScheduler<
        TSchedulerTargetCore, 
        TUIThreadInspectorCore, 
        TOutputCore, 
        TStateCore, 
        TTransitionEffectCore, 
        TPriorityCore> : ITransitionSchedulerCore
        where TStateCore : IFrameState<TStateCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TSchedulerTargetCore : class
        where TUIThreadInspectorCore : new()
        where TOutputCore : IFrameSequence<TPriorityCore>
    {
        public void Execute(IFrameInterpolator<TTransitionEffectCore, TStateCore, TOutputCore, TPriorityCore> interpolator, IFrameState<TStateCore> state, ITransitionEffect<TTransitionEffectCore, TPriorityCore> effect);
        public void Exit();
    }

    public interface ITransitionScheduler<
        TSchedulerTargetCore, 
        TUIThreadInspectorCore, 
        TOutputCore, 
        TStateCore, 
        TTransitionEffectCore> : ITransitionSchedulerCore
        where TStateCore : IFrameState<TStateCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TSchedulerTargetCore : class
        where TUIThreadInspectorCore : new()
        where TOutputCore : IFrameSequence
    {
        public void Execute(IFrameInterpolator<TTransitionEffectCore, TStateCore, TOutputCore> interpolator, IFrameState<TStateCore> state, ITransitionEffect<TTransitionEffectCore> effect);
        public void Exit();
    }

    public interface ITransitionSchedulerCore
    {

    }
}
