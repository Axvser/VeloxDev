namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameInterpolator<TPriorityCore> : IFrameInterpolatorCore
    {
        public IFrameSequence<TPriorityCore> Interpolate(
            object target,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect,
            bool isUIAccess,
            IUIThreadInspector<TPriorityCore> inspector);
    }

    public interface IFrameInterpolator : IFrameInterpolatorCore
    {
        public IFrameSequence Interpolate(
            object target,
            IFrameState state,
            ITransitionEffectCore effect,
            bool isUIAccess,
            IUIThreadInspector inspector);
    }

    public interface IFrameInterpolatorCore
    {
        public IFrameSequenceCore Interpolate(
            object target,
            IFrameState state,
            ITransitionEffectCore effect,
            bool isUIAccess,
            IUIThreadInspectorCore inspector);
    }
}