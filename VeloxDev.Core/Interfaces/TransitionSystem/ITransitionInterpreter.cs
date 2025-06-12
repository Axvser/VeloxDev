namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface ITransitionInterpreter<TPriority> : IDisposable
    {
        public Task Execute(
            object target,
            IFrameSequence<TPriority> frameSequence,
            ITransitionEffect<TPriority> effect,
            bool isUIAccess,
            CancellationTokenSource cts);

        public void Exit();
    }
}
