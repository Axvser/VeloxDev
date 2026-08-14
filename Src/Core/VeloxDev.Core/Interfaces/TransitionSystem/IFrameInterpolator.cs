namespace VeloxDev.TransitionSystem
{
    public interface IFrameInterpolator<TPriorityCore> : IFrameInterpolatorCore
    {
        public IFrameSequence<TPriorityCore> Interpolate(
            object target,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect,
            IUIThreadInspector<TPriorityCore> inspector);
    }

    public interface IFrameInterpolator : IFrameInterpolatorCore
    {
        public IFrameSequence Interpolate(
            object target,
            IFrameState state,
            ITransitionEffectCore effect,
            IUIThreadInspector inspector);
    }

    public interface IFrameInterpolatorCore
    {
        public IFrameSequenceCore Interpolate(
            object target,
            IFrameState state,
            ITransitionEffectCore effect,
            IUIThreadInspectorCore inspector);
    }
}
