using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    internal class TransitionBoardItem<TPriorityCore>(
        object target,
        IFrameState state,
        ITransitionEffect<TPriorityCore> effect,
        IFrameInterpolator<TPriorityCore> interpolator) : ITransitionBoardItem<TPriorityCore>
    {
        public object Target { get; set; } = target;
        public IFrameState State { get; set; } = state;
        public ITransitionEffect<TPriorityCore> Effect { get; set; } = effect;
        public IFrameInterpolator<TPriorityCore> Interpolator { get; set; } = interpolator;
        public IFrameSequence<TPriorityCore>? FrameSequence { get; set; } = null;
        public TimeSpan StartOffset { get; set; }
        public TimeSpan Duration => Effect.Duration;
        public bool IsCompleted { get; set; }
    }

    internal class TransitionBoardItem(
        object target,
        IFrameState state,
        ITransitionEffectCore effect,
        IFrameInterpolator interpolator) : ITransitionBoardItem
    {
        public object Target { get; set; } = target;
        public IFrameState State { get; set; } = state;
        public ITransitionEffectCore Effect { get; set; } = effect;
        public IFrameInterpolator Interpolator { get; set; } = interpolator;
        public IFrameSequence? FrameSequence { get; set; } = null;
        public TimeSpan StartOffset { get; set; }
        public TimeSpan Duration => Effect.Duration;
        public bool IsCompleted { get; set; }
    }
}
