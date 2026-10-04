using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.TransitionSystem
{
    /// <summary>Executes one transition against a target, one frame at a time.</summary>
    public interface ITransitionInterpreter<TPriorityCore> : IDisposable
    {
        /// <summary>The event arguments of the current execution.</summary>
        public TransitionEventArgs Args { get; set; }

        /// <summary>Runs the transition described by <paramref name="samplerSet"/> and <paramref name="effect"/> against <paramref name="target"/>.</summary>
        /// <param name="target">The object whose properties are animated.</param>
        /// <param name="samplerSet">The samplers and frame state for this run.</param>
        /// <param name="effect">The effect that drives the run.</param>
        /// <param name="cts">The cancellation source for the run.</param>
        public Task Execute(
            object target,
            SamplerSet<TPriorityCore> samplerSet,
            ITransitionEffect<TPriorityCore> effect,
            CancellationTokenSource cts);

        /// <summary>Stops the interpreter.</summary>
        public void Exit();
    }
}
