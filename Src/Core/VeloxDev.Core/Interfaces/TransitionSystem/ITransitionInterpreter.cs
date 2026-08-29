using VeloxDev.TimeLine;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.TransitionSystem
{
    public interface ITransitionInterpreter<TPriorityCore> : ITransitionInterpreterCore
    {
        public Task Execute(
            object target,
            SamplerSet samplerSet,
            ITransitionEffect<TPriorityCore> effect,
            CancellationTokenSource cts);
    }

    public interface ITransitionInterpreter : ITransitionInterpreterCore
    {
    }

    public interface ITransitionInterpreterCore : IDisposable
    {
        public TransitionEventArgs Args { get; set; }
        public Task Execute(
            object target,
            SamplerSet samplerSet,
            ITransitionEffectCore effect,
            CancellationTokenSource cts);
        public void Exit();
    }
}
