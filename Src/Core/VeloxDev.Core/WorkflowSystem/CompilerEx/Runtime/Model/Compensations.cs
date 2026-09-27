using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// An <see cref="IExecutionCompensation"/> over a delegate — the one-liner form for a host undoing one thing per node.
/// </summary>
/// <param name="compensate">Called once per successfully driven node, most recent first.</param>
public sealed class DelegateExecutionCompensation(Action<NodeCompensation> compensate) : IExecutionCompensation
{
    private readonly Action<NodeCompensation> _compensate = compensate ?? throw new ArgumentNullException(nameof(compensate));

    /// <inheritdoc />
    public Task CompensateAsync(NodeCompensation compensation, CancellationToken cancellationToken)
    {
        _compensate(compensation);
        return Task.CompletedTask;
    }
}
