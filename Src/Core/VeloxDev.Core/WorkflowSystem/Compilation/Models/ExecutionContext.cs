namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Context passed to <see cref="ICompileTimeSink.OnExecutionEvent"/>.
/// Carries the current position, parameter, and event type.
/// </summary>
public readonly struct ExecutionContext
{
    /// <summary>Index of the current item being processed (0-based).</summary>
    public int CurrentIndex { get; }

    /// <summary>Total number of items in the execution chain.</summary>
    public int TotalCount { get; }

    /// <summary>The parameter passed to or returned by the current node.</summary>
    public object? Parameter { get; }

    /// <summary>The event type.</summary>
    public ExecutionEvent Event { get; }

    /// <summary>The CompiledItem being processed (null for OnCompleted).</summary>
    public CompiledItem? Item { get; }

    public ExecutionContext(int currentIndex, int totalCount,
        object? parameter, ExecutionEvent @event, CompiledItem? item)
    {
        CurrentIndex = currentIndex;
        TotalCount = totalCount;
        Parameter = parameter;
        Event = @event;
        Item = item;
    }
}