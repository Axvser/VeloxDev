namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionBoardItem<TPriorityCore>
    {
        object Target { get; }
        IFrameState State { get; }
        ITransitionEffect<TPriorityCore> Effect { get; }
        IFrameInterpolator<TPriorityCore> Interpolator { get; }
        IFrameSequence<TPriorityCore>? FrameSequence { get; set; }
        TimeSpan StartOffset { get; set; }
        TimeSpan Duration { get; }
        bool IsCompleted { get; set; }
    }

    public interface ITransitionBoardItem
    {
        object Target { get; }
        IFrameState State { get; }
        ITransitionEffectCore Effect { get; }
        IFrameInterpolator Interpolator { get; }
        IFrameSequence? FrameSequence { get; set; }
        TimeSpan StartOffset { get; set; }
        TimeSpan Duration { get; }
        bool IsCompleted { get; set; }
    }
}
