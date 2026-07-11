namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Lightweight diagnostic logger for debugging the workflow compiler.
/// Call <see cref="IDiagnosticLogger.Log"/>, <see cref="LogWarning"/>, and <see cref="LogError"/>
/// from the compiler pipeline to trace topology traversal and cycle detection.
/// Each method accepts a <see cref="DiagnosticContext"/> that carries a timestamp,
/// a state-machine identifier, a content-type label, and the message body.
/// Use <c>_logger?.Log(ctx)</c> so the call site works in both Debug and Release
/// builds without any conditional compilation (null-conditional is a no-op).
/// </summary>
public interface IDiagnosticLogger
{
    /// <summary>Writes an informational diagnostic entry.</summary>
    void Log(DiagnosticContext context);

    /// <summary>Writes a warning diagnostic entry.</summary>
    void LogWarning(DiagnosticContext context);

    /// <summary>Writes an error diagnostic entry.</summary>
    void LogError(DiagnosticContext context);
}
