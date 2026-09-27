using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>An <see cref="IExecutionErrorSink"/> over a delegate — the one-liner form for a host that just counts or stores.</summary>
/// <param name="observe">Called once per recorded failure, on the thread driving the run.</param>
public sealed class DelegateExecutionErrorSink(Action<ExecutionError> observe) : IExecutionErrorSink
{
    private readonly Action<ExecutionError> _observe = observe ?? throw new ArgumentNullException(nameof(observe));

    /// <inheritdoc />
    public Task OnErrorAsync(ExecutionError error, CancellationToken cancellationToken)
    {
        _observe(error);
        return Task.CompletedTask;
    }
}
