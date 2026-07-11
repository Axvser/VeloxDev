namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Structured diagnostic entry for the workflow compiler execution log.
/// Each instance carries a timestamp, the originating state-machine ID,
/// a content-type label describing what phase produced the entry,
/// the severity level, and the free-text message body.
/// </summary>
public sealed class DiagnosticContext(DateTimeOffset timestamp, Guid machineId,
    string contentType, string level, string message)
{
    /// <summary>UTC timestamp at entry creation, precise to the millisecond.</summary>
    public DateTimeOffset Timestamp { get; } = timestamp;

    /// <summary>
    /// Unique identifier for this state-machine run.
    /// Each <see cref="WorkflowCompiler.Compile"/> call generates a new ID
    /// so log entries from different compilation sessions can be distinguished.
    /// </summary>
    public Guid MachineId { get; } = machineId;

    /// <summary>
    /// Content-type label describing which compiler phase produced the entry.
    /// Examples: <c>Compile</c>, <c>Adjacency</c>, <c>Cycle</c>, <c>BFS</c>,
    /// <c>DFS</c>, <c>Omni</c>, <c>Routes</c>, <c>Exclusive</c>, <c>Loop</c>,
    /// <c>Execute</c>.
    /// </summary>
    public string ContentType { get; } = contentType;

    /// <summary>Severity level: <c>Info</c>, <c>Warning</c>, or <c>Error</c>.</summary>
    public string Level { get; } = level;

    /// <summary>The free-text diagnostic message body.</summary>
    public string Message { get; } = message;

    /// <summary>
    /// Formats as:
    /// <c>[ 2025-03-15 10:30:45.123 ] [ a1b2c3d4e5f6 ] [ Info ]  message</c>
    /// </summary>
    public override string ToString() =>
        $"[ {Timestamp:yyyy-MM-dd HH:mm:ss.fff} ] [ {MachineId:N} ] [ {Level} ]  {Message}";
}
