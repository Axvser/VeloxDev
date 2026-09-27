using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>An <see cref="IExecutionObserver"/> over a delegate — the one-liner form for a host wiring a panel or a counter.</summary>
/// <param name="observe">Called once per observation, on the thread driving the run.</param>
public sealed class DelegateExecutionObserver(Action<ExecutionObservation> observe) : IExecutionObserver
{
    private readonly Action<ExecutionObservation> _observe = observe ?? throw new ArgumentNullException(nameof(observe));

    /// <inheritdoc />
    public Task OnObservedAsync(ExecutionObservation observation, CancellationToken cancellationToken)
    {
        _observe(observation);
        return Task.CompletedTask;
    }
}
