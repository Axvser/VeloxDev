using VeloxDev.TimeLine;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.TransitionSystem
{
    public interface ITransitionInterpreter<TPriorityCore> : IDisposable
    {
        public TransitionEventArgs Args { get; set; }

        public Task Execute(
            object target,
            SamplerSet<TPriorityCore> samplerSet,
            ITransitionEffect<TPriorityCore> effect,
            CancellationTokenSource cts);

        public void Exit();
    }
}
