namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionInterpreter<TPriority> : ITransitionInterpreter, IDisposable
    {
        public Task Execute(
            object target,
            IFrameSequence<TPriority> frameSequence,
            ITransitionEffect<TPriority> effect,
            bool isUIAccess,
            CancellationTokenSource cts);

        public void Exit();
    }

    public interface ITransitionInterpreter
    {
        public FrameEventArgs Args { get; set; }
    }
}
