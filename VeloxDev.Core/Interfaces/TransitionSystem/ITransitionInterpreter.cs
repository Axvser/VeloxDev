namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionInterpreter<TTransitionEffectCore, TPriorityCore> : ITransitionInterpreterCore
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
    {
        public Task Execute(
            object target,
            IFrameSequence<TPriorityCore> frameSequence,
            ITransitionEffect<TTransitionEffectCore, TPriorityCore> effect,
            bool isUIAccess,
            CancellationTokenSource cts);
    }

    public interface ITransitionInterpreter<TTransitionEffectCore> : ITransitionInterpreterCore
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
    {
        public Task Execute(
            object target,
            IFrameSequence frameSequence,
            ITransitionEffect<TTransitionEffectCore> effect,
            bool isUIAccess,
            CancellationTokenSource cts);
    }

    public interface ITransitionInterpreterCore : IDisposable
    {
        public FrameEventArgs Args { get; set; }
        public void Exit();
    }
}
