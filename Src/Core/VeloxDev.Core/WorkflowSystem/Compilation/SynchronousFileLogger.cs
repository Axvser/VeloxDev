using System.IO;

namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Synchronous file-backed logger that writes <see cref="DiagnosticContext"/> entries
/// directly to a <see cref="StreamWriter"/> on every call — no background queue,
/// no flush delay. This ensures diagnostics survive even when the logger is used
/// briefly and never explicitly disposed.
/// </summary>
internal sealed class SynchronousFileLogger : IDiagnosticLogger, IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;

    public SynchronousFileLogger(TextWriter writer, bool ownsWriter = false)
    {
        _writer = writer;
        _ownsWriter = ownsWriter;
    }

    public void Log(DiagnosticContext context)
    {
        _writer.WriteLine(context.ToString());
        _writer.Flush();
    }

    public void LogWarning(DiagnosticContext context)
    {
        _writer.WriteLine(context.ToString());
        _writer.Flush();
    }

    public void LogError(DiagnosticContext context)
    {
        _writer.WriteLine(context.ToString());
        _writer.Flush();
    }

    public void Dispose()
    {
        if (_ownsWriter)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
