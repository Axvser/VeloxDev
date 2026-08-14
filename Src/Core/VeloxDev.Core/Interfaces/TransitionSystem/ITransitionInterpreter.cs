using VeloxDev.TimeLine;

namespace VeloxDev.TransitionSystem
{
    public interface ITransitionInterpreter<TPriorityCore> : ITransitionInterpreterCore
    {
        public Task Execute(
            object target,
            IFrameSequence<TPriorityCore> frameSequence,
            ITransitionEffect<TPriorityCore> effect,
            CancellationTokenSource cts);
    }

    public interface ITransitionInterpreter : ITransitionInterpreterCore
    {
        public Task Execute(
            object target,
            IFrameSequence frameSequence,
            ITransitionEffectCore effect,
            CancellationTokenSource cts);
    }

    public interface ITransitionInterpreterCore : IDisposable
    {
        public TransitionEventArgs Args { get; set; }
        public Task Execute(
            object target,
            IFrameSequenceCore frameSequence,
            ITransitionEffectCore effect,
            CancellationTokenSource cts);
        public void Exit();
    }
}
